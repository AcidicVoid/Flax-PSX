// www.acidicvoid.com

using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Per-scene vertex lighting settings. Pushes its values to <see cref="VertexLightManager"/> while enabled.
/// Use one per scene; if several are active, the last one updated wins.
/// </summary>
[Category(name: "Flax PSX")]
[ExecuteInEditMode]
public class PsxVertexLightingSettings : Script
{
    /// <summary>
    /// Ambient light colour.
    /// </summary>
    [EditorOrder(0)]
    public Color Ambient = new Color(0.2f, 0.2f, 0.2f, 1.0f);

    /// <summary>
    /// Maximum number of lights uploaded per frame.
    /// </summary>
    [EditorOrder(10)]
    public PsxVertexLightCap Cap = PsxVertexLightCap.Lights64;

    /// <summary>
    /// Frustum culling against the reference view. Disable it while two or more cameras are active.
    /// </summary>
    [EditorOrder(20)]
    public bool CullingEnabled = true;

    /// <summary>
    /// Camera used for culling and sorting in play mode. Empty = main camera.
    /// In the editor's edit mode, the editor viewport camera is used.
    /// </summary>
    [EditorOrder(30)]
    public Camera ReferenceCamera;

    // TODO: temporary debug readout, remove in step 10

    /// <summary>
    /// Number of registered (enabled) vertex lights.
    /// </summary>
    [EditorOrder(100), EditorDisplay("Debug"), ShowInEditor, ReadOnly, NoSerialize]
    public int RegisteredLights => VertexLightManager.Lights.Count;

    /// <summary>
    /// Number of lights that passed culling in the last frame (before applying the cap).
    /// </summary>
    [EditorOrder(110), EditorDisplay("Debug"), ShowInEditor, ReadOnly, NoSerialize]
    public int VisibleLights => VertexLightManager.VisibleLightCount;

    /// <summary>
    /// Number of lights uploaded in the last frame.
    /// </summary>
    [EditorOrder(120), EditorDisplay("Debug"), ShowInEditor, ReadOnly, NoSerialize]
    public int UploadedLights => VertexLightManager.UploadedLightCount;

    /// <summary>
    /// Logs the GPU light data buffer contents (read back on a background thread).
    /// </summary>
    [Button("Dump Light Buffer")]
    public void DumpBuffer()
    {
        VertexLightManager.DebugDumpBuffer();
    }

    /// <inheritdoc />
    public override void OnEnable()
    {
        Apply();
    }

    /// <inheritdoc />
    public override void OnUpdate()
    {
        // Cheap assignments; keeps editor and script changes in sync
        Apply();
    }

    /// <summary>
    /// Pushes the settings to the manager.
    /// </summary>
    public void Apply()
    {
        VertexLightManager.Ambient = Ambient;
        VertexLightManager.Cap = Cap;
        VertexLightManager.CullingEnabled = CullingEnabled;
        VertexLightManager.ReferenceCamera = ReferenceCamera;
    }
}
