// www.acidicvoid.com

using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Base class for Flax-PSX vertex lights. These actors are not engine lights: they only carry
/// parameters, which are packed into the vertex light data texture and evaluated by PSX materials.
/// </summary>
public abstract class PsxVertexLight : Actor
{
    private bool _isOn = true;
    private float _brightness = 1.0f;
    private Color _color = FlaxEngine.Color.White;
    private PsxLightAnimation _animation = PsxLightAnimation.Normal;
    private string _customPattern = "m";
    private float _animationSpeed = 1.0f;
    private float _animationStrength = 1.0f;

    /// <summary>
    /// Light type, written to the <c>type</c> field of the light data texture.
    /// </summary>
    public abstract PsxLightType LightType { get; }

    /// <summary>
    /// Turns the light on or off. Lights that are off are not uploaded at all.
    /// Independent of the actor's active state.
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
        set => _customPattern = value ?? string.Empty;
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

    private bool IsCustomAnimation => _animation == PsxLightAnimation.Custom;

    /// <summary>
    /// Sets the actor's rotation from a direction vector. Ignores (near) zero vectors.
    /// </summary>
    protected void SetDirectionFromVector(Float3 value)
    {
        if (value.LengthSquared < 1e-8f)
            return;
        Direction = value.Normalized;
    }

    /// <summary>
    /// Direction from this light towards the target. Falls back to the actor's direction
    /// if there is no target, the target is this actor, or both are at the same position.
    /// </summary>
    protected Float3 GetDirectionTowards(Actor target)
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
    protected const float DebugMarkerRadius = 10.0f;

    /// <summary>
    /// Debug draw colour: light colour (opaque) when on, gray when off.
    /// </summary>
    protected Color DebugColor => _isOn ? new Color(_color.R, _color.G, _color.B, 1.0f) : FlaxEngine.Color.Gray;

    /// <summary>
    /// Builds two vectors perpendicular to the given direction.
    /// </summary>
    protected static void GetPerpendicularAxes(Float3 direction, out Float3 right, out Float3 up)
    {
        Float3 reference = Mathf.Abs(direction.Y) < 0.999f ? Float3.Up : Float3.Right;
        right = Float3.Cross(reference, direction).Normalized;
        up = Float3.Cross(direction, right);
    }

    /// <inheritdoc />
    public override void OnDebugDraw()
    {
        // Small marker so the light is visible in the viewport without being selected
        DebugDraw.DrawWireSphere(new BoundingSphere(Position, DebugMarkerRadius), DebugColor);

        base.OnDebugDraw();
    }
#endif
}
