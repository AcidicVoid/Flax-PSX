// www.acidicvoid.com

using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Omnidirectional point vertex light.
/// </summary>
[ActorContextMenu("New/Flax PSX/Point Vertex Light"), ActorToolbox("Flax PSX", "Point Vertex Light")]
public class PsxPointLight : PsxVertexLight
{
    private float _range = 1000.0f;
    private PsxLightFalloff _falloff = PsxLightFalloff.Linear;

    /// <inheritdoc />
    public override PsxLightType LightType => PsxLightType.Point;

    /// <summary>
    /// Light range (in world units, cm). The light reaches zero at this distance.
    /// </summary>
    [EditorOrder(30), EditorDisplay("Light"), Limit(1.0f, float.MaxValue, 1.0f)]
    public float Range
    {
        get => _range;
        set => _range = Mathf.Max(1.0f, value);
    }

    /// <summary>
    /// Distance falloff curve.
    /// </summary>
    [EditorOrder(40), EditorDisplay("Light")]
    public PsxLightFalloff Falloff
    {
        get => _falloff;
        set => _falloff = value;
    }

#if FLAX_EDITOR
    /// <inheritdoc />
    public override void OnDebugDrawSelected()
    {
        DebugDraw.DrawWireSphere(new BoundingSphere(Position, _range), DebugColor);

        base.OnDebugDrawSelected();
    }
#endif
}
