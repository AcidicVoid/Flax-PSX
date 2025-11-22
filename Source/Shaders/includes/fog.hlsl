#ifndef __FLAX_PSX__FOG__
#define __FLAX_PSX__FOG__

float FogFalloffOutBack(half c1, half c2, half depth) {
    return saturate(1 + c2 * pow(depth - 1, 3) + c1 * pow(depth - 1, 2));
}

float FogFalloffOutQuint(half depth) {
    return saturate(1 - pow(1 - depth, 5));
}

#endif