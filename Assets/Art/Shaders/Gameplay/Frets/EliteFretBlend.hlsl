#ifndef YARG_ELITE_FRET_BLEND
#define YARG_ELITE_FRET_BLEND

void EliteFretBlend_float(float4 Drum, float3 Position, float4 Cymbal, float4 AxisX,
    float4 AxisZ, float Expansion, out float4 Out)
{
    Out = Drum;
    if (Cymbal.w == 0) return;

    float4 pos = float4(Position, 1);
    float x = dot(AxisX, pos) * sign(Cymbal.w);
    float z = dot(AxisZ, pos);
    float mode = abs(Cymbal.w);
    float side, softness;
    if (mode < 1.5)
    {
        // The middle rail occupies about 11% of the rectangular face depth.
        // Across the rail, blend left to right. At its two ends the vertical
        // strokes change color over a narrow, pixel-filtered contact.
        float alongBar = smoothstep(-0.32, 0.32, x);
        float seam = lerp(0.055, -0.055, alongBar);
        side = lerp(z - seam, x, Expansion);
        float atCenter = 1 - smoothstep(0, 0.38, abs(x));
        softness = lerp(0.014 + 0.085 * atCenter, 0.055, Expansion);
    }
    else
    {
        float resting = mode < 2.5 ? z : (z + x) * 0.5;
        side = lerp(resting, x, Expansion);
        softness = lerp(0.33, 0.055, Expansion);
    }
    softness = max(softness, fwidth(side) * 0.5);
    Out.rgb = lerp(Drum.rgb, Cymbal.rgb, smoothstep(-softness, softness, side));
}

void EliteFretBlend_half(half4 Drum, half3 Position, half4 Cymbal, half4 AxisX,
    half4 AxisZ, half Expansion, out half4 Out)
{
    float4 result;
    EliteFretBlend_float(Drum, Position, Cymbal, AxisX, AxisZ, Expansion, result);
    Out = result;
}
#endif
