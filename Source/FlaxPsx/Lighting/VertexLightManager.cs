// www.acidicvoid.com

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Collects all Flax-PSX vertex lights, evaluates animations, culls and sorts them,
/// packs them into the light data buffer and uploads it once per frame.
/// The buffer is bound to every material that has a <see cref="MaterialParameterName"/> parameter.
/// </summary>
/// <remarks>
/// Light data buffer layout (typed buffer, R32G32B32A32_Float, 1 + 4 * cap elements; HLSL: Buffer&lt;float4&gt;):
/// <list type="bullet">
/// <item>Element 0 (header): x = light count, yzw = ambient colour.</item>
/// <item>Light i starts at element 1 + 4 * i:</item>
/// <item>E0: position.xyz, 1 / range (0 = no range limit, directional)</item>
/// <item>E1: colour.rgb (premultiplied by brightness and animation), type</item>
/// <item>E2: direction.xyz, falloff (spot: distance falloff + cone falloff * 32)</item>
/// <item>E3: shape (area only: 1 = rectangle, 2 = circle), halfSize.x, halfSize.y, angle (spot: cos(angle / 2), area: roll)</item>
/// </list>
/// The range test only needs E0, so the shader can skip loading E1–E3 for lights out of range.
/// </remarks>
public static class VertexLightManager
{
    /// <summary>
    /// Name of the material parameter receiving the light data buffer. Must be the material's first parameter (GPU Buffer).
    /// </summary>
    public const string MaterialParameterName = "PsxLightData";

    /// <summary>
    /// float4 elements per light in the light data buffer.
    /// </summary>
    public const int ElementsPerLight = 4;

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
    private static GPUBuffer _buffer;
    private static RenderTask _task;
    private static PsxVertexLightCap _cap = PsxVertexLightCap.Lights64;
    private static bool _bufferDirty = true;
    private static float _nextMaterialScan;
    private static bool _initialized;

    /// <summary>
    /// Maximum number of lights uploaded per frame. Changing it reallocates the light data buffer.
    /// </summary>
    public static PsxVertexLightCap Cap
    {
        get => _cap;
        set
        {
            if (_cap == value)
                return;
            _cap = value;
            _bufferDirty = true;
        }
    }

    /// <summary>
    /// Enables frustum culling against the reference view. Disable it while two or more cameras are active,
    /// since the light data buffer is shared by every view in a frame.
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
    /// The light data buffer (null until the first frame after initialization).
    /// </summary>
    public static GPUBuffer LightDataBuffer => _buffer;

    /// <summary>
    /// All registered (enabled) vertex lights.
    /// </summary>
    public static IReadOnlyList<PsxVertexLight> Lights => _lights;

    /// <summary>
    /// Materials the light data buffer is bound to.
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

        _bufferDirty = true;
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

        // Unbind the buffer from all materials before releasing it
        for (int i = 0; i < _materials.Count; i++)
        {
            var material = _materials[i];
            var parameter = material ? material.GetParameter(MaterialParameterName) : null;
            if (parameter != null && parameter.Value as GPUBuffer == _buffer)
                parameter.Value = null;
        }
        _materials.Clear();
        _reportedMaterials.Clear();

        if (_buffer)
        {
            _buffer.ReleaseGPU();
            FlaxEngine.Object.Destroy(ref _buffer);
        }
        _bufferDirty = true;
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
    /// Explicitly binds the light data buffer to a material (e.g. a material that isn't loaded yet during the automatic scan).
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
        if (parameter != null && parameter.Value as GPUBuffer == _buffer)
            parameter.Value = null;
    }

    /// <summary>
    /// Scans all loaded materials and binds the light data buffer to those with a <see cref="MaterialParameterName"/> parameter.
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
        if (_bufferDirty && !CreateBuffer())
            return;
        if (!_buffer)
            return;

        float now = Time.TimeSinceStartup;
        if (now >= _nextMaterialScan)
        {
            _nextMaterialScan = now + MaterialScanInterval;
            ScanMaterials();
        }

        // Game time is paused in the editor's edit mode, so animate on real time there
        float time = Engine.IsPlayMode ? Time.GameTime : Time.TimeSinceStartup;
        int count = CollectLights();
        UploadedLightCount = count;
        Pack(count, time);
        Upload(context, count);
    }

    private static bool CreateBuffer()
    {
        _bufferDirty = false;

        if (_buffer)
        {
            _buffer.ReleaseGPU();
            FlaxEngine.Object.Destroy(ref _buffer);
        }

        // Typed buffer matching the material's generated 'Buffer' (= Buffer<float4>) declaration; Default usage, updated via UpdateBuffer
        int elements = 1 + ElementsPerLight * (int)_cap;
        var desc = GPUBufferDescription.Typed(elements, PixelFormat.R32G32B32A32_Float);
        _buffer = GPUDevice.Instance.CreateBuffer("PsxVertexLightData");
        if (_buffer.Init(ref desc))
        {
            Debug.LogError("[VertexLightManager] Failed to create the light data buffer.");
            FlaxEngine.Object.Destroy(ref _buffer);
            return false;
        }

        _data = new Float4[elements];

        // Point all known materials to the new buffer
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
        if (_buffer && parameter.Value as GPUBuffer != _buffer)
            parameter.Value = _buffer;
        return true;
    }

    /// <summary>
    /// The shader accesses the buffer by its generated name (In1), so the parameter must be the material's first one.
    /// </summary>
    private static bool ValidateMaterial(MaterialBase material)
    {
        var parameters = material.Parameters;
        bool valid = parameters != null && parameters.Length > 0 &&
                     parameters[0].Name == MaterialParameterName &&
                     parameters[0].ParameterType == MaterialParameterType.GPUBuffer;
        if (!valid && _reportedMaterials.Add(material.ID))
        {
            Debug.LogError($"[VertexLightManager] Material '{material.Path}': '{MaterialParameterName}' must be the first parameter and of type GPU Buffer. Vertex lighting is disabled for this material.");
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
            int t = 1 + i * ElementsPerLight;

            float intensity = light.Brightness * PsxLightStyles.Evaluate(light, time);
            var color = light.Color;
            var colour = new Float3(color.R * intensity, color.G * intensity, color.B * intensity);
            Float3 position = light.Position;

            switch (light)
            {
            case PsxDirectionalLight directional:
                _data[t + 0] = new Float4(position, 0.0f); // No range limit
                _data[t + 1] = new Float4(colour, (float)PsxLightType.Directional);
                _data[t + 2] = new Float4(directional.EffectiveDirection, 0.0f);
                _data[t + 3] = Float4.Zero;
                break;
            case PsxPointLight point:
                _data[t + 0] = new Float4(position, 1.0f / point.Range);
                _data[t + 1] = new Float4(colour, (float)PsxLightType.Point);
                _data[t + 2] = new Float4(0.0f, 0.0f, 0.0f, (float)(int)point.Falloff);
                _data[t + 3] = Float4.Zero;
                break;
            case PsxSpotLight spot:
            {
                float falloff = (int)spot.Falloff + (int)spot.ConeFalloff * 32;
                float cosHalfAngle = Mathf.Cos(spot.Angle * 0.5f * Mathf.DegreesToRadians);
                _data[t + 0] = new Float4(position, 1.0f / spot.Range);
                _data[t + 1] = new Float4(colour, (float)PsxLightType.Spot);
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
                _data[t + 0] = new Float4(position, 1.0f / area.Range);
                _data[t + 1] = new Float4(colour, (float)PsxLightType.Area);
                _data[t + 2] = new Float4(normal, (float)(int)area.Falloff);
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

    private static unsafe void Upload(GPUContext context, int count)
    {
        // Header + used lights only; the shader never reads past the light count
        uint size = (uint)((1 + ElementsPerLight * count) * sizeof(Float4));
        fixed (Float4* ptr = _data)
        {
            context.UpdateBuffer(_buffer, new IntPtr(ptr), size, 0);
        }
    }

    // TODO: temporary debug helper, remove in step 10
    /// <summary>
    /// Reads the light data buffer back from the GPU on a background thread and logs its contents.
    /// </summary>
    public static void DebugDumpBuffer()
    {
        var buffer = _buffer;
        if (!buffer)
        {
            Debug.LogWarning("[VertexLightManager] No light data buffer to dump.");
            return;
        }

        // Default-usage buffers can't be downloaded on the main thread
        System.Threading.Tasks.Task.Run(() =>
        {
            if (buffer.DownloadData(out byte[] bytes) || bytes == null)
            {
                Debug.LogWarning("[VertexLightManager] Light data buffer readback failed.");
                return;
            }

            var elements = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Float4>(bytes.AsSpan());
            var header = elements[0];
            int count = (int)header.X;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[VertexLightManager] Buffer dump: {elements.Length} elements, {count} lights, ambient = ({header.Y}, {header.Z}, {header.W})");
            for (int i = 0; i < count && 1 + (i + 1) * ElementsPerLight <= elements.Length; i++)
            {
                int t = 1 + i * ElementsPerLight;
                sb.AppendLine($"  [{i}] type = {elements[t + 1].W}, invRange = {elements[t].W}");
                sb.AppendLine($"      E0 = {elements[t + 0]}");
                sb.AppendLine($"      E1 = {elements[t + 1]}");
                sb.AppendLine($"      E2 = {elements[t + 2]}");
                sb.AppendLine($"      E3 = {elements[t + 3]}");
            }
            Debug.Log(sb.ToString());
        });
    }
}
