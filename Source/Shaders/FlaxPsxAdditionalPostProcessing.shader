// www.acidicvoid.com

#ifndef __FLAX_PSX_ADDITIONAL_POST_PROCESSING__
#define __FLAX_PSX_ADDITIONAL_POST_PROCESSING__

struct frag_out
{
  float4 color : SV_Target;
};

#include "./Flax/Common.hlsl"

META_CB_BEGIN(0, Data)
    float2 resolution;
    float2 texelSize;
    float  blendWithOriginal;
META_CB_END

Texture2D sceneTexture : register(t0);

META_PS(true, FEATURE_LEVEL_ES3)
frag_out PS_FlaxPsxAdditionalPostProcessing(Quad_VS2PS input)
{
    frag_out o;
    half4 scene = sceneTexture.Sample(SamplerPointClamp, input.TexCoord); 
    o.color = lerp(half4(1, scene.g, scene.b, 1), scene, blendWithOriginal);
    return o; 
}
#endif