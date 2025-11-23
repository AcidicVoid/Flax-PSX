#ifndef __LIQUID__
#define __LIQUID__

half2 q1styleWater(half2 uv, half time, half scale, half amplitude) {
    half t1 = time * 0.8;
    half t2 = time * 1.3;
    half s1 = 6.0;
    half s2 = 12.0;
    half a = 0.03;
    uv.x += sin(uv.y * s1 + t1) * a;
    uv.y += sin(uv.x * s2 - t2) * a;
    return uv;
}

#endif