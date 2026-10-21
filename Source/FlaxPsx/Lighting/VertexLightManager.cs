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
/// Light data texture layout (R32G32B32A32_Float, one row, width = 1 + 4 * cap):
/// <list type="bullet">
/// <item>Texel 0 (header): x = light count, yzw = ambient colour.</item>
/// <item>Light i starts at texel 1 + 4 * i:</item>
/// <item>T0: position.xyz, type</item>
/// <item>T1: colour.rgb (premultiplied by brightness and animation), 1 / range</item>
/// <item>T2: direction.xyz, falloff (spot: distance falloff + cone falloff * 32)</item>
/// <item>T3: shape (area only: 1 = rectangle, 2 = circle), halfSize.x, halfSize.y, angle (spot: cos(angle / 2), area: roll)</item>
/// </list>
/// </remarks>
public static class VertexLightManager
{
    /// <summary>
    /// Name of the material parameter receiving the light data texture. Must be the material's first parameter.
    /// </summary>
    public const string MaterialParameterName = "PsxLightData";

    /// <summary>
    /// Texels per light in the light data texture.
    /// </summary>
    public const int TexelsPerLight = 4;

    /// <summary>
    /// Range margin used for frustum culling (+5%).
    /// </summary>
    public const float CullingRangeMargin = 1.05f;

    /// <summary>
    /// Interval (seconds) between scans for newly loaded materials.
    /// </summary>
    public const float MaterialScanInterval = 2.0f;

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
    private static Asset[] _assetBuffer;
    private static GPUTexture _texture;
    private static RenderTask _task;
    private static PsxVertexLightCap _cap = PsxVertexLightCap.Lights64;
    private static bool _textureDirty = true;
    private static float _nextMaterialScan;
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
    /// Ambient light colour, sent through the header texel.
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

        Level.SceneLoaded += OnSceneLoaded;

        _textureDirty = true;
        _nextMaterialScan = 0.0f;
    }

    /// <summary>
    /// Releases all resources. Called by the Flax-PSX game plugin.
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized)
            return;
        _initialized = false;

        Level.SceneLoaded -= OnSceneLoaded;

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
            var parameter = material ? material.GetParameter(MaterialParameterName) : null;
            if (parameter != null && parameter.Value as GPUTexture == _texture)
                parameter.Value = null;
        }
        _materials.Clear();
        _reportedMaterials.Clear();

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
        if (light && !_lights.Contains(light))
            _lights.Add(light);
    }

    /// <summary>
    /// Unregisters a light. Called by <see cref="PsxVertexLight"/> when disabled.
    /// </summary>
    internal static void Unregister(PsxVertexLight light)
    {
        _lights.Remove(light);
    }

    /// <summary>
    /// Explicitly binds the light data texture to a material (e.g. a material that isn't loaded yet during the automatic scan).
    /// Base materials are found automatically; material instances inherit the binding unless they override the parameter.
    /// </summary>
    /// <param name="material">The material.</param>
    /// <returns>True if the material has a valid light data parameter.</returns>
    public static bool RegisterMaterial(MaterialBase material)
    {
        if (!material)
            return false;
        if (!_materials.Contains(material))
            _materials.Add(material);
        return BindMaterial(material);
    }

    /// <summary>
    /// Removes a material from the binding list and clears its light data parameter.
    /// </summary>
    /// <param name="material">The material.</param>
    public static void UnregisterMaterial(MaterialBase material)
    {
        if (!material)
            return;
        _materials.Remove(material);
        var parameter = material.GetParameter(MaterialParameterName);
        if (parameter != null && parameter.Value as GPUTexture == _texture)
            parameter.Value = null;
    }

    /// <summary>
    /// Scans all loaded materials and binds the light data texture to those with a <see cref="MaterialParameterName"/> parameter.
    /// Runs automatically on scene load and every <see cref="MaterialScanInterval"/> seconds.
    /// </summary>
    public static void ScanMaterials()
    {
        // Drop destroyed materials
        for (int i = _materials.Count - 1; i >= 0; i--)
        {
            if (!_materials[i])
                _materials.RemoveAt(i);
        }

        Content.GetAssets(ref _assetBuffer, out int count);
        for (int i = 0; i < count; i++)
        {
            if (_assetBuffer[i] is not Material material || !material.IsLoaded)
                continue;
            if (material.GetParameter(MaterialParameterName) == null)
                continue;
            if (!_materials.Contains(material))
                _materials.Add(material);
        }

        // Clear references so unloaded assets aren't kept alive by the buffer
        if (_assetBuffer != null)
            Array.Clear(_assetBuffer, 0, _assetBuffer.Length);

        // (Re)bind everything; this also restores bindings lost through asset reloads
        for (int i = 0; i < _materials.Count; i++)
            BindMaterial(_materials[i]);
    }

    private static void OnSceneLoaded(Scene scene, Guid sceneId)
    {
        _nextMaterialScan = 0.0f;
    }

    private static void OnRender(RenderTask task, GPUContext context)
    {
        if (_textureDirty && !CreateTexture())
            return;
        if (!_texture)
            return;

        float now = Time.TimeSinceStartup;
        if (now >= _nextMaterialScan)
        {
            _nextMaterialScan = now + MaterialScanInterval;
            ScanMaterials();
        }

        int count = CollectLights();
        Pack(count, Time.GameTime);
        Upload(context);
    }

    private static bool CreateTexture()
    {
        _textureDirty = false;

        if (_texture)
        {
            _texture.ReleaseGPU();
            FlaxEngine.Object.Destroy(ref _texture);
        }

        int width = 1 + TexelsPerLight * (int)_cap;
        var desc = GPUTextureDescription.New2D(width, 1, 1, PixelFormat.R32G32B32A32_Float, GPUTextureFlags.ShaderResource);
        _texture = GPUDevice.Instance.CreateTexture("PsxVertexLightData");
        if (_texture.Init(ref desc))
        {
            Debug.LogError("[VertexLightManager] Failed to create the light data texture.");
            FlaxEngine.Object.Destroy(ref _texture);
            return false;
        }

        // Regular (shader resource only) textures start with 0 resident mips, which makes materials skip binding them
        _texture.ResidentMipLevels = 1;

        _data = new Float4[width];

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
        switch (light)
        {
        case PsxPointLight point: return point.Range;
        case PsxSpotLight spot: return spot.Range;
        case PsxAreaLight area: return area.Range;
        default: return float.MaxValue;
        }
    }

    private static void Pack(int count, float time)
    {
        var ambient = Ambient;
        _data[0] = new Float4(count, ambient.R, ambient.G, ambient.B);

        for (int i = 0; i < count; i++)
        {
            var light = _candidates[i];
            int t = 1 + i * TexelsPerLight;

            float intensity = light.Brightness * PsxLightStyles.Evaluate(light, time);
            var color = light.Color;
            var colour = new Float3(color.R * intensity, color.G * intensity, color.B * intensity);
            Float3 position = light.Position;

            switch (light)
            {
            case PsxDirectionalLight directional:
                _data[t + 0] = new Float4(position, (float)PsxLightType.Directional);
                _data[t + 1] = new Float4(colour, 0.0f);
                _data[t + 2] = new Float4(directional.EffectiveDirection, 0.0f);
                _data[t + 3] = Float4.Zero;
                break;
            case PsxPointLight point:
                _data[t + 0] = new Float4(position, (float)PsxLightType.Point);
                _data[t + 1] = new Float4(colour, 1.0f / point.Range);
                _data[t + 2] = new Float4(0.0f, 0.0f, 0.0f, (float)(int)point.Falloff);
                _data[t + 3] = Float4.Zero;
                break;
            case PsxSpotLight spot:
            {
                float falloff = (int)spot.Falloff + (int)spot.ConeFalloff * 32;
                float cosHalfAngle = Mathf.Cos(spot.Angle * 0.5f * Mathf.DegreesToRadians);
                _data[t + 0] = new Float4(position, (float)PsxLightType.Spot);
                _data[t + 1] = new Float4(colour, 1.0f / spot.Range);
                _data[t + 2] = new Float4(spot.EffectiveDirection, falloff);
                _data[t + 3] = new Float4(0.0f, 0.0f, 0.0f, cosHalfAngle);
                break;
            }
            case PsxAreaLight area:
            {
                Transform transform = area.Transform;
                Float3 normal = transform.Forward;
                float roll = PsxLightMath.ComputeRoll(normal, transform.Right);
                float shape = area.Shape == PsxAreaLightShape.Circle ? (float)PsxAreaLightShape.Circle : (float)PsxAreaLightShape.Rectangle;
                Float2 half = area.HalfSize;
                _data[t + 0] = new Float4(position, (float)PsxLightType.Area);
                _data[t + 1] = new Float4(colour, 1.0f / area.Range);
                _data[t + 2] = new Float4(normal, (float)(int)area.Falloff);
                _data[t + 3] = new Float4(shape, half.X, half.Y, roll);
                break;
            }
            default:
                _data[t + 0] = Float4.Zero; // Type 0 = empty slot
                _data[t + 1] = Float4.Zero;
                _data[t + 2] = Float4.Zero;
                _data[t + 3] = Float4.Zero;
                break;
            }
        }
    }

    private static unsafe void Upload(GPUContext context)
    {
        uint rowPitch = (uint)(_data.Length * sizeof(Float4));
        fixed (Float4* ptr = _data)
        {
            context.UpdateTexture(_texture, 0, 0, new IntPtr(ptr), rowPitch, rowPitch);
        }
    }
}
