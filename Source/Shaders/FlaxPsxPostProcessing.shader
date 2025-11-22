// www.acidicvoid.com 

#ifndef __FLAX_PSX__POST_PROCESSING__
#define __FLAX_PSX__POST_PROCESSING__

#include "./Flax/Common.hlsl"
#include "./Flax/GBuffer.hlsl"
#include "./Flax-PSX/includes/RetroFX.hlsl"
#include "./Flax-PSX/includes/Fog.hlsl"
#include "./Flax-PSX/includes/Functions.hlsl"

META_CB_BEGIN(0, Data)
  float2 sceneRenderSize;
  float2 upscaledSize;
  float  near;
  float  far;
  float  fogBoost;
  float  falloff;
  float4 fogColor;
  float  fogMin;
  float  scanlineStrength;
  int    useDithering;
  float  ditherStrength;
  float  ditherBlend;
  int    ditherSize;
META_CB_END

Texture2D sceneTexture   : register(t0);
Texture2D uiTexture      : register(t1);
Texture2D DepthBuffer    : register(t2);
float4 c0 = float4(1,1,1,1);
float  c1 = 1.70158;
float  c2 = 2.70158;

struct frag_out
{
  float4 color : SV_Target;
  float  depth : SV_Depth;
};

float NormalizeZ(float z_buffer, float z_near, float z_far)
{
    float linear_depth_actual = z_near * z_far / (z_far - z_buffer * (z_far - z_near));
    return (linear_depth_actual - z_near) / (z_far - z_near);
}

float Scanlines(float2 uv, float2 sceneRenderSize, float2 upscaledSize, float strength) {
    // Scale UV to upscaled pixel space
    float2 pixelCoord = uv * upscaledSize;
    // Convert to original scene render space
    float2 scenePixelCoord = pixelCoord * (sceneRenderSize / upscaledSize);
    // Every second horizontal line: use fmod or frac
    float linePattern = fmod(floor(scenePixelCoord.y), 2.0);
    // Return 1.0 for visible lines, 0.0 for scanline gaps
    return clamp(1 - strength + linePattern,0,1);
}

float2 snapUv(float2 uv, float2 steps)
{
    float2 stepSize = 1.0 / steps;
    return floor(uv * steps) / steps + stepSize * 0.5;
}

META_PS(true, FEATURE_LEVEL_ES3)
frag_out PS_FlaxPsxPostProcessing(Quad_VS2PS input)
{
    // Prepare resources
    frag_out o;
    half4 ui = uiTexture.Sample(SamplerPointClamp, input.TexCoord);
    half4 scene = sceneTexture.Sample(SamplerPointClamp, input.TexCoord); 

    // Get and linearize depth buffer
    float depth = SAMPLE_RT(DepthBuffer, input.TexCoord);
    float depth01 = NormalizeZ(depth, near, far);

    // Dither Strength
    float ditherStr = ditherStrength * useDithering;

    // Coords for dither pattern
    float2 ditherUv = floor(input.TexCoord * float2(sceneRenderSize.x * (float)ditherSize, sceneRenderSize.y * (float)ditherSize));

    // Fog
    half fogFalloff = lerp(FogFalloffOutBack(c1,c2,depth01), FogFalloffOutQuint(depth01), falloff);
    half fog = clamp(fogFalloff,fogMin,1);
         fog = InterleavedGradientNoise1(ditherUv, fog, 0.005);
    scene = half4(lerp(scene.rgb,fog,fogColor.rgb * fogColor.a),1);

    half4  sceneProcessed = ColorPostProcessing(scene, ditherUv, ditherStr);
           sceneProcessed = lerp(scene, sceneProcessed, ditherBlend);

    // Combine
    o.color = lerp(sceneProcessed, ui, 0) * Scanlines(input.TexCoord, sceneRenderSize, upscaledSize, scanlineStrength);
    o.depth = depth;
    return o; 
}

#endif