#if __JETBRAINS_IDE__
#include "./../../../../../../../FlaxEngine/Source/Shaders/GBuffer.hlsl"
#include "./../../../../../../../FlaxEngine/Source/Shaders/Lighting/Lighting.hlsl"
#include "./../../../../../../../FlaxEngine/Source/Shaders/Lighting/LightingCommon.hlsl"
#include "./../../../../../../../FlaxEngine/Source/Shaders/MaterialCommon.hlsl"
#endif

#ifndef __INC_VL__
#define __INC_VL__

#include "./Flax/MaterialCommon.hlsl"
#include "./Flax/GBufferCommon.hlsl"

// acidicvoid.com
// Vertex Lighting Functions for Retro Gouraud Shading

// ---------------------------------------------------------------------------------------------------------------------
// Legacy: vertex lighting from engine lights (VL_*). Only compiled for materials with the engine's forward shading
// feature (Custom Lit shading model), which defines MAX_LOCAL_LIGHTS and the engine light data.
// TODO: remove once all PSX materials use PsxVertexLighting (plan step 9).
// ---------------------------------------------------------------------------------------------------------------------
#ifdef MAX_LOCAL_LIGHTS

#include "./Flax/Lighting/Lighting.hlsl"
#include "./Flax/Lighting/LightingCommon.hlsl"

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
    return lightColor * lightColor * NdotL * attenuation;
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
// DSL Strength = Directional, Sky, Local
float3 VL_GetAllLighting(float3 worldPos, float3 viewPos, float3 worldNormal, float3 dslStrength)
{
    GBufferSample gBuffer;
    gBuffer.Normal = worldNormal;
    gBuffer.Roughness = 1;
    gBuffer.Metalness = 0;
    gBuffer.Specular = 0;
    gBuffer.Color = float3(1, 1, 1);
    gBuffer.AO = 1.0; //float3(1, 1, 1);
    gBuffer.ViewPos = viewPos;
    gBuffer.WorldPos = worldPos;
    gBuffer.ShadingModel = MATERIAL_SHADING_MODEL;
    
    float3 lighting = float3(0,0,0);
    
    // Directional light
    LightData dirLight = GetDirectionalLight();
    lighting += (float4(VL_GetDirectionalLighting(dirLight, worldNormal), 0) * dslStrength.x).xyz;
    
    // Sky-light
    lighting += (float4(VL_GetSkyLighting(worldNormal), 0) * dslStrength.y).xyz;
        
    // Local lights
    lighting += (float4(VL_GetLocalLighting(worldPos, worldNormal), 0) * dslStrength.z).xyz;
    
    return lighting;
}

#endif // MAX_LOCAL_LIGHTS

// ---------------------------------------------------------------------------------------------------------------------
// Flax-PSX vertex lights (PsxVertexLight actors, packed by VertexLightManager).
// The light data texture is the material's first parameter (GPU Texture 'PsxLightData', generated name In1).
// Outside of scene render tasks (asset previews, thumbnails) it holds neutral data: no lights, ambient 1.
// Pass it from a Custom Code node, e.g.: Output1.xyz = PsxVertexLighting(In1, Input1.xyz, Input3.xyz);
//
// Layout (one row, one float4 per texel):
//   texel 0:          x = light count, yzw = ambient colour
//   light i, texel 1 + 4 * i + n:
//     E0: position.xyz, 1 / range (0 = no range limit)
//     E1: colour.rgb (premultiplied), type (1 = directional, 2 = point, 3 = spot, 4 = area)
//     E2: direction.xyz, falloff
//     E3: area: shape, halfSize.xy / spot: w = cos(angle / 2), area: w = roll
// ---------------------------------------------------------------------------------------------------------------------

#define PSX_LIGHT_DIRECTIONAL 1
#define PSX_LIGHT_POINT 2
#define PSX_LIGHT_SPOT 3
#define PSX_LIGHT_AREA 4
#define PSX_LIGHT_ELEMENTS 4

float4 PsxLoadLightElement(Texture2D lights, uint index)
{
    return lights.Load(int3(index, 0, 0));
}

// ---------------------------------------------------------------------------------------------------------------------
// Falloff curves: Robert Penner's easing equations (MIT), in the common closed forms (easings.net).
// Index = PsxLightFalloff value (0 = Linear, then In/Out/InOut for Sine, Quad, Cubic, Quart, Quint, Expo, Circ, Bounce).
// Back and Elastic are excluded on purpose (they overshoot below 0 and above 1).
// ---------------------------------------------------------------------------------------------------------------------

float PsxEaseOutBounce(float t)
{
    const float n1 = 7.5625;
    const float d1 = 2.75;
    if (t < 1.0 / d1)
        return n1 * t * t;
    if (t < 2.0 / d1)
    {
        t -= 1.5 / d1;
        return n1 * t * t + 0.75;
    }
    if (t < 2.5 / d1)
    {
        t -= 2.25 / d1;
        return n1 * t * t + 0.9375;
    }
    t -= 2.625 / d1;
    return n1 * t * t + 0.984375;
}

// Eases t (0..1) with the given curve; returns 0..1
float PsxEase(uint curve, float t)
{
    t = saturate(t);
    float u = 1.0 - t;      // For "Out" curves
    float a = 2.0 * t;      // For the first half of "InOut" curves
    float b = 2.0 - a;      // For the second half of "InOut" curves (= -2t + 2)
    bool firstHalf = t < 0.5;
    switch (curve)
    {
    // Sine
    case 1: return 1.0 - cos(t * PI * 0.5);
    case 2: return sin(t * PI * 0.5);
    case 3: return -(cos(PI * t) - 1.0) * 0.5;
    // Quad
    case 4: return t * t;
    case 5: return 1.0 - u * u;
    case 6: return firstHalf ? 2.0 * t * t : 1.0 - b * b * 0.5;
    // Cubic
    case 7: return t * t * t;
    case 8: return 1.0 - u * u * u;
    case 9: return firstHalf ? 4.0 * t * t * t : 1.0 - b * b * b * 0.5;
    // Quart
    case 10: return t * t * t * t;
    case 11: return 1.0 - u * u * u * u;
    case 12: return firstHalf ? 8.0 * t * t * t * t : 1.0 - b * b * b * b * 0.5;
    // Quint
    case 13: return t * t * t * t * t;
    case 14: return 1.0 - u * u * u * u * u;
    case 15: return firstHalf ? 16.0 * t * t * t * t * t : 1.0 - b * b * b * b * b * 0.5;
    // Expo
    case 16: return t <= 0.0 ? 0.0 : exp2(10.0 * t - 10.0);
    case 17: return t >= 1.0 ? 1.0 : 1.0 - exp2(-10.0 * t);
    case 18: return t <= 0.0 ? 0.0 : t >= 1.0 ? 1.0 : firstHalf ? exp2(20.0 * t - 10.0) * 0.5 : (2.0 - exp2(-20.0 * t + 10.0)) * 0.5;
    // Circ
    case 19: return 1.0 - sqrt(saturate(1.0 - t * t));
    case 20: return sqrt(saturate(1.0 - u * u));
    case 21: return firstHalf ? (1.0 - sqrt(saturate(1.0 - a * a))) * 0.5 : (sqrt(saturate(1.0 - b * b)) + 1.0) * 0.5;
    // Bounce
    case 22: return 1.0 - PsxEaseOutBounce(u);
    case 23: return PsxEaseOutBounce(t);
    case 24: return firstHalf ? (1.0 - PsxEaseOutBounce(1.0 - a)) * 0.5 : (1.0 + PsxEaseOutBounce(a - 1.0)) * 0.5;
    // Linear (0) and unknown values
    default: return t;
    }
}

// Light attenuation for a normalized distance or cone position t (0 = full brightness, 1 = edge): 1 - ease(t)
float PsxFalloff(uint curve, float t)
{
    return saturate(1.0 - PsxEase(curve, t));
}

// Returns ambient + the sum of all vertex lights for a vertex (world space position and normal).
// Returns 1 (unlit) if no light data texture is bound (e.g. a material that isn't registered yet).
float3 PsxVertexLighting(Texture2D lights, float3 worldPos, float3 worldNormal)
{
    // Unbound texture: width 0 on DX11; other backends may bind a tiny placeholder texture instead.
    // The real texture always has room for at least 32 lights, so anything that can't hold one light isn't ours.
    uint width, height;
    lights.GetDimensions(width, height);
    if (width < 1 + PSX_LIGHT_ELEMENTS)
        return float3(1, 1, 1);

    float4 header = PsxLoadLightElement(lights, 0);
    float3 lighting = header.yzw;

    // Never read past the texture, even if the header is wrong
    uint count = min((uint)header.x, (width - 1) / PSX_LIGHT_ELEMENTS);

    float3 N = normalize(worldNormal);

    LOOP
    for (uint i = 0; i < count; i++)
    {
        uint first = 1 + i * PSX_LIGHT_ELEMENTS;

        // E0 only: range test before loading the rest (directional lights have invRange = 0 and always pass)
        float4 e0 = PsxLoadLightElement(lights, first);
        float3 toLight = e0.xyz - worldPos;
        float distSq = dot(toLight, toLight);
        float invRange = e0.w;
        if (distSq * invRange * invRange >= 1.0)
            continue;

        float4 e1 = PsxLoadLightElement(lights, first + 1);
        uint type = (uint)(e1.w + 0.5);
        float4 e2 = PsxLoadLightElement(lights, first + 2);

        if (type == PSX_LIGHT_DIRECTIONAL)
        {
            // E2.xyz = direction the light shines towards
            float NdotL = saturate(dot(N, -e2.xyz));
            lighting += e1.rgb * NdotL;
        }
        else if (type == PSX_LIGHT_POINT)
        {
            // E2.w = distance falloff curve
            float dist = sqrt(distSq);
            float3 L = dist > 0.0001 ? toLight / dist : N;
            float NdotL = saturate(dot(N, L));
            float attenuation = PsxFalloff((uint)(e2.w + 0.5), dist * invRange);
            lighting += e1.rgb * (NdotL * attenuation);
        }
        else if (type == PSX_LIGHT_SPOT)
        {
            // E2.xyz = spot direction, E2.w = distance falloff + cone falloff * 32, E3.w = cos(angle / 2)
            float dist = sqrt(distSq);
            float3 L = dist > 0.0001 ? toLight / dist : -e2.xyz;
            float cosToVertex = dot(-L, e2.xyz);
            float cosHalfAngle = PsxLoadLightElement(lights, first + 3).w;
            if (cosToVertex <= cosHalfAngle)
                continue;

            uint falloffs = (uint)(e2.w + 0.5);
            uint distanceCurve = falloffs % 32;
            uint coneCurve = falloffs / 32;

            // Cone position linear in angle: 0 on the axis, 1 at the cone edge
            float conePos = acos(saturate(cosToVertex)) / max(acos(saturate(cosHalfAngle)), 0.0001);
            float NdotL = saturate(dot(N, L));
            float attenuation = PsxFalloff(distanceCurve, dist * invRange) * PsxFalloff(coneCurve, conePos);
            lighting += e1.rgb * (NdotL * attenuation);
        }
        // TODO: area (step 7)
    }

    return lighting;
}

#endif
