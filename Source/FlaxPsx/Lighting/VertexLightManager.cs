// www.acidicvoid.com

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Collects all Flax-PSX vertex lights, evaluates animations, culls and sorts them,
/// packs them into the light data texture and uploads it once per frame.
/// The texture is bound to every material that has a <see cref="MaterialParameterName"/> parameter.
/// </summary>
/// <remarks>
/// Light data texture layout (R32G32B32A32_Float, one row, width = 1 + 4 * cap; HLSL: In1.Load(int3(x, 0, 0))).
/// One texel = one float4 element:
/// <list type="bullet">
/// <item>Element 0 (header): x = light count, yzw = ambient colour.</item>
/// <item>Light i starts at element 1 + 4 * i:</item>
/// <item>E0: position.xyz, 1 / range (0 = no range limit, directional; area: 1 / (range + shape radius), so the test covers the whole shape)</item>
/// <item>E1: colour.rgb (premultiplied by brightness and animation), type</item>
/// <item>E2: direction.xyz, falloff (spot: distance falloff + cone falloff * 32)</item>
/// <item>E3: shape (area only: 1 = rectangle, 2 = circle), halfSize.x, halfSize.y, angle (spot: cos(angle / 2), area: roll)</item>
/// </list>
/// The range test only needs E0, so the shader can skip loading E1–E3 for lights out of range.
/// A texture is used instead of a GPU Buffer parameter because the 1.13 editor can't open material instances
/// of materials with GPU Buffer parameters (SurfaceUtils.ToType has no case for it).
/// <para>
/// The texture holds neutral data (no lights, ambient 1 = unlit) by default. Only render tasks that render the scene
/// (<see cref="AddLitTask"/>) see the real light data, so asset previews and thumbnails render unlit.
/// </para>
/// <para>
/// Materials are bound explicitly, not by scanning: the Flax-PSX base materials automatically,
/// other materials via <see cref="RegisterMaterial"/>. Material instances use their base material's binding.
/// </para>
/// </remarks>
public static class VertexLightManager
{
    /// <summary>
    /// Name of the material parameter receiving the light data texture. Must be the material's first parameter (GPU Texture).
    /// </summary>
    public const string MaterialParameterName = "PsxLightData";

    /// <summary>
    /// float4 elements (texels) per light in the light data texture.
    /// </summary>
    public const int ElementsPerLight = 4;

    /// <summary>
    /// Range margin used for frustum culling (+5%).
    /// </summary>
    public const float CullingRangeMargin = 1.05f;

    /// <summary>
    /// Flax-PSX base materials using vertex lighting, registered automatically (asset IDs).
    /// </summary>
    private static readonly string[] PluginMaterialIds =
    {
        "02d5c97140b2fe13e5c560a65534b453", // PSX Surface Opaque
        "0bac7c1a44e88c6c0cd2d1bf2804797e", // PSX Surface Opaque Two-Sided
        "7d36f2d24542bc48fe64eba26a487b90", // PSX Surface Triplanar Opaque
        "7af16ca24189080723678b85d109fa3e", // PSX Liquid
    };

    /// <summary>
    /// Provides a reference view (position and frustum) for culling and sorting. Return false to fall back to the camera.
    /// </summary>
    public delegate bool ReferenceViewDelegate(out Vector3 position, out BoundingFrustum frustum);

    private static readonly List<PsxVertexLight> _lights = new List<PsxVertexLight>();
    private static readonly List<MaterialBase> _materials = new List<MaterialBase>();
    private static readonly HashSet<Guid> _reportedMaterials = new HashSet<Guid>();
    private static PsxVertexLight[] _candidates = new PsxVertexLight[64];
    private static float[] _keys = new float[64];
    private static Float4[] _data = Array.Empty<Float4>();
    private static Float4[] _neutralData = Array.Empty<Float4>();
    private static readonly List<SceneRenderTask> _litTasks = new List<SceneRenderTask>();
    private static GPUTexture _texture;
    private static RenderTask _task;
    private static MainRenderTask _mainTask;
    private static PsxVertexLightCap _cap = PsxVertexLightCap.Lights64;
    private static bool _textureDirty = true;
    private static bool _initialized;

    /// <summary>
    /// Maximum number of lights uploaded per frame. Changing it reallocates the light data texture.
    /// </summary>
    public static PsxVertexLightCap Cap
    {
        get => _cap;
        set
        {
            if (_cap == value)
                return;
            _cap = value;
            _textureDirty = true;
        }
    }

    /// <summary>
    /// Enables frustum culling against the reference view. Disable it while two or more cameras are active,
    /// since the light data texture is shared by every view in a frame.
    /// </summary>
    public static bool CullingEnabled { get; set; } = true;

    /// <summary>
    /// Ambient light colour, sent through the header element.
    /// </summary>
    public static Color Ambient { get; set; } = new Color(0.2f, 0.2f, 0.2f, 1.0f);

    /// <summary>
    /// Camera used for culling and sorting. If not set, <see cref="Camera.MainCamera"/> is used.
    /// </summary>
    public static Camera ReferenceCamera { get; set; }

    /// <summary>
    /// Optional reference view provider, checked before <see cref="ReferenceCamera"/>.
    /// The editor module uses it to supply the editor viewport camera in edit mode.
    /// </summary>
    public static ReferenceViewDelegate ReferenceViewProvider { get; set; }

    /// <summary>
    /// The light data texture (null until the first frame after initialization).
    /// </summary>
    public static GPUTexture LightDataTexture => _texture;

    /// <summary>
    /// All registered (enabled) vertex lights.
    /// </summary>
    public static IReadOnlyList<PsxVertexLight> Lights => _lights;

    /// <summary>
    /// Materials the light data texture is bound to.
    /// </summary>
    public static IReadOnlyList<MaterialBase> Materials => _materials;

    /// <summary>
    /// Number of lights that passed culling in the last frame (before applying the cap).
    /// </summary>
    public static int VisibleLightCount { get; private set; }

    /// <summary>
    /// Number of lights uploaded in the last frame.
    /// </summary>
    public static int UploadedLightCount { get; private set; }

    /// <summary>
    /// Initializes the manager. Called by the Flax-PSX game plugin.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        // Plugin-owned render task with the lowest order, so the upload runs before every scene render
        _task = FlaxEngine.Object.New<RenderTask>();
        _task.Order = int.MinValue;
        _task.Enabled = true;
        _task.Render += OnRender;

        _textureDirty = true;

        // Flax-PSX base materials (materials without the light data parameter are ignored)
        for (int i = 0; i < PluginMaterialIds.Length; i++)
        {
            var material = Content.LoadAsync<MaterialBase>(FlaxEngine.Json.JsonSerializer.ParseID(PluginMaterialIds[i]));
            if (material)
                RegisterMaterial(material);
        }
    }

    /// <summary>
    /// Releases all resources. Called by the Flax-PSX game plugin.
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized)
            return;
        _initialized = false;

        if (_task)
        {
            _task.Render -= OnRender;
            _task.Enabled = false;
            FlaxEngine.Object.Destroy(ref _task);
        }

        // Unbind the texture from all materials before releasing it
        for (int i = 0; i < _materials.Count; i++)
        {
            var material = _materials[i];
            if (!material)
                continue;
            material.Loaded -= OnMaterialLoaded;
            var parameter = material.GetParameter(MaterialParameterName);
            if (parameter != null && parameter.Value as GPUTexture == _texture)
                parameter.Value = null;
        }
        _materials.Clear();
        _reportedMaterials.Clear();

        for (int i = 0; i < _litTasks.Count; i++)
        {
            var task = _litTasks[i];
            if (task)
            {
                task.PreRender -= OnLitTaskPreRender;
                task.PostRender -= OnLitTaskPostRender;
            }
        }
        _litTasks.Clear();
        _mainTask = null;

        if (_texture)
        {
            _texture.ReleaseGPU();
            FlaxEngine.Object.Destroy(ref _texture);
        }
        _textureDirty = true;
        VisibleLightCount = 0;
        UploadedLightCount = 0;
    }

    /// <summary>
    /// Registers a light. Called by <see cref="PsxVertexLight"/> when enabled.
    /// </summary>
    internal static void Register(PsxVertexLight light)
    {
        if (!light || _lights.Contains(light))
            return;
        _lights.Add(light);
        if (light.LightType == PsxLightType.Directional)
            ResolveDirectional(light);
    }

    /// <summary>
    /// Called by <see cref="PsxVertexLight"/> when its type changes.
    /// </summary>
    internal static void OnLightTypeChanged(PsxVertexLight light)
    {
        if (light && light.LightType == PsxLightType.Directional && _lights.Contains(light))
            ResolveDirectional(light);
    }

    /// <summary>
    /// Returns the active directional light of a scene: the registered directional light with the highest
    /// stamp, ties go to the one registered first. Null if the scene has no registered directional light.
    /// </summary>
    /// <param name="scene">The scene (null for lights outside of any scene, e.g. in a prefab preview).</param>
    public static PsxVertexLight GetActiveDirectional(Scene scene)
    {
        PsxVertexLight best = null;
        for (int i = 0; i < _lights.Count; i++)
        {
            var light = _lights[i];
            if (!light || light.LightType != PsxLightType.Directional || light.Scene != scene)
                continue;
            if (best == null || light.ActiveStamp > best.ActiveStamp)
                best = light;
        }
        return best;
    }

    /// <summary>
    /// True if the light is a registered directional light and the active one of its scene.
    /// </summary>
    /// <param name="light">The light.</param>
    public static bool IsActiveDirectional(PsxVertexLight light)
    {
        return light && light.LightType == PsxLightType.Directional && _lights.Contains(light) && GetActiveDirectional(light.Scene) == light;
    }

    /// <summary>
    /// Makes the light the active directional light of its scene. Only one directional light per scene is used.
    /// The choice is stored in the light (serialized with the scene), so it survives saving and reloading.
    /// </summary>
    /// <param name="light">The light. Must be a directional light.</param>
    public static void SetActiveDirectional(PsxVertexLight light)
    {
        if (!light)
            return;
        if (light.LightType != PsxLightType.Directional)
        {
            Debug.LogWarning($"[VertexLightManager] '{light.Name}' is not a directional light and can't be the active directional light.");
            return;
        }
        if (IsActiveDirectional(light))
            return;

        // Outrank every other directional light of the scene
        var scene = light.Scene;
        int max = 0;
        for (int i = 0; i < _lights.Count; i++)
        {
            var other = _lights[i];
            if (other && other != light && other.LightType == PsxLightType.Directional && other.Scene == scene)
                max = Math.Max(max, other.ActiveStamp);
        }
        light.ActiveStamp = max + 1;
    }

    /// <summary>
    /// Keeps the active directional light stable when another directional light appears in the same scene.
    /// Never changes which light is active, only makes the current choice persistent.
    /// </summary>
    private static void ResolveDirectional(PsxVertexLight light)
    {
        var scene = light.Scene;
        PsxVertexLight best = null;
        for (int i = 0; i < _lights.Count; i++)
        {
            var other = _lights[i];
            if (!other || other == light || other.LightType != PsxLightType.Directional || other.Scene != scene)
                continue;
            if (best == null || other.ActiveStamp > best.ActiveStamp)
                best = other;
        }
        if (best == null)
            return;

        if (light.ActiveStamp == best.ActiveStamp)
        {
            if (best.ActiveStamp == 0)
            {
                // Neither was chosen explicitly yet: freeze the current active one (otherwise hierarchy order would decide after reloading)
                best.ActiveStamp = 1;
            }
            else
            {
                // Copy of the active light (e.g. duplicated): the original stays active
                light.ActiveStamp = 0;
            }
        }
    }

    /// <summary>
    /// Unregisters a light. Called by <see cref="PsxVertexLight"/> when disabled.
    /// </summary>
    internal static void Unregister(PsxVertexLight light)
    {
        _lights.Remove(light);
    }

    /// <summary>
    /// Binds the light data texture to a material that uses <c>PsxVertexLighting</c> (the Flax-PSX base materials are registered automatically).
    /// The material is kept loaded and rebound whenever it's reloaded (e.g. after editing it). Material instances inherit the binding.
    /// </summary>
    /// <param name="material">The material (may still be loading).</param>
    /// <returns>True if the material is loaded and has a valid light data parameter.</returns>
    public static bool RegisterMaterial(MaterialBase material)
    {
        if (!material)
            return false;
        if (!_materials.Contains(material))
        {
            // Holding the managed object keeps the asset loaded; Loaded also fires after reloads, which reset parameter values
            _materials.Add(material);
            material.Loaded += OnMaterialLoaded;
        }
        return BindMaterial(material);
    }

    /// <summary>
    /// Removes a material from the binding list and clears its light data parameter.
    /// </summary>
    /// <param name="material">The material.</param>
    public static void UnregisterMaterial(MaterialBase material)
    {
        if (!material || !_materials.Remove(material))
            return;
        material.Loaded -= OnMaterialLoaded;
        var parameter = material.GetParameter(MaterialParameterName);
        if (parameter != null && parameter.Value as GPUTexture == _texture)
            parameter.Value = null;
    }

    private static void OnMaterialLoaded(Asset asset)
    {
        BindMaterial(asset as MaterialBase);
    }

    private static void OnRender(RenderTask task, GPUContext context)
    {
        if (_textureDirty && !CreateTexture(context))
            return;
        if (!_texture)
            return;

        // The game's main view renders the scene (registered here since it may not exist yet during plugin initialization)
        var mainTask = MainRenderTask.Instance;
        if (mainTask && mainTask != _mainTask)
        {
            _mainTask = mainTask;
            AddLitTask(mainTask);
        }

        // Game time is paused in the editor's edit mode, so animate on real time there
        float time = Engine.IsPlayMode ? Time.GameTime : Time.TimeSinceStartup;
        int count = CollectLights();
        UploadedLightCount = count;

        // Pack only; lit tasks upload it in their PreRender, everything else keeps seeing the neutral data
        Pack(count, time);
    }

    private static bool CreateTexture(GPUContext context)
    {
        _textureDirty = false;

        if (_texture)
        {
            _texture.ReleaseGPU();
            FlaxEngine.Object.Destroy(ref _texture);
        }

        // One row, one float4 element per texel (max. 1 + 4 * 256 = 1025 texels)
        int elements = 1 + ElementsPerLight * (int)_cap;
        var desc = GPUTextureDescription.New2D(elements, 1, 1, PixelFormat.R32G32B32A32_Float, GPUTextureFlags.ShaderResource);
        _texture = GPUDevice.Instance.CreateTexture("PsxVertexLightData");
        if (_texture.Init(ref desc))
        {
            Debug.LogError("[VertexLightManager] Failed to create the light data texture.");
            FlaxEngine.Object.Destroy(ref _texture);
            return false;
        }

        // Regular (shader resource only) textures start with 0 resident mips, which makes materials skip binding them
        _texture.ResidentMipLevels = 1;

        _data = new Float4[elements];

        // Neutral data (default content): no lights, ambient 1 (= texture colour unchanged)
        _neutralData = new Float4[elements];
        _neutralData[0] = new Float4(0.0f, 1.0f, 1.0f, 1.0f);
        UploadData(context, _neutralData);

        // Point all known materials to the new texture
        for (int i = 0; i < _materials.Count; i++)
            BindMaterial(_materials[i]);
        return true;
    }

    private static bool BindMaterial(MaterialBase material)
    {
        if (!material || !material.IsLoaded)
            return false;
        var parameter = material.GetParameter(MaterialParameterName);
        if (parameter == null)
            return false;
        if (!ValidateMaterial(material))
            return false;
        if (_texture && parameter.Value as GPUTexture != _texture)
            parameter.Value = _texture;
        return true;
    }

    /// <summary>
    /// The shader accesses the texture by its generated name (In1), so the parameter must be the material's first one.
    /// </summary>
    private static bool ValidateMaterial(MaterialBase material)
    {
        var parameters = material.Parameters;
        bool valid = parameters != null && parameters.Length > 0 &&
                     parameters[0].Name == MaterialParameterName &&
                     parameters[0].ParameterType == MaterialParameterType.GPUTexture;
        if (!valid && _reportedMaterials.Add(material.ID))
        {
            Debug.LogError($"[VertexLightManager] Material '{material.Path}': '{MaterialParameterName}' must be the first parameter and of type GPU Texture. Vertex lighting is disabled for this material.");
        }
        return valid;
    }

    private static bool TryGetReferenceView(out Vector3 position, out BoundingFrustum frustum)
    {
        var provider = ReferenceViewProvider;
        if (provider != null && provider(out position, out frustum))
            return true;

        var camera = ReferenceCamera ? ReferenceCamera : Camera.MainCamera;
        if (camera)
        {
            position = camera.Position;
            frustum = camera.Frustum;
            return true;
        }

        position = Vector3.Zero;
        frustum = default;
        return false;
    }

    /// <summary>
    /// Filters, culls and sorts the lights into <see cref="_candidates"/>. Returns the number of lights to upload.
    /// </summary>
    private static int CollectLights()
    {
        int lightCount = _lights.Count;
        if (_candidates.Length < lightCount)
        {
            int size = Math.Max(lightCount, _candidates.Length * 2);
            _candidates = new PsxVertexLight[size];
            _keys = new float[size];
        }

        bool hasView = TryGetReferenceView(out Vector3 viewPosition, out BoundingFrustum frustum);
        bool cull = CullingEnabled && hasView;

        int count = 0;
        for (int i = 0; i < lightCount; i++)
        {
            var light = _lights[i];
            if (!light || !light.IsOn || light.Brightness <= 0.0f)
                continue;

            float key;
            if (light.LightType == PsxLightType.Directional)
            {
                // Only the active directional light of each scene is used
                if (GetActiveDirectional(light.Scene) != light)
                    continue;

                // Unlimited range: never culled, always first
                key = -1.0f;
            }
            else
            {
                float range = GetRange(light);
                Vector3 position = light.Position;
                if (cull)
                {
                    var sphere = new BoundingSphere(position, range * CullingRangeMargin);
                    if (!frustum.Intersects(ref sphere))
                        continue;
                }

                // Distance to the edge of the light's range
                key = hasView ? Mathf.Max(0.0f, (float)Vector3.Distance(position, viewPosition) - range) : 0.0f;
            }

            _candidates[count] = light;
            _keys[count] = key;
            count++;
        }

        if (hasView && count > 1)
            Array.Sort(_keys, _candidates, 0, count);

        // Clear stale references beyond the used range
        for (int i = count; i < lightCount && i < _candidates.Length; i++)
            _candidates[i] = null;

        VisibleLightCount = count;
        return Math.Min(count, (int)_cap);
    }

    private static float GetRange(PsxVertexLight light)
    {
        switch (light.LightType)
        {
        case PsxLightType.Directional: return float.MaxValue;
        case PsxLightType.Area: return light.Range + GetAreaRadius(light);
        default: return light.Range;
        }
    }

    /// <summary>
    /// Distance from an area light's centre to the farthest point of its shape. The area light's Range is measured from the
    /// shape itself, so range tests and culling around the centre use Range + this radius.
    /// The shader derives it the same way from E3 (circle: halfSize.x, rectangle: length(halfSize)).
    /// </summary>
    private static float GetAreaRadius(PsxVertexLight light)
    {
        Float2 half = light.HalfSize;
        return light.Shape == PsxAreaLightShape.Circle ? half.X : half.Length;
    }

    private static void Pack(int count, float time)
    {
        var ambient = Ambient;
        _data[0] = new Float4(count, ambient.R, ambient.G, ambient.B);

        for (int i = 0; i < count; i++)
        {
            var light = _candidates[i];
            int t = 1 + i * ElementsPerLight;

            float intensity = light.Brightness * PsxLightStyles.Evaluate(light, time);
            var color = light.Color;
            var colour = new Float3(color.R * intensity, color.G * intensity, color.B * intensity);
            Float3 position = light.Position;

            switch (light.LightType)
            {
            case PsxLightType.Directional:
                _data[t + 0] = new Float4(position, 0.0f); // No range limit
                _data[t + 1] = new Float4(colour, (float)PsxLightType.Directional);
                _data[t + 2] = new Float4(light.EffectiveDirection, 0.0f);
                _data[t + 3] = Float4.Zero;
                break;
            case PsxLightType.Point:
                _data[t + 0] = new Float4(position, 1.0f / light.Range);
                _data[t + 1] = new Float4(colour, (float)PsxLightType.Point);
                _data[t + 2] = new Float4(0.0f, 0.0f, 0.0f, (float)(int)light.Falloff);
                _data[t + 3] = Float4.Zero;
                break;
            case PsxLightType.Spot:
            {
                float falloff = (int)light.Falloff + (int)light.ConeFalloff * 32;
                float cosHalfAngle = Mathf.Cos(light.Angle * 0.5f * Mathf.DegreesToRadians);
                _data[t + 0] = new Float4(position, 1.0f / light.Range);
                _data[t + 1] = new Float4(colour, (float)PsxLightType.Spot);
                _data[t + 2] = new Float4(light.EffectiveDirection, falloff);
                _data[t + 3] = new Float4(0.0f, 0.0f, 0.0f, cosHalfAngle);
                break;
            }
            case PsxLightType.Area:
            {
                Transform transform = light.Transform;
                Float3 normal = transform.Forward;
                float roll = PsxLightMath.ComputeRoll(normal, transform.Right);
                float shape = light.Shape == PsxAreaLightShape.Circle ? (float)PsxAreaLightShape.Circle : (float)PsxAreaLightShape.Rectangle;
                Float2 half = light.HalfSize;
                _data[t + 0] = new Float4(position, 1.0f / (light.Range + GetAreaRadius(light)));
                _data[t + 1] = new Float4(colour, (float)PsxLightType.Area);
                _data[t + 2] = new Float4(normal, (float)(int)light.Falloff);
                _data[t + 3] = new Float4(shape, half.X, half.Y, roll);
                break;
            }
            default:
                _data[t + 0] = Float4.Zero;
                // E1.w = 0 = empty slot (PsxLightType.Empty)
                _data[t + 1] = Float4.Zero;
                _data[t + 2] = Float4.Zero;
                _data[t + 3] = Float4.Zero;
                break;
            }
        }
    }

    private static unsafe void UploadData(GPUContext context, Float4[] data)
    {
        // UpdateTexture always writes the whole row (max. 16 KB at 256 lights); unused slots are never read by the shader
        uint rowPitch = (uint)(data.Length * sizeof(Float4));
        fixed (Float4* ptr = data)
        {
            context.UpdateTexture(_texture, 0, 0, new IntPtr(ptr), rowPitch, rowPitch);
        }
    }

    /// <summary>
    /// Registers a render task that renders the scene: while it renders, materials see the real light data.
    /// All other tasks (asset previews, thumbnails, unregistered tasks) see neutral data and render unlit.
    /// The game's main render task is registered automatically; register your own scene render tasks (e.g. render-to-texture cameras).
    /// Destroyed tasks are removed automatically.
    /// </summary>
    /// <param name="task">The render task.</param>
    public static void AddLitTask(SceneRenderTask task)
    {
        if (!task || _litTasks.Contains(task))
            return;

        // Drop destroyed tasks
        for (int i = _litTasks.Count - 1; i >= 0; i--)
        {
            if (!_litTasks[i])
                _litTasks.RemoveAt(i);
        }

        _litTasks.Add(task);
        task.PreRender += OnLitTaskPreRender;
        task.PostRender += OnLitTaskPostRender;
    }

    /// <summary>
    /// Reverts <see cref="AddLitTask"/>.
    /// </summary>
    /// <param name="task">The render task.</param>
    public static void RemoveLitTask(SceneRenderTask task)
    {
        if (!task || !_litTasks.Remove(task))
            return;
        task.PreRender -= OnLitTaskPreRender;
        task.PostRender -= OnLitTaskPostRender;
    }

    private static void OnLitTaskPreRender(GPUContext context, ref RenderContext renderContext)
    {
        // Real light data for this task only; the GPU executes the updates in order with the draws
        if (_texture && _data.Length > 0)
            UploadData(context, _data);
    }

    private static void OnLitTaskPostRender(GPUContext context, ref RenderContext renderContext)
    {
        // Back to neutral for everything rendered after this task
        if (_texture && _neutralData.Length > 0)
            UploadData(context, _neutralData);
    }

    // TODO: temporary debug helper, remove in step 10
    /// <summary>
    /// Logs the light data as last uploaded (CPU-side copy of the texture row).
    /// </summary>
    public static void DebugDumpBuffer()
    {
        var elements = _data;
        if (!_texture || elements.Length == 0)
        {
            Debug.LogWarning("[VertexLightManager] No light data texture to dump.");
            return;
        }

        var header = elements[0];
        int count = (int)header.X;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[VertexLightManager] Light data dump: {elements.Length} texels, {count} lights, ambient = ({header.Y:G6}, {header.Z:G6}, {header.W:G6})");
        for (int i = 0; i < count && 1 + (i + 1) * ElementsPerLight <= elements.Length; i++)
        {
            int t = 1 + i * ElementsPerLight;
            sb.AppendLine($"  [{i}] type = {elements[t + 1].W:G6}, invRange = {elements[t].W:G6}");
            sb.AppendLine($"      E0 = {Format(elements[t + 0])}");
            sb.AppendLine($"      E1 = {Format(elements[t + 1])}");
            sb.AppendLine($"      E2 = {Format(elements[t + 2])}");
            sb.AppendLine($"      E3 = {Format(elements[t + 3])}");
        }
        Debug.Log(sb.ToString());
    }

    private static string Format(Float4 v)
    {
        return $"({v.X:G6}, {v.Y:G6}, {v.Z:G6}, {v.W:G6})";
    }
}
