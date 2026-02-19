// www.acidicvoid.com 

#ifndef __FLAX_PSX__POST_PROCESSING_LEGACY__
#define __FLAX_PSX__POST_PROCESSING_LEGACY__

#include "./Flax/Common.hlsl"
#include "./Flax/GBuffer.hlsl"
#include "./FlaxPSX/includes/psx.hlsl"
#include "./FlaxPSX/includes/fog.hlsl"

META_CB_BEGIN(0, Data)
  float2 sceneRenderSize;
  float2 upscaledSize;
  float  depthNear;
  float  depthFar;
  float  depthProd;
  float  depthDiff;
  float  depthDiffDiffRecip;
  float  fogBoost;
  int    useDithering;
  int    useHighColor;
  float4 fogColor;
  float  scanlineStrength;
  float  ditherStrength;
  float  ditherBlend;
  int    ditherSize;
  int    usePsxColorPrecision;
  float2 sceneToUpscaleRatio; 
  int    fogStyle;
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
    // The core linear depth calculation: L_actual = (N*F) / (F - Z_raw * (F-N))    
    // Calculate linear depth (L_actual = depthProd / Denominator)
    float linear_depth_actual = depthProd / (depthFar - z_buffer * depthDiff);
    
    // Normalization to [0, 1]: (L_actual - N) * (1 / (F-N))
    // Uses: depthNear = N, depthDiffDiffRecip = 1.0 / (F-N)
    return (linear_depth_actual - depthNear) * depthDiffDiffRecip; 
}

// Optimized Scanlines function using the pre-calculated sceneToUpscaleRatio uniform.
float Scanlines(float2 uv, float2 sceneRenderSize, float2 upscaledSize, float strength) {
    // Scale UV to upscaled pixel space
    float2 pixelCoord = uv * upscaledSize;
    // Convert to original scene render space using pre-calculated ratio
    float2 scenePixelCoord = pixelCoord * sceneToUpscaleRatio; 
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
frag_out PS_FlaxPsxPostProcessingLegacy(Quad_VS2PS input)
{
    // Prepare resources
    frag_out o;
    // half4 ui = uiTexture.Sample(SamplerPointClamp, input.TexCoord);
    half4 scene = sceneTexture.Sample(SamplerPointClamp, input.TexCoord); 

    // Get and linearize depth buffer
    float depth = SAMPLE_RT(DepthBuffer, input.TexCoord);
    float depth01 = NormalizeZ(depth, depthNear, depthFar);

    // Set depth
    o.depth = depth;

    // Dither Strength
    float ditherStr = ditherStrength * useDithering;

    // Coords for dither pattern
    float2 ditherUv = floor(input.TexCoord * float2(sceneRenderSize.x * (float)ditherSize, sceneRenderSize.y * (float)ditherSize));

    // Apply fog to the scene color
    // 1 -> SH1 style fog
    if (fogStyle == 1) {
        float fog = SH1Fog(depth01, fogColor.a, fogBoost);
        scene = half4(lerp(scene.rgb, fogColor.rgb, fogColor.rgb * fog), fog);
    }

    // Color post processing (color range + dithering)
    half4 sceneProcessed;

    // Skip the expensive ColorPostProcessing call if both features are disabled
    if ((useDithering == 0) && (usePsxColorPrecision == 0)) {
        sceneProcessed = scene; // Use fogged scene directly
    } else {
        sceneProcessed = ColorPostProcessing(scene, ditherUv, ditherStr, usePsxColorPrecision, useHighColor);
        sceneProcessed = lerp(scene, sceneProcessed,  ditherBlend);
    }   
    
    // Assign final color to output
    o.color = sceneProcessed * Scanlines(input.TexCoord, sceneRenderSize, upscaledSize, scanlineStrength);
    return o; 
}

#endif