// www.acidicvoid.com

using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Spot vertex light with a light cone.
/// </summary>
[ActorContextMenu("New/Flax PSX/Spot Vertex Light"), ActorToolbox("Flax PSX", "Spot Vertex Light")]
public class PsxSpotLight : PsxVertexLight
{
    /// <summary>
    /// Maximum cone angle in degrees.
    /// </summary>
    public const float MaxAngle = 179.0f;

    /// <summary>
    /// Minimum cone angle in degrees.
    /// </summary>
    public const float MinAngle = 1.0f;

    private float _range = 1000.0f;
    private PsxLightFalloff _falloff = PsxLightFalloff.Linear;
    private float _angle = 60.0f;
    private PsxLightFalloff _coneFalloff = PsxLightFalloff.Linear;
    private Actor _target;

    /// <inheritdoc />
    public override PsxLightType LightType => PsxLightType.Spot;

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

    /// <summary>
    /// Full opening angle of the light cone in degrees (1–179).
    /// </summary>
    [EditorOrder(50), EditorDisplay("Light"), Limit(MinAngle, MaxAngle, 0.1f)]
    public float Angle
    {
        get => _angle;
        set => _angle = Mathf.Clamp(value, MinAngle, MaxAngle);
    }

    /// <summary>
    /// Falloff curve from the cone's centre (full brightness) to its edge (zero).
    /// </summary>
    [EditorOrder(60), EditorDisplay("Light")]
    public PsxLightFalloff ConeFalloff
    {
        get => _coneFalloff;
        set => _coneFalloff = value;
    }

    /// <summary>
    /// Light direction in world space (the direction the light shines towards).
    /// Backed by the actor's rotation, so rotating the actor changes it too. Overridden by Target if set.
    /// </summary>
    [EditorOrder(70), EditorDisplay("Light", "Direction"), NoSerialize]
    public Float3 LightDirection
    {
        get => Direction;
        set => SetDirectionFromVector(value);
    }

    /// <summary>
    /// Optional target. If set, the light faces towards the target and LightDirection is ignored.
    /// </summary>
    [EditorOrder(80), EditorDisplay("Light")]
    public Actor Target
    {
        get => _target;
        set => _target = value;
    }

    /// <summary>
    /// Direction actually used for lighting: towards Target if set, otherwise LightDirection.
    /// </summary>
    public Float3 EffectiveDirection => GetDirectionTowards(_target);

#if FLAX_EDITOR
    /// <inheritdoc />
    public override void OnDebugDraw()
    {
        Vector3 position = Position;
        DebugDraw.DrawLine(position, position + EffectiveDirection * 50.0f, DebugColor);

        base.OnDebugDraw();
    }

    /// <inheritdoc />
    public override void OnDebugDrawSelected()
    {
        Color color = DebugColor;
        Vector3 position = Position;
        Float3 direction = EffectiveDirection;
        GetPerpendicularAxes(direction, out Float3 right, out Float3 up);

        // Cone: four edge lines of length Range plus the circle where they end
        float halfAngle = _angle * 0.5f * Mathf.DegreesToRadians;
        float axial = Mathf.Cos(halfAngle) * _range;
        float radial = Mathf.Sin(halfAngle) * _range;
        Vector3 discCenter = position + direction * axial;
        DebugDraw.DrawLine(position, discCenter + up * radial, color);
        DebugDraw.DrawLine(position, discCenter - up * radial, color);
        DebugDraw.DrawLine(position, discCenter + right * radial, color);
        DebugDraw.DrawLine(position, discCenter - right * radial, color);
        DebugDraw.DrawCircle(discCenter, direction, radial, color);

        if (_target && _target != this)
            DebugDraw.DrawLine(position, _target.Position, color * 0.5f);

        base.OnDebugDrawSelected();
    }
#endif
}
