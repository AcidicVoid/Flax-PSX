// www.acidicvoid.com

using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Math shared between the CPU-side packing and the vertex lighting shader.
/// Anything here that the shader relies on must be implemented identically in HLSL.
/// </summary>
public static class PsxLightMath
{
    /// <summary>
    /// Builds a fixed orthonormal basis around a normal (left-handed, Y up).
    /// HLSL counterpart must use the same reference vectors and the same cross product order.
    /// </summary>
    /// <param name="normal">Normalized direction.</param>
    /// <param name="right">Basis right vector.</param>
    /// <param name="up">Basis up vector.</param>
    public static void BuildBasis(Float3 normal, out Float3 right, out Float3 up)
    {
        Float3 reference = Mathf.Abs(normal.Y) < 0.999f ? Float3.Up : Float3.Right;
        right = Float3.Cross(reference, normal).Normalized;
        up = Float3.Cross(normal, right);
    }

    /// <summary>
    /// Roll angle (radians) of a shape around its normal, measured against <see cref="BuildBasis"/>.
    /// The shader rebuilds the shape's right axis as <c>cos(roll) * basisRight + sin(roll) * basisUp</c>.
    /// </summary>
    /// <param name="normal">Normalized facing direction of the shape.</param>
    /// <param name="shapeRight">The shape's right axis (perpendicular to the normal).</param>
    public static float ComputeRoll(Float3 normal, Float3 shapeRight)
    {
        BuildBasis(normal, out Float3 basisRight, out Float3 basisUp);
        return Mathf.Atan2(Float3.Dot(shapeRight, basisUp), Float3.Dot(shapeRight, basisRight));
    }
}
