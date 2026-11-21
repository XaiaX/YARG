// pattern: Functional Core
#ifndef YARG_HI_HAT_SHIMMER
#define YARG_HI_HAT_SHIMMER

// Independent local flashes, reconstructed solely from chart UVs and
// explicit song time. No shared frame clock regenerates the whole field.
float YargHiHatTransientShimmer(float2 uv, float time)
{
    float2 grid = uv * float2(18, 48);
    float2 cell = floor(grid);
    float3 seed;
    Hash_Tchou_2_3_float(cell, seed);
    float rate = lerp(2.1, 3.4, seed.x);
    float clock = time * rate + seed.y * 11;
    float age = frac(clock) / rate;
    float3 event;
    Hash_Tchou_3_3_float(float3(cell, floor(clock)), event);
    float lifetime = lerp(.06, .105, event.z);
    float flash = smoothstep(0, .008, age) * (1 - smoothstep(.018, lifetime, age));
    float2 center = .3 + event.xy * .4;
    float2 radius = float2(.15, .065);
    // Derivatives of unwrapped coordinates avoid seams at cell edges.
    // Fade under minification instead of producing subpixel flicker.
    float2 footprint = max(fwidth(grid), .0001);
    float2 aa = min(footprint * .5, radius * .5);
    float2 shape = 1 - smoothstep(radius * .25, radius + aa, abs(frac(grid) - center));
    float energy = saturate(radius.x / footprint.x) * saturate(radius.y / footprint.y);
    return shape.x * shape.y * energy * flash;
}

#endif
