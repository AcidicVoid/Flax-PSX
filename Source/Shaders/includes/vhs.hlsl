/*
    FLAX PSX VHS SHADER (HLSL Port)
    Original by hunterk, adapted from ompuco
    Ported to generic HLSL for post-processing
*/

// ===================================================================================
// TEXTURES & SAMPLERS
// ===================================================================================
Texture2D InputTexture : register(t0);
Texture2D OverlayTexture : register(t1); // The "Play" or "Rec" OSD texture

SamplerState BasicSampler : register(s0);

// ===================================================================================
// STRUCTS
// ===================================================================================
struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float2 uv  : TEXCOORD0;
};

// ===================================================================================
// HELPER FUNCTIONS
// ===================================================================================

// GLSL 'mod' behaves differently than HLSL 'fmod' for negative numbers.
// This is a safe implementation.
float nmod(float x, float y)
{
    return x - y * floor(x / y);
}

float onOff(float a, float b, float c, float t)
{
    return step(c, sin((t * 0.001) + a * cos((t * 0.001) * b)));
}

// Color Space Conversions
float3 rgb2yiq(float3 c)
{
    return float3(
        (0.2989 * c.x + 0.5959 * c.y + 0.2115 * c.z),
        (0.5870 * c.x - 0.2744 * c.y - 0.5229 * c.z),
        (0.1140 * c.x - 0.3216 * c.y + 0.3114 * c.z)
    );
}

float3 yiq2rgb(float3 c)
{
    return float3(
        (1.0 * c.x + 0.956 * c.y + 0.6210 * c.z),
        (1.0 * c.x - 0.2720 * c.y - 0.6474 * c.z),
        (1.0 * c.x - 1.1060 * c.y + 1.7046 * c.z)
    );
}

// Logic to smear the texture lookup based on angle
float2 Circle(float Start, float Points, float Point)
{
    float Rad = (3.141592 * 2.0 * (1.0 / Points)) * (Point + Start);
    // Note: The original shader has a specific artistic quirk here:
    // It returns linear X and Cosine Y, rather than Sin/Cos. Preserved for accuracy.
    return float2(-(0.3 + Rad), cos(Rad));
}

// The heavy lifting blur function
float3 Blur(float2 uv, float d, float iTime)
{
    // t is calculated but effectively reset to 0.0 in original source immediately.
    // Kept 0.0 for optimization.
    float t = 0.0; 
    float b = 1.0;

    float2 PixelOffset = float2(d + 0.0005 * t, 0);

    float Start = 2.0 / 14.0;
    float2 Scale = 0.66 * 4.0 * 2.0 * PixelOffset.xy;

    // Manual unroll of 15 texture taps (Original N0 to N14)
    float3 sum = float3(0,0,0);
    
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 0.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 1.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 2.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 3.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 4.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 5.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 6.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 7.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 8.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 9.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 10.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 11.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 12.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv + Circle(Start, 14.0, 13.0) * Scale).rgb;
    sum += InputTexture.Sample(BasicSampler, uv).rgb; // N14 is just center

    float W = 1.0 / 15.0;
    return (sum * W) * b;
}

// Vertical jitter logic
float2 jumpy(float2 uv, float t, float w)
{
    float2 look = uv;
    float window = 1.0 / (1.0 + 80.0 * (look.y - nmod(t / 4.0, 1.0)) * (look.y - nmod(t / 4.0, 1.0)));
    
    look.x += 0.05 * sin(look.y * 10.0 + t) / 20.0 * onOff(4.0, 4.0, 0.3, t) * (0.5 + cos(t * 20.0)) * window;
    
    float vShift = (0.1 * w) * 0.4 * onOff(2.0, 3.0, 0.9, t) * (sin(t) * sin(t * 20.0) + (0.5 + 0.1 * sin(t * 200.0) * cos(t)));
    
    look.y = nmod(look.y - 0.01 * vShift, 1.0);
    return look;
}
/*
    Node Code:
    Output0.xyz = flax_psx_vhs(Input0, Input1.xy, Input2.x, Input2.y, Input2.z, Input2.w)
*/

// ===================================================================================
// MAIN PIXEL SHADER
// ===================================================================================
float4 flax_psx_vhs(float4 Color, float2 uv, float Time, float FrameCount, float WiggleParam, float SmearParam) : SV_Target
{
    return Color;
    // Mimic the iTime macro from original: mod(float(FrameCount), 7.0)
    // We use generic Time variable for smoothness, but keep the modulo 7 logic
    float iTime = nmod(FrameCount, 7.0);

    // Calculate D (Distortion intensity based on time)
    float d = 0.1 - ceil(nmod(iTime / 3.0, 1.0) + 0.5) * 0.1;

    // Calculate Jittered UVs
    uv = jumpy(uv, iTime, WiggleParam);
    float2 uv2 = uv; // Copy for overlay

    // Calculate S (Vertical noise/stripes)
    float s = 0.0001 * -d + 0.0001 * WiggleParam * sin(iTime);

    // Calculate E (Edge artifacts)
    float e = min(0.30, pow(max(0.0, cos(uv.y * 4.0 + 0.3) - 0.75) * (s + 0.5) * 1.0, 3.0)) * 25.0;
    
    // Horizontal distortion based on time and scanline
    float r = (iTime * (2.0 * s));
    uv.x += abs(r * pow(min(0.003, (-uv.y + (0.01 * nmod(iTime, 17.0)))) * 3.0, 2.0));

    // Refine D for Blur intensity
    d = 0.051 + abs(sin(s / 4.0));
    float c = max(0.0001, 0.002 * d) * SmearParam;

    float4 finalColor;

    // --- Pass 1: Blur Y (Luma) ---
    finalColor.xyz = Blur(uv, c + c * (uv.x), iTime);
    float y = rgb2yiq(finalColor.xyz).r;

    // --- Pass 2: Blur I (Chroma Orange/Blue) ---
    uv.x += 0.01 * d;
    c *= 6.0;
    finalColor.xyz = Blur(uv, c, iTime);
    float i = rgb2yiq(finalColor.xyz).g;

    // --- Pass 3: Blur Q (Chroma Purple/Green) ---
    uv.x += 0.005 * d;
    c *= 2.50;
    finalColor.xyz = Blur(uv, c, iTime);
    float q = rgb2yiq(finalColor.xyz).b;

    // Recombine YIQ to RGB and apply noise (s+e)
    finalColor = float4(yiq2rgb(float3(y, i, q)) - pow(s + e * 2.0, 3.0), 1.0);

    // --- Overlay Handling (Play/Rec text) ---
    // If you don't use an overlay texture, this section essentially does nothing
    // assuming the sampler returns black or you bind a transparent 1x1 texture.
    float2 overlayUV = uv2; 
    
    // Note: The original code scaled UVs based on texture/input size ratio here.
    // Assuming standard normalized UVs (0-1) for both implies direct mapping.
    
    float4 play_osd = OverlayTexture.Sample(BasicSampler, overlayUV);
    
    // Blink logic for the overlay
    float timer = FrameCount; 
    // Logic: Blink every 50 frames, don't show at t=0, stop after 500 frames
    float show_overlay = (nmod(timer, 100.0) < 50.0) && (timer != 0.0) && (timer < 500.0) ? play_osd.a : 0.0;
    show_overlay = saturate(show_overlay); // clamp 0..1

    finalColor = lerp(finalColor, play_osd, show_overlay);

    return finalColor;
}