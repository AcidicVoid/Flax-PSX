// www.acidicvoid.com

#ifndef __FLAX_PSX_ADDITIONAL_POST_PROCESSING__
#define __FLAX_PSX_ADDITIONAL_POST_PROCESSING__

struct frag_out
{
    half3 color : SV_Target;
};

#include "./Flax/Common.hlsl"

META_CB_BEGIN(0, Data)
    float2 resolution;
    float2 texelSize;
    float2 slotmaskSize;
    float  aspectRatio;          // viewport aspect ratio  
    float  internalAspectRatio;  // aspect ratio of low-res renderer
    float  crtOverlayStretchX;
    float  crtOverlayStretchY;
    float  curvatureX;
    float  curvatureY;
    int    useCrtOverlay;
    int    slotmaskBlendMode;
    float  slotmaskScale;
    float  slotMaskStrength;
    float  blurX;
    float  blurY;
    float2 pillarboxMin;
    float2 pillarboxMax;
    float  brightnessBoost;
META_CB_END

Texture2D sceneTexture : register(t0);
Texture2D slotMaskTexture : register(t1);
Texture2D crtTexture : register(t2);

static const float kBlurOffsets[5] =
{
    0.0, 1.0, 2.0, 3.0, 4.0
};

static const float kCoefficients[5] =
{
    0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162
};
/*
static float Max3(float a, float b, float c)
{
    return max(a, max(b, c));
}
*/
static half3 Tonemap_Exp(half3 c)
{
    return 1.0h - exp(-c);
}

static half3 BlendScreen(half3 a, half3 b)
{
    return 1.0 - (1.0 - a) * (1.0 - b);
}

static half3 BlendOverlay(half3 a, half3 b)
{
    float3 low  = 2.0 * a * b;
    float3 high = 1.0 - 2.0 * (1.0 - a) * (1.0 - b);
    return lerp(low, high, step(0.5, a));
}

META_PS(true, FEATURE_LEVEL_ES3)
frag_out PS_FlaxPsxAdditionalPostProcessing(Quad_VS2PS input)
{
    frag_out o;
    float2 uv = input.TexCoord;

    // Calculate pillar-box area
    const bool pillarbox = any(uv < pillarboxMin) || any(uv > pillarboxMax);
    if (pillarbox) 
    {
        o.color = half3(0,0,0);
        return o;
    }

    // CRT Overlay
    half4 crtOverlay = half4(0,0,0,0);    
    if (useCrtOverlay == 1)
    {
        float2 crtUv = uv - 0.5;

        // Aspect-ratio compensation (unchanged)
        crtUv.x *= aspectRatio / internalAspectRatio;

        // Stretch: 1.0 = original, 0.5 = half-size
        crtUv /= float2(crtOverlayStretchX, crtOverlayStretchY);
        crtUv += 0.5;
        crtOverlay = any(crtUv < 0.0) || any(crtUv > 1.0) ? half4(0,0,0,0) : crtTexture.Sample(SamplerLinearClamp, crtUv);
    }

    // Apply curvature
    if (curvatureX > 0.001 || curvatureY > 0.001)
    {
        float2 d = uv - 0.5;
        float2 curvature = float2(curvatureX, curvatureY);
        float k = d.x * d.x * curvature.x + d.y * d.y * curvature.y; // Axis-weighted squared distance
        uv = d * (1.0 + k) + 0.5;
    }

    const half3 original = sceneTexture.Sample(SamplerPointClamp, uv);
    half3 scene = original;

    // Gaussian-ish blur
    if (blurX > 0.001 || blurY > 0.001)  // Use blur factors to determine if blur is enabled
    {
        half3 blurred = original * (half)kCoefficients[0];
        [unroll]
        for (int i = 1; i < 5; i++)  // Always use all 5 samples
        {
            const float2 tapOffset =
                float2(texelSize.x * kBlurOffsets[i] * blurX,
                       texelSize.y * kBlurOffsets[i] * blurY);

            blurred += sceneTexture.Sample(SamplerLinearClamp, uv + tapOffset) * (half)kCoefficients[i];
            blurred += sceneTexture.Sample(SamplerLinearClamp, uv - tapOffset) * (half)kCoefficients[i];
        }
        const half blurStrength = (half)saturate(max(blurX, blurY));
        scene = lerp(scene, blurred, blurStrength);
    }

    // Exposure-like brightness (0..1 -> ~0..+2 stops), then tone-map back into 0..1-ish.
    if (brightnessBoost > 0.001)
    {
        const half stops = (half)(2.0 * saturate(brightnessBoost)); // 0..2 stops
        const half exposure = exp2(stops);                          // 1..4
        scene.rgb = Tonemap_Exp(scene.rgb * exposure);
    }

    // Slot-mask modulation
    if ((slotmaskBlendMode >= 1) && (slotMaskStrength > 0.001))
    {
        half3 slotmask = slotMaskTexture.Sample(SamplerLinearWrap, uv * slotmaskSize / slotmaskScale).rgb;
        half3 blended = slotmask * scene.rgb; // Default: multiply
        if (slotmaskBlendMode > 1)
        {
            blended = (slotmaskBlendMode < 3) 
                ? BlendOverlay(scene.rgb, slotmask)   // mode 2
                : BlendScreen(scene.rgb, slotmask);    // mode 3
        }
        scene = lerp(scene, blended, (half)saturate(slotMaskStrength));
    }

    scene = saturate(scene); 
    
    // Apply CRT Overlay
    if (useCrtOverlay == 1)
    {
        scene = lerp(scene, crtOverlay, crtOverlay.a);
    }

    o.color = scene;
    return o;
}

#endif
