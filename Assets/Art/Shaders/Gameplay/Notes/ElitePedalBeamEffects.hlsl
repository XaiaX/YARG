// pattern: Functional Core
#ifndef YARG_ELITE_PEDAL_BEAM_EFFECTS
#define YARG_ELITE_PEDAL_BEAM_EFFECTS

#pragma target 3.5
#include "Assets/Art/Shaders/Gameplay/HiHatShimmer.hlsl"

float _YargElitePedalBeamEffect;

// Gem Flow uses the motion rate of the Rectangular gem material.
void YargPedalNoiseSpeed_float(float original, out float speed)
{
    speed = _YargElitePedalBeamEffect == 1 ? original * 2 : original;
}

void YargPedalNoiseSpeed_half(half original, out half speed)
{
    float result;
    YargPedalNoiseSpeed_float(original, result);
    speed = result;
}

void YargPedalBeamFinish_float(float3 color, float3 emission, float4 noise,
    float3 positionOS, float time, out float3 beamColor, out float3 beamEmission)
{
    beamColor = color;
    beamEmission = emission;
    if (_YargElitePedalBeamEffect == 1)
    {
        float phase = positionOS.x * 23 + positionOS.y * 31 - time * 5 + noise.r * 2;
        float flow = smoothstep(-.8, .8, sin(phase));
        beamColor *= lerp(.46, 1, flow);
        beamEmission *= lerp(.52, 1, flow);
    }
    else if (_YargElitePedalBeamEffect == 2)
    {
        // Position is continuous across the modular wall, even where its UVs repeat.
        float2 uv = float2(positionOS.x * .48 + .5, positionOS.y * 2.5);
        float sparkle = YargHiHatTransientShimmer(uv, time);
        float gain = .62 + noise.r * .18;
        beamColor *= gain + sparkle * .45;
        beamEmission *= gain + sparkle * .6;
    }
}

void YargPedalBeamFinish_half(half3 color, half3 emission, half4 noise,
    half3 positionOS, half time, out half3 beamColor, out half3 beamEmission)
{
    float3 result, resultEmission;
    YargPedalBeamFinish_float(color, emission, noise, positionOS, time, result, resultEmission);
    beamColor = result;
    beamEmission = resultEmission;
}

#endif
