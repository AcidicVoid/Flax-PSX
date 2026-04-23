// PS1 dither table from PSY-Q Docs:
// https://psx.arthus.net/sdk/Psy-Q/DOCS/LIBREF46.PDF, PDF page 242
static const uint4x4 psx_dither_table = uint4x4
(
     0,     8,     2,    10,
    12,     4,    14,     6,
     3,    11,     1,     9,
    15,     7,    13,     5
);

static const uint LOW_COLOR_CNT = 255;
static const uint HIGH_COLOR_CNT = 32768;

// Adds PSX style dithering only
half4 Dither(half4 col, int2 screenPos, float ditherStr)
{
    // Matrix indexing: table[row][column]
    // Use modulo 4 on integer coordinates
    float dither_val = psx_dither_table[screenPos.y % 4][screenPos.x % 4];
    
    // Convert 0..15 range to -4.0..3.5 range
    float offset = (dither_val / 2.0 - 4.0) * ditherStr;
    
    // Apply offset (assuming col is in 0..255 range)
    return col + offset;
}

// Truncates colors to PSX-like 5bpc precision
// Adds PSX style dithering
half4 ColorPostProcessing(half4 col, float2 uv, float renderSize, float ditherStr, bool psxPrec, bool highColor)
{
    // Convert normalized UV to integer Pixel Coordinates
    int2 screenPos = int2(uv * renderSize);
    
    col *= 255.0;
    // Apply dithering according to PSY-Q Docs
    if (ditherStr > 0) 
    {
        col = Dither(col, screenPos, ditherStr);
    }
    // We clamp to 0-255 first to prevent negative wrap-around glitches
    col = clamp(col, 0.0, 255.0);
    
    if (!highColor && psxPrec)
    {
        // Truncate to 5bpc precision via bitwise AND operator, and limit value max to prevent wrapping
        // HEX 0xf8 -> DEC 248
        // lerp/step prevents values from exceeding 248 to mimic PSX hardware limits
        col = lerp((half4)(uint4(col) & 0xf8), 248.0, step(248.0, col));
    }

    // 5. Return to 0..1 range
    return col / 255.0;
}

float3 ConvertToPsxColorRange(float3 color)
{
    color *= 255;
    color = lerp((uint3(color) & 0xf8), 0xf8, step(0xf8,color));
    color /= 255;
    return color;
}