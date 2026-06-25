#ifndef GEM_HIGHWAY_GLOW_INCLUDED
#define GEM_HIGHWAY_GLOW_INCLUDED

// Shader-stamped gem highway glow.
// See docs/design-plans/2026-06-25-shader-stamped-gem-highway-glow.md.
//
// This helper backs a single Shader Graph Custom Function node in
// Track.shadergraph. Gameplay code (TrackMaterial.SetGemGlowSources) uploads two
// fixed-size Vector4 arrays per highway material instance each frame:
//
//   _GemGlowPositions[i] : x = localX, y = localZ, z = width, w = length
//   _GemGlowColors[i]    : rgb = resolved note color, a = proximity intensity
//
// The arrays are bound per-material (never via Shader.SetGlobalVectorArray), so
// each highway reads only its own notes. The Custom Function node should feed in
// the object-space Position (x/z) of the highway pixel plus the scalar count and
// intensity, and add the returned color to the highway emission/output.

#define GEM_GLOW_MAX_SOURCES 16

// Bound by name from C# via material.SetVectorArray(...). Shader Graph's
// blackboard cannot expose array properties cleanly, but Unity still binds these
// by property name when the material sets them.
float4 _GemGlowPositions[GEM_GLOW_MAX_SOURCES];
float4 _GemGlowColors[GEM_GLOW_MAX_SOURCES];

// Strikeline band gate. Pixels outside this highway-local Z window skip the
// source loop entirely — this matters far more than the source cap, because a
// fixed loop over every covered pixel is the real cost on four highways.
// Keep these aligned with the gameplay collection window in TrackPlayer
// (GEM_GLOW_MIN_Z / GEM_GLOW_MAX_Z), widened slightly for lobe overhang.
#define GEM_GLOW_BAND_MIN -4.0
#define GEM_GLOW_BAND_MAX  7.0

void GemHighwayGlow_float(float3 PositionOS, float Count, float Intensity, out float3 GlowColor)
{
    GlowColor = float3(0.0, 0.0, 0.0);

    // Cheap zero path: nothing to do with no sources or no intensity.
    if (Intensity <= 0.0 || Count <= 0.0)
    {
        return;
    }

    float localX = PositionOS.x;
    float localZ = PositionOS.z;

    // Spatial early-out before touching the source array.
    if (localZ < GEM_GLOW_BAND_MIN || localZ > GEM_GLOW_BAND_MAX)
    {
        return;
    }

    int count = (int) min(Count, (float) GEM_GLOW_MAX_SOURCES);

    [loop]
    for (int i = 0; i < count; i++)
    {
        float4 p = _GemGlowPositions[i];
        float4 c = _GemGlowColors[i];

        float width  = max(p.z, 1e-3);
        float length = max(p.w, 1e-3);

        // Across-lane falloff (symmetric).
        float dx = (localX - p.x) / width;
        float across = saturate(1.0 - dx * dx);

        // Forward falloff toward the strikeline (asymmetric): a short radius behind
        // the gem (further down the track, larger Z) and a longer smear ahead of it
        // toward the camera / strikeline (smaller Z).
        float dz = localZ - p.y;
        float behindScale = 0.45;          // tight behind the gem
        float scale = (dz > 0.0) ? behindScale : 1.0;
        float along = saturate(1.0 - (dz / (length * scale)) * (dz / (length * scale)));

        // Sharpen the lobe so adjacent lanes don't wash together.
        float falloff = across * along;
        falloff *= falloff;

        GlowColor += c.rgb * (falloff * c.a);
    }

    GlowColor *= Intensity;
}

// Half-precision variant for graphs compiled at half precision.
void GemHighwayGlow_half(float3 PositionOS, float Count, float Intensity, out half3 GlowColor)
{
    float3 result;
    GemHighwayGlow_float(PositionOS, Count, Intensity, result);
    GlowColor = (half3) result;
}

#endif // GEM_HIGHWAY_GLOW_INCLUDED
