// www.acidicvoid.com

using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Vertex light type. Values match the <c>type</c> field of the light data texture.
/// Can be changed at any time via <see cref="PsxVertexLight.LightType"/>.
/// </summary>
public enum PsxLightType
{
    /// <summary>
    /// Empty slot, ignored by the shader. Not selectable as a light type.
    /// </summary>
    [HideInEditor]
    Empty = 0,

    /// <summary>
    /// Directional light (sun/moon). No range.
    /// </summary>
    Directional = 1,

    /// <summary>
    /// Omnidirectional point light.
    /// </summary>
    Point = 2,

    /// <summary>
    /// Spot light with a light cone.
    /// </summary>
    Spot = 3,

    /// <summary>
    /// Area light (square, rectangle or circle) facing the actor's forward direction.
    /// </summary>
    Area = 4,
}

/// <summary>
/// Shape of an area light. Square and Circle only use Dimensions.X.
/// </summary>
public enum PsxAreaLightShape
{
    /// <summary>
    /// Square, side length = Dimensions.X. Sent to the GPU as a rectangle.
    /// </summary>
    Square = 0,

    /// <summary>
    /// Rectangle, width = Dimensions.X, height = Dimensions.Y.
    /// </summary>
    Rectangle = 1,

    /// <summary>
    /// Circle, diameter = Dimensions.X.
    /// </summary>
    Circle = 2,
}

/// <summary>
/// Light falloff curve, based on Robert Penner's easing equations (MIT).
/// Attenuation is <c>1 - ease(distance / range)</c>: "In" curves stay bright and cut off late (hard edge),
/// "Out" curves fade early with a long dim tail (soft), "InOut" curves fade smoothly.
/// Values are written to the shader as integers (0–24) and must stay below 32 (spot cone packing).
/// Back and Elastic are intentionally excluded since they overshoot below 0 and above 1.
/// </summary>
public enum PsxLightFalloff
{
    /// <summary>Linear fade from full brightness to zero at range.</summary>
    [EditorDisplay(null, "Linear")] Linear = 0,

    /// <summary>easeInSine</summary>
    [EditorDisplay(null, "Sine (hard edge)")] InSine = 1,
    /// <summary>easeOutSine</summary>
    [EditorDisplay(null, "Sine (soft)")] OutSine = 2,
    /// <summary>easeInOutSine</summary>
    [EditorDisplay(null, "Sine (smooth)")] InOutSine = 3,

    /// <summary>easeInQuad</summary>
    [EditorDisplay(null, "Quad (hard edge)")] InQuad = 4,
    /// <summary>easeOutQuad</summary>
    [EditorDisplay(null, "Quad (soft)")] OutQuad = 5,
    /// <summary>easeInOutQuad</summary>
    [EditorDisplay(null, "Quad (smooth)")] InOutQuad = 6,

    /// <summary>easeInCubic</summary>
    [EditorDisplay(null, "Cubic (hard edge)")] InCubic = 7,
    /// <summary>easeOutCubic</summary>
    [EditorDisplay(null, "Cubic (soft)")] OutCubic = 8,
    /// <summary>easeInOutCubic</summary>
    [EditorDisplay(null, "Cubic (smooth)")] InOutCubic = 9,

    /// <summary>easeInQuart</summary>
    [EditorDisplay(null, "Quart (hard edge)")] InQuart = 10,
    /// <summary>easeOutQuart</summary>
    [EditorDisplay(null, "Quart (soft)")] OutQuart = 11,
    /// <summary>easeInOutQuart</summary>
    [EditorDisplay(null, "Quart (smooth)")] InOutQuart = 12,

    /// <summary>easeInQuint</summary>
    [EditorDisplay(null, "Quint (hard edge)")] InQuint = 13,
    /// <summary>easeOutQuint</summary>
    [EditorDisplay(null, "Quint (soft)")] OutQuint = 14,
    /// <summary>easeInOutQuint</summary>
    [EditorDisplay(null, "Quint (smooth)")] InOutQuint = 15,

    /// <summary>easeInExpo</summary>
    [EditorDisplay(null, "Expo (hard edge)")] InExpo = 16,
    /// <summary>easeOutExpo</summary>
    [EditorDisplay(null, "Expo (soft)")] OutExpo = 17,
    /// <summary>easeInOutExpo</summary>
    [EditorDisplay(null, "Expo (smooth)")] InOutExpo = 18,

    /// <summary>easeInCirc</summary>
    [EditorDisplay(null, "Circ (hard edge)")] InCirc = 19,
    /// <summary>easeOutCirc</summary>
    [EditorDisplay(null, "Circ (soft)")] OutCirc = 20,
    /// <summary>easeInOutCirc</summary>
    [EditorDisplay(null, "Circ (smooth)")] InOutCirc = 21,

    /// <summary>easeInBounce</summary>
    [EditorDisplay(null, "Bounce (hard edge)")] InBounce = 22,
    /// <summary>easeOutBounce</summary>
    [EditorDisplay(null, "Bounce (soft)")] OutBounce = 23,
    /// <summary>easeInOutBounce</summary>
    [EditorDisplay(null, "Bounce (smooth)")] InOutBounce = 24,
}

/// <summary>
/// Light animation style. Values 0–11 match Quake's light style indices (world.qc).
/// The patterns themselves are evaluated on the CPU later, not by the light actors.
/// </summary>
public enum PsxLightAnimation
{
    /// <summary>Quake style 0: steady light.</summary>
    [EditorDisplay(null, "None (Normal)")] Normal = 0,
    /// <summary>Quake style 1: flicker (first variety).</summary>
    [EditorDisplay(null, "Flicker 1")] Flicker1 = 1,
    /// <summary>Quake style 2: slow strong pulse.</summary>
    [EditorDisplay(null, "Slow Strong Pulse")] SlowStrongPulse = 2,
    /// <summary>Quake style 3: candle (first variety).</summary>
    [EditorDisplay(null, "Candle 1")] Candle1 = 3,
    /// <summary>Quake style 4: fast strobe.</summary>
    [EditorDisplay(null, "Fast Strobe")] FastStrobe = 4,
    /// <summary>Quake style 5: gentle pulse.</summary>
    [EditorDisplay(null, "Gentle Pulse")] GentlePulse = 5,
    /// <summary>Quake style 6: flicker (second variety).</summary>
    [EditorDisplay(null, "Flicker 2")] Flicker2 = 6,
    /// <summary>Quake style 7: candle (second variety).</summary>
    [EditorDisplay(null, "Candle 2")] Candle2 = 7,
    /// <summary>Quake style 8: candle (third variety).</summary>
    [EditorDisplay(null, "Candle 3")] Candle3 = 8,
    /// <summary>Quake style 9: slow strobe.</summary>
    [EditorDisplay(null, "Slow Strobe")] SlowStrobe = 9,
    /// <summary>Quake style 10: fluorescent flicker.</summary>
    [EditorDisplay(null, "Fluorescent Flicker")] FluorescentFlicker = 10,
    /// <summary>Quake style 11: slow pulse, not fading to black.</summary>
    [EditorDisplay(null, "Slow Pulse (No Black)")] SlowPulseNoBlack = 11,
    /// <summary>Custom pattern string ('a' = dark, 'm' = normal, 'z' = double bright).</summary>
    [EditorDisplay(null, "Custom")] Custom = 12,
}

/// <summary>
/// Maximum number of vertex lights uploaded per frame. Only sets the light data texture size;
/// shader cost scales with the lights actually uploaded.
/// </summary>
public enum PsxVertexLightCap
{
    /// <summary>Up to 32 lights.</summary>
    [EditorDisplay(null, "32")] Lights32 = 32,
    /// <summary>Up to 64 lights (default).</summary>
    [EditorDisplay(null, "64")] Lights64 = 64,
    /// <summary>Up to 128 lights.</summary>
    [EditorDisplay(null, "128")] Lights128 = 128,
    /// <summary>Up to 256 lights.</summary>
    [EditorDisplay(null, "256")] Lights256 = 256,
}
