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
half4 Dither(half4 col, float2 uv, float ditherStr)
{
    int dither_u = psx_dither_table[int(uv.x % 4)][int(uv.y % 4)];
    // Apply dithering according to PSY-Q Docs
    col += (dither_u / 2.0 - 4.0) * ditherStr;
    return col;
}

// Truncates colors to PSX-like 5bpc precision
// Adds PSX style dithering
half4 ColorPostProcessing(half4 col, float2 uv, float ditherStr, bool psxPrec, bool highColor)
{
    // Coloes per pixel
    int colors = highColor ? HIGH_COLOR_CNT : LOW_COLOR_CNT;
    
    col *= highColor ? 1 : 255;
    // Apply dithering according to PSY-Q Docs
    col = ditherStr <= 0 ? col : Dither(col, uv, ditherStr);
    // Truncate to 5bpc precision via bitwise AND operator, and limit value max to prevent wrapping
    // HEX 0xf8 -> DEC 248
    if (!highColor && psxPrec)
    {
        col = lerp((half4)(uint4(col) & 0xf8), 0xf8, step(0xf8,col));
    }
    else if (!highColor && !psxPrec)
    {
        col = lerp((half4)(uint4(col) & LOW_COLOR_CNT), LOW_COLOR_CNT, step(LOW_COLOR_CNT,col));
    }
    
    return col / (highColor ? 1 : 255);
}

float3 ConvertToPsxColorRange(float3 color)
{
    color *= 255;
    color = lerp((uint3(color) & 0xf8), 0xf8, step(0xf8,color));
    color /= 255;
    return color;
}