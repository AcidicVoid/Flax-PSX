#if __JETBRAINS_IDE__
#include "./../../../../../../../FlaxEngine/Source/Shaders/GBuffer.hlsl"
#include "./../../../../../../../FlaxEngine/Source/Shaders/Lighting.hlsl"
#include "./../../../../../../../FlaxEngine/Source/Shaders/LightingCommon.hlsl"
#include "./../../../../../../../FlaxEngine/Source/Shaders/MaterialCommon.hlsl"
#endif

#ifndef __PSX_MATERIAL__
#define __PSX_MATERIAL__

#include "./Flax/MaterialCommon.hlsl"
#include "./FlaxPSX/includes/psx.hlsl"
#include "./FlaxPSX/includes/VertexLighting.hlsl"

float4 V_GetAffineUV0W(float2 TexCoords, float3 WorldPosition)
{
    float w = mul(float4(WorldPosition, 1), ViewProjectionMatrix).w;
    float2 uv = TexCoords * w;
    return float4(uv, 0.0, w);
}

// AffineUV0W calculated by V_GetAffineUV0W in vertex stage
float2 F_GetAffineUVs(float4 AffineUV0W)
{
    return AffineUV0W.xy / AffineUV0W.z;
}

// Called before a "Interpolate VS To PS" node
// Handles vertex-stage operations
float4 Vertex_SurfaceOpaque(float4 UVs, float3 WorldPosition, out float4 AffineUV0W)
{
    float2 texCoords    = UVs.xy;
    // Calculate vertex stage part of Affine UVs, pass to output AffineUV0W
    AffineUV0W = V_GetAffineUV0W(texCoords, WorldPosition);
    return float4(0,0,0,0);
}

// Input 0 - Color:
// * xyzw: RGBA values of surface color
//
// Input 1 - VertexColor:
// * xyz: Vertex color
// * w:   Ambient Light Strength for Vertex Lighting
//
// Input 2 - VertexColorInfo:
// * x: reserved (null) TODO: Implement Vertex Color Threshold to ignore pure black or white
// * y: reserved (null) TODO: Implement Vertex Color Threshold inversion (swap black/white targeting)
// * z: Use Additive Vertex Color (int) 
// * w: Vertex Color Strength (float)
//
// Input 3 - Vertex Lighting
// * xyz:  Vertex Lighting RGB Colors
// * w:    Vertex Lighting Clamp Max

float4 Frag_SurfaceOpaque(float4 Color, float4 VertexColor, float4 VertexColorInfo, float4 VertexLighting)
{
    // Cache color
    float4 c = Color;
    
    // Vertex Lighting
    float3 vertexColor = VertexColor.rgb;
    float  ambientLightStrength = VertexColor.w;

    c.xyz *= clamp(VertexLighting.xyz, 0, VertexLighting.w);
    
    // Vertex Color
    int useAdditiveVertexColors = VertexColorInfo.z;
    int vertexColorStrength = VertexColorInfo.w;
    if (useAdditiveVertexColors > 0.5)
        c += float4(lerp(float3(0,0,0), vertexColor, vertexColorStrength), 0);
    else
        c *= float4(lerp(float3(1,1,1), vertexColor, vertexColorStrength), 1);
    
    return lerp(c, c + Color, ambientLightStrength);
}

#endif