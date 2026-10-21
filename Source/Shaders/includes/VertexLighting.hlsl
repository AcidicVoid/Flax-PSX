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
//     E0: position.xyz, 1 / range (0 = no range limit; area: 1 / (range + shape radius))
//     E1: colour.rgb (premultiplied), type (1 = directional, 2 = point, 3 = spot, 4 = area)
//     E2: direction.xyz, falloff (0 = linear, 1 = quadratic; spot: distance + cone * 2)
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

// Light attenuation for a normalized distance or cone position t (0 = full brightness, 1 = edge).
// curve: 0 = linear (1 - t), 1 = quadratic ((1 - t)^2), see PsxLightFalloff
float PsxFalloff(uint curve, float t)
{
    float u = 1.0 - saturate(t);
    return curve == 1 ? u * u : u;
}

// Orthonormal basis around a normal. Must match PsxLightMath.BuildBasis (C#): same reference vectors, same cross product order.
void PsxBuildBasis(float3 n, out float3 right, out float3 up)
{
    float3 reference = abs(n.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    right = normalize(cross(reference, n));
    up = cross(n, right);
}

#define PSX_AREA_SHAPE_CIRCLE 2

// Closest point to p on an area light's shape (rectangle or circle in the plane through center with normal n).
// e3 = shape, halfSize.x (circle: radius), halfSize.y, roll (see PsxLightMath.ComputeRoll)
float3 PsxClosestPointOnArea(float3 center, float3 n, float4 e3, float3 p)
{
    float3 basisRight, basisUp;
    PsxBuildBasis(n, basisRight, basisUp);
    float sinRoll, cosRoll;
    sincos(e3.w, sinRoll, cosRoll);
    float3 right = cosRoll * basisRight + sinRoll * basisUp;
    float3 up = cross(n, right);

    float3 d = p - center;
    float2 local = float2(dot(d, right), dot(d, up));
    if ((uint)(e3.x + 0.5) == PSX_AREA_SHAPE_CIRCLE)
    {
        float len = length(local);
        if (len > e3.y)
            local *= e3.y / len;
    }
    else
    {
        local = clamp(local, -e3.yz, e3.yz);
    }
    return center + right * local.x + up * local.y;
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

        // Each type sets the light direction L, a type specific factor and the distance falloff input;
        // the falloff curve is then evaluated once for all types
        float3 L = N;
        float factor = 1.0;
        uint curve = 0;
        float t = 0.0;

        if (type == PSX_LIGHT_DIRECTIONAL)
        {
            // E2.xyz = direction the light shines towards; no distance falloff
            L = -e2.xyz;
        }
        else if (type == PSX_LIGHT_POINT || type == PSX_LIGHT_SPOT || type == PSX_LIGHT_AREA)
        {
            float3 lightPos = e0.xyz;
            float lightInvRange = invRange;
            float4 e3 = float4(0, 0, 0, 0);

            if (type == PSX_LIGHT_AREA)
            {
                // One-sided: only vertices in front of the shape (E2.xyz = shape normal) receive light
                if (dot(worldPos - e0.xyz, e2.xyz) <= 0.0)
                    continue;

                // E0.w covers range + shape radius; the falloff is measured from the closest point on the shape
                e3 = PsxLoadLightElement(lights, first + 3);
                float radius = (uint)(e3.x + 0.5) == PSX_AREA_SHAPE_CIRCLE ? e3.y : length(e3.yz);
                lightInvRange = 1.0 / max(1.0 / invRange - radius, 1.0);
                lightPos = PsxClosestPointOnArea(e0.xyz, e2.xyz, e3, worldPos);
            }

            float3 toLightPos = lightPos - worldPos;
            float dist = length(toLightPos);
            t = dist * lightInvRange;
            if (t >= 1.0)
                continue;
            L = dist > 0.0001 ? toLightPos / dist : (type == PSX_LIGHT_POINT ? N : -e2.xyz);
            curve = (uint)(e2.w + 0.5);

            if (type == PSX_LIGHT_SPOT)
            {
                // E2.w = distance falloff + cone falloff * 2, E3.w = cos(angle / 2)
                e3 = PsxLoadLightElement(lights, first + 3);
                float cosToVertex = dot(-L, e2.xyz);
                if (cosToVertex <= e3.w)
                    continue;

                // Cone position linear in angle: 0 on the axis, 1 at the cone edge
                float conePos = acos(saturate(cosToVertex)) / max(acos(saturate(e3.w)), 0.0001);
                factor = PsxFalloff(curve / 2, conePos);
                curve = curve % 2;
            }
            else if (type == PSX_LIGHT_AREA)
            {
                // Emitter facing: light leaves the shape along its normal
                factor = saturate(dot(e2.xyz, -L));
            }
        }
        else
        {
            continue;
        }

        float NdotL = saturate(dot(N, L));
        lighting += e1.rgb * (NdotL * factor * PsxFalloff(curve, t));
    }

    return lighting;
}

#endif
