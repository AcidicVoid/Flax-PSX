// www.acidicvoid.com

using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Area vertex light (square, rectangle or circle). The shape faces the actor's forward direction
/// and is oriented by the actor's rotation. Actor scale is ignored; use Dimensions instead.
/// </summary>
[ActorContextMenu("New/Flax PSX/Area Vertex Light"), ActorToolbox("Flax PSX", "Area Vertex Light")]
public class PsxAreaLight : PsxVertexLight
{
    /// <summary>
    /// Minimum size of the shape along each axis (cm).
    /// </summary>
    public const float MinDimension = 0.01f;

    private PsxAreaLightShape _shape = PsxAreaLightShape.Rectangle;
    private Float2 _dimensions = new Float2(100.0f, 100.0f);
    private float _range = 1000.0f;
    private PsxLightFalloff _falloff = PsxLightFalloff.Linear;

    /// <inheritdoc />
    public override PsxLightType LightType => PsxLightType.Area;

    /// <summary>
    /// Shape of the light.
    /// </summary>
    [EditorOrder(30), EditorDisplay("Light")]
    public PsxAreaLightShape Shape
    {
        get => _shape;
        set => _shape = value;
    }

    /// <summary>
    /// Full size of the shape (cm). Rectangle: width (X) and height (Y).
    /// Square: side length (X). Circle: diameter (X). Y is ignored for Square and Circle.
    /// </summary>
    [EditorOrder(40), EditorDisplay("Light")]
    public Float2 Dimensions
    {
        get => _dimensions;
        set => _dimensions = new Float2(Mathf.Max(MinDimension, value.X), Mathf.Max(MinDimension, value.Y));
    }

    /// <summary>
    /// Light range (in world units, cm). The light reaches zero at this distance.
    /// </summary>
    [EditorOrder(50), EditorDisplay("Light"), Limit(1.0f, float.MaxValue, 1.0f)]
    public float Range
    {
        get => _range;
        set => _range = Mathf.Max(1.0f, value);
    }

    /// <summary>
    /// Distance falloff curve.
    /// </summary>
    [EditorOrder(60), EditorDisplay("Light")]
    public PsxLightFalloff Falloff
    {
        get => _falloff;
        set => _falloff = value;
    }

    /// <summary>
    /// Half size of the shape as used by the shader: Square uses X for both axes, Circle stores the radius in X.
    /// </summary>
    public Float2 HalfSize
    {
        get
        {
            switch (_shape)
            {
            case PsxAreaLightShape.Square:
                return new Float2(_dimensions.X * 0.5f, _dimensions.X * 0.5f);
            case PsxAreaLightShape.Circle:
                return new Float2(_dimensions.X * 0.5f, 0.0f);
            default:
                return _dimensions * 0.5f;
            }
        }
    }

#if FLAX_EDITOR
    /// <inheritdoc />
    public override void OnDebugDraw()
    {
        DrawShape(DebugColor);

        base.OnDebugDraw();
    }

    /// <inheritdoc />
    public override void OnDebugDrawSelected()
    {
        Color color = DebugColor;
        Vector3 position = Position;
        Float3 forward = Direction;

        // Facing normal and range
        DebugDraw.DrawLine(position, position + forward * Mathf.Min(_range, 100.0f), color);
        DebugDraw.DrawWireSphere(new BoundingSphere(position, _range), color * 0.5f);

        base.OnDebugDrawSelected();
    }

    private void DrawShape(Color color)
    {
        Vector3 position = Position;
        Transform transform = Transform;
        Float2 half = HalfSize;

        if (_shape == PsxAreaLightShape.Circle)
        {
            DebugDraw.DrawCircle(position, transform.Forward, half.X, color);
            return;
        }

        Float3 right = transform.Right * half.X;
        Float3 up = transform.Up * half.Y;
        Vector3 c0 = position + right + up;
        Vector3 c1 = position - right + up;
        Vector3 c2 = position - right - up;
        Vector3 c3 = position + right - up;
        DebugDraw.DrawLine(c0, c1, color);
        DebugDraw.DrawLine(c1, c2, color);
        DebugDraw.DrawLine(c2, c3, color);
        DebugDraw.DrawLine(c3, c0, color);
    }
#endif
}
