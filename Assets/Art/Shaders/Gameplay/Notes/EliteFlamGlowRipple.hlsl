// pattern: Functional Core
#ifndef YARG_ELITE_FLAM_GLOW_RIPPLE
#define YARG_ELITE_FLAM_GLOW_RIPPLE

// Gameplay supplies 1 + the visual quarter-note phase. Zero leaves editor previews static.
float _YargFlamGlowPhase;

void YargFlamGlowRipple_float(float3 positionOS, float4 vertexTint, float useVertexTint, out float4 tint)
{
    tint = lerp(1.0, vertexTint, useVertexTint);
    if (useVertexTint <= 0.0 || _YargFlamGlowPhase <= 0.0)
        return;

    // Start the fade on the quarter note and the expansion on the upbeat.
    float halfBeatPhase = 2.0 * frac(_YargFlamGlowPhase + 0.5);
    float glow;
    if (halfBeatPhase < 1.0)
    {
        // The center starts first, and the outermost point finishes on the quarter note.
        float distanceFromCenter = saturate(abs(positionOS.x) / 0.244);
        float start = distanceFromCenter * 0.7;
        glow = smoothstep(start, start + 0.3, halfBeatPhase);
    }
    else
    {
        // Every point fades to its authored tint together by the upbeat.
        glow = 1.0 - smoothstep(1.0, 2.0, halfBeatPhase);
    }
    tint.rgb = lerp(tint.rgb, 1.0, glow);
}

void YargFlamGlowRipple_half(half3 positionOS, half4 vertexTint, half useVertexTint, out half4 tint)
{
    float4 result;
    YargFlamGlowRipple_float(positionOS, vertexTint, useVertexTint, result);
    tint = result;
}

#endif
