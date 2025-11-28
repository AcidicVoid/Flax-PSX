#ifndef __FLAX_PSX__FOG__
#define __FLAX_PSX__FOG__

// SH1 style fog is characterized by extreme density and a sharp falloff,
// giving a claustrophobic, volumetric feel. This is implemented using Exponential Fog.

// Calculates the amount of fog to apply (0.0 = none, 1.0 = fully fogged).
// depth01: Normalized linear depth (0.0 at near plane, 1.0 at far plane)
// density: Controls the rate of falloff (higher value = denser fog)
// fogBoost: A factor to push the fog closer to the camera. If 0, it behaves normally.
float SH1Fog(float depth01, float density, float fogBoost)
{
    // Determine the distance factor: (1.0 + fogBoost) ensures 
    // a factor of 1.0 when fogBoost is 0, maintaining original behavior.
    float distanceFactor = 1.0 + fogBoost;

    // Adjust the depth: Multiplying depth by a factor > 1 makes the 
    // density curve reach full fog faster, drawing the fog line closer.
    float boostedDepth = depth01 * distanceFactor;

    // Exponential Fog: f = e^-(boostedDepth * density)
    // Calculate the transmission factor (how much light gets through)
    float transmission = exp(-boostedDepth * density);
    
    // The fog amount is the inverse of transmission (1 - transmission)
    return clamp(1.0 - transmission, 0.0, 1.0);
}
#endif