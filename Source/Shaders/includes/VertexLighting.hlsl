#if __JETBRAINS_IDE__
#include "./../../../../../../../FlaxEngine/Source/Shaders/GBuffer.hlsl"
#include "./../../../../../../../FlaxEngine/Source/Shaders/Lighting.hlsl"
#include "./../../../../../../../FlaxEngine/Source/Shaders/LightingCommon.hlsl"
#include "./../../../../../../../FlaxEngine/Source/Shaders/MaterialCommon.hlsl"
#endif

#ifndef __INC_VL__
#define __INC_VL__

#include "./Flax/Lighting.hlsl"
#include "./Flax/LightingCommon.hlsl"
#include "./Flax/MaterialCommon.hlsl"
#include "./Flax/GBufferCommon.hlsl"

// acidicvoid.com
// Vertex Lighting Functions for Retro Gouraud Shading

// Helper function to calculate attenuation for point/spotlights
float CalculateLightAttenuation(LightData light, float3 worldPos)
{
    float3 toLight = light.Position - worldPos;
    float distSq = dot(toLight, toLight);
    float dist = sqrt(distSq);
    
    // Check if pixel is within light radius
    if (dist > light.Radius)
        return 0.0;
    
    // Distance attenuation
    float attenuation;
    if (light.InverseSquared > 0.0)
    {
        // Inverse square falloff with smooth radius cutoff
        attenuation = saturate(1.0 - pow(dist * light.RadiusInv, 4.0));
        attenuation *= attenuation;
        attenuation /= (distSq + 1.0);
    }
    else
    {
        // Linear falloff
        attenuation = saturate(1.0 - dist * light.RadiusInv);
        attenuation = pow(attenuation, light.FalloffExponent);
    }
    
    return attenuation;
}

// Helper function to calculate spot light cone attenuation
float CalculateSpotAttenuation(LightData light, float3 worldPos)
{
    float3 L = normalize(light.Position - worldPos);
    float cosAngle = dot(L, -light.Direction);
    
    // light.SpotAngles.x = cos(outerAngle), light.SpotAngles.y = cos(innerAngle)
    float spotAttenuation = saturate((cosAngle - light.SpotAngles.x) / (light.SpotAngles.y - light.SpotAngles.x));
    return spotAttenuation * spotAttenuation;
}

// Calculate simple Lambertian diffuse lighting
float3 CalculateDiffuseLighting(float3 lightColor, float3 L, float3 N, float attenuation)
{
    float NdotL = saturate(dot(N, L));
    return lightColor * NdotL * attenuation;
}

// Directional Light (Sun)
float3 VL_GetDirectionalLighting(LightData dirLight, float3 worldNormal)
{
    // Directional light has no attenuation
    float3 L = dirLight.Direction;
    float NdotL = saturate(dot(worldNormal, L));
    
    return dirLight.Color * NdotL;
}

// Skylight (Ambient/Hemisphere)
float3 VL_GetSkyLighting(float3 worldNormal)
{
    LightData skyLight = GetSkyLight();
    
    // Simple hemisphere lighting
    // Skylights typically provide ambient illumination
    float skyFactor = saturate(dot(worldNormal, float3(0, 1, 0)) * 0.5 + 0.5);
    
    return skyLight.Color * skyFactor;
}

// Local Lights (Point and Spot)
float3 VL_GetLocalLighting(float3 worldPos, float3 worldNormal)
{
    float3 totalLighting = float3(0, 0, 0);
    
    LOOP
    for (uint i = 0; i < GetLocalLightsCount(); i++)
    {
        const LightData localLight = GetLocalLight(i);
        
        // Calculate light direction and distance
        float3 toLight = localLight.Position - worldPos;
        float dist = length(toLight);
        float3 L = toLight / dist; // Normalized
        
        // Calculate basic attenuation
        float attenuation = CalculateLightAttenuation(localLight, worldPos);
        
        if (attenuation <= 0.0)
            continue;
        
        // Check if it's a spotlight (SpotAngles.x != SpotAngles.y means spot light)
        if (abs(localLight.SpotAngles.x - localLight.SpotAngles.y) > 0.001)
        {
            float spotAtten = CalculateSpotAttenuation(localLight, worldPos);
            attenuation *= spotAtten;
            
            if (attenuation <= 0.0)
                continue;
        }
        
        // Calculate diffuse lighting
        float3 lighting = CalculateDiffuseLighting(localLight.Color, L, worldNormal, attenuation);

        totalLighting += lighting;
    }
    
    return totalLighting;
}

// Combined function to get all vertex lighting at once
float3 VL_GetAllLighting(float3 worldPos, float3 viewPos, float3 worldNormal, out half3 directionalLighting, out half3 skyLighting, out half3 localLighting)
{
    GBufferSample gBuffer;
    gBuffer.Normal = worldNormal;
    gBuffer.Roughness = 1;
    gBuffer.Metalness = 0;
    gBuffer.Specular = 0;
    gBuffer.Color = float3(1, 1, 1);
    gBuffer.AO = float3(1, 1, 1);
    gBuffer.ViewPos = viewPos;
    gBuffer.WorldPos = worldPos;
    gBuffer.ShadingModel = MATERIAL_SHADING_MODEL;
    
    float3 lighting = float3(0, 0, 0);
    
    // Directional light
    LightData dirLight = GetDirectionalLight();
    directionalLighting = VL_GetDirectionalLighting(dirLight, worldNormal);
    lighting += directionalLighting;
    
    // Sky-light
    skyLighting = VL_GetSkyLighting(worldNormal);
    lighting += skyLighting;
    
    // Local lights
    localLighting = VL_GetLocalLighting(worldPos, worldNormal);
    lighting += localLighting;
    
    return lighting;
}

#endif