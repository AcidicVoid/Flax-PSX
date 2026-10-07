// www.acidicvoid.com

using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Directional vertex light (sun, moon). Lights everything from one direction, no range.
/// </summary>
[ActorContextMenu("New/Flax PSX/Directional Vertex Light"), ActorToolbox("Flax PSX", "Directional Vertex Light")]
public class PsxDirectionalLight : PsxVertexLight
{
    private Actor _target;

    /// <inheritdoc />
    public override PsxLightType LightType => PsxLightType.Directional;

    /// <summary>
    /// Light direction in world space (the direction the light shines towards).
    /// Backed by the actor's rotation, so rotating the actor changes it too. Overridden by Target if set.
    /// </summary>
    [EditorOrder(30), EditorDisplay("Light", "Direction"), NoSerialize]
    public Float3 LightDirection
    {
        get => Direction;
        set => SetDirectionFromVector(value);
    }

    /// <summary>
    /// Optional target. If set, the light faces towards the target and LightDirection is ignored.
    /// </summary>
    [EditorOrder(40), EditorDisplay("Light")]
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
        DebugDraw.DrawLine(position, position + EffectiveDirection * 100.0f, DebugColor);

        base.OnDebugDraw();
    }

    /// <inheritdoc />
    public override void OnDebugDrawSelected()
    {
        Color color = DebugColor;
        Vector3 position = Position;
        Float3 direction = EffectiveDirection;
        GetPerpendicularAxes(direction, out Float3 right, out Float3 up);

        // Parallel rays to visualize the direction
        const float spread = 40.0f;
        const float length = 300.0f;
        DebugDraw.DrawLine(position, position + direction * length, color);
        DebugDraw.DrawLine(position + right * spread, position + right * spread + direction * length, color);
        DebugDraw.DrawLine(position - right * spread, position - right * spread + direction * length, color);
        DebugDraw.DrawLine(position + up * spread, position + up * spread + direction * length, color);
        DebugDraw.DrawLine(position - up * spread, position - up * spread + direction * length, color);

        if (_target && _target != this)
            DebugDraw.DrawLine(position, _target.Position, color * 0.5f);

        base.OnDebugDrawSelected();
    }
#endif
}
