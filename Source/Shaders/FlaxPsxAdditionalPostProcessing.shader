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
    int    slotmaskBlendMode;
    float  slotmaskScale;        // expected 0..1
    float  slotMaskStrength;     // expected 0..1
    float  blurX;                // scale in X
    float  blurY;                // scale in Y
    float  brightnessBoost;      // expected 0..1 (0 = off, 1 = strong)
    float  blendWithOriginal;    // expected 0..1 (0 = fully processed, 1 = fully original)
META_CB_END

Texture2D sceneTexture : register(t0);
Texture2D slMskTexture : register(t1);

static const float kBlurOffsets[5] =
{
    0.0, 1.0, 2.0, 3.0, 4.0
};

static const float kCoefficients[5] =
{
    0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162
};

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

    const half3 original = sceneTexture.Sample(SamplerPointClamp, input.TexCoord);
    half3 scene = original;
    bool  pillarbox = !all(abs(input.TexCoord - 0.5) <= min(1.0, float2(internalAspectRatio / aspectRatio, aspectRatio / internalAspectRatio)) * 0.5);

    // Gaussian-ish blur around the original scene sample
    half3 blurred = original * (half)kCoefficients[0];
    if (blurX > 0.01 || blurY > 0.01)
    {
        [unroll]
        for (int i = 1; i < 5; i++)
        {
            const float2 tapOffset =
                float2(texelSize.x * kBlurOffsets[i] * blurX,
                       texelSize.y * kBlurOffsets[i] * blurY);

            blurred  += sceneTexture.Sample(SamplerLinearClamp, input.TexCoord + tapOffset) * (half)kCoefficients[i];
            blurred  += sceneTexture.Sample(SamplerLinearClamp, input.TexCoord - tapOffset) * (half)kCoefficients[i];
        }
        const half blurMix = (half)saturate(max(blurX, blurY));
        scene = lerp(scene, blurred, blurMix);
    }

    // Saturation for safety
    blurred = saturate(blurred);

    // Exposure-like brightness (0..1 -> ~0..+2 stops), then tone-map back into 0..1-ish.
    if (brightnessBoost > 0.001)
    {
        const half stops = (half)(2.0 * saturate(brightnessBoost)); // 0..2 stops
        const half exposure = exp2(stops);                          // 1..4
        scene.rgb = Tonemap_Exp(scene.rgb * exposure);
    }

    // Slot-mask modulation
    if (!pillarbox && (slotmaskBlendMode > 0.5) && (slotMaskStrength > 0.001))
    {
        half3 slotmask = slMskTexture.Sample(SamplerLinearWrap, input.TexCoord * slotmaskSize / slotmaskScale).rgb;
        if (slotmaskBlendMode == 1) {
            // Multiply
            slotmask *= scene.rgb;
        }
        else if (slotmaskBlendMode == 2) {
            // Overlay
            slotmask = BlendOverlay((half3)scene.rgb, slotmask);
        }
        else if (slotmaskBlendMode == 3) {
            // Screen
            slotmask = BlendScreen((half3)scene.rgb, slotmask);
        }
        scene = lerp(scene, slotmask, (half)saturate(slotMaskStrength));
    }

    // Combine processed + blur, then optionally blend back with original
    const half t = (half)saturate(blendWithOriginal);
    o.color = (half3)saturate(lerp(scene, original, t));
    return o;
}

#endif
