// www.acidicvoid.com

using System;
using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Lighting;

/// <summary>
/// Light animation patterns and their evaluation (CPU side).
/// Pattern data: Quake light styles 0–11 (id Software, qw-qc/world.qc). Only the raw data is used; parser and evaluation are our own.
/// Timing and brightness mapping follow Quake's R_AnimateLight (WinQuake/r_light.c): 10 steps per second, value = (c - 'a') * 22 / 256.
/// </summary>
public static class PsxLightStyles
{
    /// <summary>
    /// Pattern steps per second at animation speed 1.
    /// </summary>
    public const double StepsPerSecond = 10.0;

    // Raw pattern data, index = PsxLightAnimation value
    private static readonly string[] Patterns =
    {
        "m",                                                   // 0  Normal
        "mmnmmommommnonmmonqnmmo",                             // 1  Flicker 1
        "abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba", // 2  Slow Strong Pulse
        "mmmmmaaaaammmmmaaaaaabcdefgabcdefg",                  // 3  Candle 1
        "mamamamamama",                                        // 4  Fast Strobe
        "jklmnopqrstuvwxyzyxwvutsrqponmlkj",                   // 5  Gentle Pulse
        "nmonqnmomnmomomno",                                   // 6  Flicker 2
        "mmmaaaabcdefgmmmmaaaammmaamm",                        // 7  Candle 2
        "mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",          // 8  Candle 3
        "aaaaaaaazzzzzzzz",                                    // 9  Slow Strobe
        "mmamammmmammamamaaamammma",                           // 10 Fluorescent Flicker
        "abcdefghijklmnopqrrqponmlkjihgfedcba",                // 11 Slow Pulse (No Black)
    };

    private static readonly float[][] ParsedPatterns = ParseAll();

    /// <summary>
    /// Parses a pattern string into brightness multipliers. Letters a–z (case-insensitive) are used, everything else is ignored.
    /// 'a' = 0, 'm' ≈ 1.03, 'z' ≈ 2.15.
    /// </summary>
    /// <param name="pattern">Pattern string.</param>
    /// <returns>Brightness multipliers, one per step. Empty if the pattern contains no letters.</returns>
    public static float[] Parse(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return Array.Empty<float>();

        int count = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (IsPatternChar(pattern[i]))
                count++;
        }

        var values = new float[count];
        int index = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (!IsPatternChar(c))
                continue;
            values[index++] = (char.ToLowerInvariant(c) - 'a') * 22.0f / 256.0f;
        }
        return values;
    }

    /// <summary>
    /// Brightness multiplier of a light at the given time. Returns 1 for steady lights.
    /// </summary>
    /// <param name="light">The light.</param>
    /// <param name="time">Time in seconds.</param>
    public static float Evaluate(PsxVertexLight light, float time)
    {
        var animation = light.Animation;
        float strength = light.AnimationStrength;
        if (animation == PsxLightAnimation.Normal || strength <= 0.0f)
            return 1.0f;

        float[] values;
        if (animation == PsxLightAnimation.Custom)
            values = light.CustomPatternValues;
        else if ((int)animation >= 0 && (int)animation < ParsedPatterns.Length)
            values = ParsedPatterns[(int)animation];
        else
            return 1.0f;

        return Mathf.Lerp(1.0f, Sample(values, time, light.AnimationSpeed), strength);
    }

    /// <summary>
    /// Samples a parsed pattern. No interpolation between steps (authentic to Quake).
    /// </summary>
    /// <param name="values">Parsed pattern.</param>
    /// <param name="time">Time in seconds.</param>
    /// <param name="speed">Speed multiplier (1 = 10 steps per second).</param>
    /// <returns>Brightness multiplier, 1 if the pattern is empty.</returns>
    public static float Sample(float[] values, float time, float speed)
    {
        if (values == null || values.Length == 0)
            return 1.0f;
        double steps = Math.Max(0.0, time * StepsPerSecond * speed);
        long index = (long)steps % values.Length;
        return values[index];
    }

    private static bool IsPatternChar(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    private static float[][] ParseAll()
    {
        var result = new float[Patterns.Length][];
        for (int i = 0; i < Patterns.Length; i++)
            result[i] = Parse(Patterns[i]);
        return result;
    }
}
