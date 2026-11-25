// pattern: Functional Core
#ifndef YARG_ELITE_FLAM_GLOW_RIPPLE
#define YARG_ELITE_FLAM_GLOW_RIPPLE

// Gameplay and preview wrap at eight quarter notes, the longest alternating bell cycle.
// The gameplay clock adds one so zero can mark an inactive clock.
float _YargFlamGlowPhase;
float4 _YargCymbalFlamGlowAdjustments;

float YargHatPulsePhase(float rate, float previewBeatRate)
{
    float beat = previewBeatRate > 0.0 ? 1.0 + fmod(_Time.y * previewBeatRate, 8.0) : _YargFlamGlowPhase;
    return beat > 0.0 ? frac((beat - 1.0) * rate) : -1.0;
}

float YargWholeSurfaceGlow(float phase)
{
    return smoothstep(0.0, 1.0, abs(2.0 * frac(phase) - 1.0));
}

void YargCymbalFlamPulse_float(float3 positionOS, float4 pulse, float3 surface, float3 emission,
    out float3 pulsedSurface, out float3 pulsedEmission)
{
    float gain = 1.0;
    if (pulse.x > 0.0)
    {
        float phase = YargHatPulsePhase(pulse.x, pulse.z);
        float peak = 1.0 + _YargCymbalFlamGlowAdjustments.y;
        float floorGain = min(peak, 0.35 * (1.0 + _YargCymbalFlamGlowAdjustments.x));
        float offset = positionOS.x * pulse.y > 0.0 ? 0.5 : 0.0;
        gain = lerp(floorGain, peak, phase < 0.0 ? 1.0 : YargWholeSurfaceGlow(phase + offset));
    }
    pulsedSurface = surface * gain;
    pulsedEmission = emission * gain;
}

void YargCymbalFlamPulse_half(half3 positionOS, half4 pulse, half3 surface, half3 emission,
    out half3 pulsedSurface, out half3 pulsedEmission)
{
    float3 baseResult, emissionResult;
    YargCymbalFlamPulse_float(positionOS, pulse, surface, emission, baseResult, emissionResult);
    pulsedSurface = baseResult;
    pulsedEmission = emissionResult;
}

float YargQuarterNoteGlow(float distanceFromCenter, float beatPhase)
{
    // Start the fade on the quarter note and the expansion on the upbeat.
    float halfBeatPhase = 2.0 * frac(beatPhase + 0.5);
    if (halfBeatPhase < 1.0)
    {
        // The center starts first, and the outermost point finishes on the quarter note.
        float start = saturate(distanceFromCenter) * 0.7;
        return smoothstep(start, start + 0.3, halfBeatPhase);
    }
    return 1.0 - smoothstep(1.0, 2.0, halfBeatPhase);
}

void YargFlamGlowRipple_float(float3 positionOS, float4 vertexTint, float useVertexTint, out float4 tint)
{
    tint = lerp(1.0, vertexTint, useVertexTint);
    if (useVertexTint <= 0.0 || _YargFlamGlowPhase <= 0.0)
        return;

    float glow = YargQuarterNoteGlow(abs(positionOS.x) / 0.244, _YargFlamGlowPhase);
    tint.rgb = lerp(tint.rgb, 1.0, glow);
}

void YargFlamGlowRipple_half(half3 positionOS, half4 vertexTint, half useVertexTint, out half4 tint)
{
    float4 result;
    YargFlamGlowRipple_float(positionOS, vertexTint, useVertexTint, result);
    tint = result;
}

#endif
