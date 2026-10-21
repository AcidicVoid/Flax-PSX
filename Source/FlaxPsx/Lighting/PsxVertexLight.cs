// www.acidicvoid.com

using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Flax-PSX vertex light (directional, point, spot or area). Not an engine light: it only carries
/// parameters, which are packed into the vertex light data buffer and evaluated by PSX materials.
/// The light type can be changed at any time, also from scripts.
/// </summary>
/// <remarks>
/// The editor's scene context menu offers one entry per light type (New/Flax PSX); the Toolbox shows a single entry.
/// Only one directional light per scene is used, see <see cref="VertexLightManager.SetActiveDirectional"/>.
/// </remarks>
[ActorToolbox("Flax PSX", "Vertex Light")]
public class PsxVertexLight : Actor
{
    /// <summary>
    /// Maximum spot cone angle in degrees.
    /// </summary>
    public const float MaxAngle = 179.0f;

    /// <summary>
    /// Minimum spot cone angle in degrees.
    /// </summary>
    public const float MinAngle = 1.0f;

    /// <summary>
    /// Minimum size of an area light shape along each axis (cm).
    /// </summary>
    public const float MinDimension = 0.01f;

    private PsxLightType _type = PsxLightType.Point;
    private bool _isOn = true;
    private float _brightness = 1.0f;
    private Color _color = FlaxEngine.Color.White;
    private float _range = 1000.0f;
    private PsxLightFalloff _falloff = PsxLightFalloff.Linear;
    private float _angle = 60.0f;
    private PsxLightFalloff _coneFalloff = PsxLightFalloff.Linear;
    private PsxAreaLightShape _shape = PsxAreaLightShape.Rectangle;
    private Float2 _dimensions = new Float2(100.0f, 100.0f);
    private Actor _target;
    private PsxLightAnimation _animation = PsxLightAnimation.Normal;
    private string _customPattern = "m";
    private float[] _customPatternValues = PsxLightStyles.Parse("m");
    private float _animationSpeed = 1.0f;
    private float _animationStrength = 1.0f;

    // Active directional light selection: the highest stamp per scene wins (see VertexLightManager)
    [Serialize, HideInEditor]
    private int _activeStamp;

    /// <summary>
    /// Light type, written to the <c>type</c> field of the light data buffer.
    /// </summary>
    [EditorOrder(-10), EditorDisplay("Light", "Type")]
    public PsxLightType LightType
    {
        get => _type;
        set
        {
            if (_type == value || value == PsxLightType.Empty)
                return;
            _type = value;
            VertexLightManager.OnLightTypeChanged(this);
        }
    }

    /// <summary>
    /// Turns the light on or off. Lights that are off are not uploaded at all.
    /// Independent of the actor's active state. An active directional light that is off stays the scene's active one.
    /// </summary>
    [EditorOrder(0), EditorDisplay("Light", "On")]
    public bool IsOn
    {
        get => _isOn;
        set => _isOn = value;
    }

    /// <summary>
    /// Brightness multiplier, folded into the colour on the CPU.
    /// </summary>
    [EditorOrder(10), EditorDisplay("Light"), Limit(0.0f, float.MaxValue, 0.01f)]
    public float Brightness
    {
        get => _brightness;
        set => _brightness = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// Light colour.
    /// </summary>
    [EditorOrder(20), EditorDisplay("Light", "Colour")]
    public Color Color
    {
        get => _color;
        set => _color = value;
    }

    /// <summary>
    /// Light range (in world units, cm). The light reaches zero at this distance. Not used by directional lights.
    /// </summary>
    [EditorOrder(30), EditorDisplay("Light"), Limit(1.0f, float.MaxValue, 1.0f), VisibleIf(nameof(HasRange))]
    public float Range
    {
        get => _range;
        set => _range = Mathf.Max(1.0f, value);
    }

    /// <summary>
    /// Distance falloff curve. Not used by directional lights.
    /// </summary>
    [EditorOrder(40), EditorDisplay("Light"), VisibleIf(nameof(HasRange))]
    public PsxLightFalloff Falloff
    {
        get => _falloff;
        set => _falloff = value;
    }

    /// <summary>
    /// Spot only: full opening angle of the light cone in degrees (1–179).
    /// </summary>
    [EditorOrder(50), EditorDisplay("Light"), Limit(MinAngle, MaxAngle, 0.1f), VisibleIf(nameof(IsSpot))]
    public float Angle
    {
        get => _angle;
        set => _angle = Mathf.Clamp(value, MinAngle, MaxAngle);
    }

    /// <summary>
    /// Spot only: falloff curve from the cone's centre (full brightness) to its edge (zero).
    /// </summary>
    [EditorOrder(60), EditorDisplay("Light"), VisibleIf(nameof(IsSpot))]
    public PsxLightFalloff ConeFalloff
    {
        get => _coneFalloff;
        set => _coneFalloff = value;
    }

    /// <summary>
    /// Area only: shape of the light. The shape faces the actor's forward direction and is oriented by the actor's rotation.
    /// </summary>
    [EditorOrder(70), EditorDisplay("Light"), VisibleIf(nameof(IsArea))]
    public PsxAreaLightShape Shape
    {
        get => _shape;
        set => _shape = value;
    }

    /// <summary>
    /// Area only: full size of the shape (cm). Rectangle: width (X) and height (Y).
    /// Square: side length (X). Circle: diameter (X). Y is ignored for Square and Circle. Actor scale is ignored.
    /// </summary>
    [EditorOrder(80), EditorDisplay("Light"), VisibleIf(nameof(IsArea))]
    public Float2 Dimensions
    {
        get => _dimensions;
        set => _dimensions = new Float2(Mathf.Max(MinDimension, value.X), Mathf.Max(MinDimension, value.Y));
    }

    /// <summary>
    /// Directional and spot only: light direction in world space (the direction the light shines towards).
    /// Backed by the actor's rotation, so rotating the actor changes it too. Overridden by Target if set.
    /// </summary>
    [EditorOrder(90), EditorDisplay("Light", "Direction"), NoSerialize, VisibleIf(nameof(HasDirection))]
    public Float3 LightDirection
    {
        get => Direction;
        set => SetDirectionFromVector(value);
    }

    /// <summary>
    /// Directional and spot only: optional target. If set, the light faces towards the target and LightDirection is ignored.
    /// </summary>
    [EditorOrder(95), EditorDisplay("Light"), VisibleIf(nameof(HasDirection))]
    public Actor Target
    {
        get => _target;
        set => _target = value;
    }

    /// <summary>
    /// Light animation (Quake light styles or a custom pattern).
    /// </summary>
    [EditorOrder(100), EditorDisplay("Animation")]
    public PsxLightAnimation Animation
    {
        get => _animation;
        set => _animation = value;
    }

    /// <summary>
    /// Custom animation pattern, used if Animation is set to Custom.
    /// One character per step at 10 steps per second: 'a' = dark, 'm' = normal, 'z' = double bright.
    /// </summary>
    [EditorOrder(110), EditorDisplay("Animation"), VisibleIf(nameof(IsCustomAnimation))]
    public string CustomPattern
    {
        get => _customPattern;
        set
        {
            _customPattern = value ?? string.Empty;
            _customPatternValues = PsxLightStyles.Parse(_customPattern);
        }
    }

    /// <summary>
    /// Animation speed multiplier. 1 = original Quake timing (10 steps per second).
    /// </summary>
    [EditorOrder(120), EditorDisplay("Animation"), Limit(0.0f, float.MaxValue, 0.01f)]
    public float AnimationSpeed
    {
        get => _animationSpeed;
        set => _animationSpeed = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// Animation strength. 0 = no animation (steady light), 1 = full pattern.
    /// </summary>
    [EditorOrder(130), EditorDisplay("Animation"), Range(0.0f, 1.0f)]
    public float AnimationStrength
    {
        get => _animationStrength;
        set => _animationStrength = Mathf.Saturate(value);
    }

    /// <summary>
    /// Parsed CustomPattern (brightness multipliers), updated whenever CustomPattern changes.
    /// </summary>
    internal float[] CustomPatternValues => _customPatternValues;

    /// <summary>
    /// Active directional light stamp (serialized). Written by <see cref="VertexLightManager"/> only.
    /// </summary>
    internal int ActiveStamp
    {
        get => _activeStamp;
        set => _activeStamp = value;
    }

    /// <summary>
    /// True if this is a directional light and the active one of its scene. See <see cref="VertexLightManager.SetActiveDirectional"/>.
    /// </summary>
    public bool IsActiveDirectional => VertexLightManager.IsActiveDirectional(this);

    /// <summary>
    /// Direction actually used for lighting. Directional and spot: towards Target if set, otherwise LightDirection.
    /// Area: the actor's forward direction (shape normal). Point: unused.
    /// </summary>
    public Float3 EffectiveDirection => HasDirection ? GetDirectionTowards(_target) : (Float3)Direction;

    /// <summary>
    /// Area only: half size of the shape as used by the shader. Square uses X for both axes, Circle stores the radius in X.
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

    private bool HasRange => _type != PsxLightType.Directional;
    private bool HasDirection => _type == PsxLightType.Directional || _type == PsxLightType.Spot;
    private bool IsSpot => _type == PsxLightType.Spot;
    private bool IsArea => _type == PsxLightType.Area;
    private bool IsCustomAnimation => _animation == PsxLightAnimation.Custom;

    /// <inheritdoc />
    public override void OnEnable()
    {
        base.OnEnable();
        VertexLightManager.Register(this);
    }

    /// <inheritdoc />
    public override void OnDisable()
    {
        VertexLightManager.Unregister(this);
        base.OnDisable();
    }

    /// <summary>
    /// Sets the actor's rotation from a direction vector. Ignores (near) zero vectors.
    /// </summary>
    private void SetDirectionFromVector(Float3 value)
    {
        if (value.LengthSquared < 1e-8f)
            return;
        Direction = value.Normalized;
    }

    /// <summary>
    /// Direction from this light towards the target. Falls back to the actor's direction
    /// if there is no target, the target is this actor, or both are at the same position.
    /// </summary>
    private Float3 GetDirectionTowards(Actor target)
    {
        if (target && target != this)
        {
            Vector3 delta = target.Position - Position;
            var length = delta.Length;
            if (length > 0.001f)
                return delta / length;
        }
        return Direction;
    }

#if FLAX_EDITOR
    /// <summary>
    /// Radius of the always-visible debug marker.
    /// </summary>
    private const float DebugMarkerRadius = 10.0f;

    /// <summary>
    /// Debug draw colour: light colour (opaque) when on, gray when off.
    /// </summary>
    private Color DebugColor => _isOn ? new Color(_color.R, _color.G, _color.B, 1.0f) : FlaxEngine.Color.Gray;

    /// <inheritdoc />
    public override void OnDebugDraw()
    {
        Color color = DebugColor;
        Vector3 position = Position;

        // Small marker so the light is visible in the viewport without being selected
        DebugDraw.DrawWireSphere(new BoundingSphere(position, DebugMarkerRadius), color);

        switch (_type)
        {
        case PsxLightType.Directional:
            DebugDraw.DrawLine(position, position + EffectiveDirection * 100.0f, color);
            break;
        case PsxLightType.Spot:
            DebugDraw.DrawLine(position, position + EffectiveDirection * 50.0f, color);
            break;
        case PsxLightType.Area:
            DrawAreaShape(color);
            break;
        }

        base.OnDebugDraw();
    }

    /// <inheritdoc />
    public override void OnDebugDrawSelected()
    {
        Color color = DebugColor;
        Vector3 position = Position;

        switch (_type)
        {
        case PsxLightType.Directional:
        {
            // Parallel rays to visualize the direction
            Float3 direction = EffectiveDirection;
            PsxLightMath.BuildBasis(direction, out Float3 right, out Float3 up);
            const float spread = 40.0f;
            const float length = 300.0f;
            DebugDraw.DrawLine(position, position + direction * length, color);
            DebugDraw.DrawLine(position + right * spread, position + right * spread + direction * length, color);
            DebugDraw.DrawLine(position - right * spread, position - right * spread + direction * length, color);
            DebugDraw.DrawLine(position + up * spread, position + up * spread + direction * length, color);
            DebugDraw.DrawLine(position - up * spread, position - up * spread + direction * length, color);
            break;
        }
        case PsxLightType.Point:
            DebugDraw.DrawWireSphere(new BoundingSphere(position, _range), color);
            break;
        case PsxLightType.Spot:
        {
            // Cone: four edge lines of length Range plus the circle where they end
            Float3 direction = EffectiveDirection;
            PsxLightMath.BuildBasis(direction, out Float3 right, out Float3 up);
            float halfAngle = _angle * 0.5f * Mathf.DegreesToRadians;
            float axial = Mathf.Cos(halfAngle) * _range;
            float radial = Mathf.Sin(halfAngle) * _range;
            Vector3 discCenter = position + direction * axial;
            DebugDraw.DrawLine(position, discCenter + up * radial, color);
            DebugDraw.DrawLine(position, discCenter - up * radial, color);
            DebugDraw.DrawLine(position, discCenter + right * radial, color);
            DebugDraw.DrawLine(position, discCenter - right * radial, color);
            DebugDraw.DrawCircle(discCenter, direction, radial, color);
            break;
        }
        case PsxLightType.Area:
            // Facing normal and range
            DebugDraw.DrawLine(position, position + (Float3)Direction * Mathf.Min(_range, 100.0f), color);
            DebugDraw.DrawWireSphere(new BoundingSphere(position, _range), color * 0.5f);
            break;
        }

        if (HasDirection && _target && _target != this)
            DebugDraw.DrawLine(position, _target.Position, color * 0.5f);

        base.OnDebugDrawSelected();
    }

    private void DrawAreaShape(Color color)
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
