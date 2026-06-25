#ifndef GEM_HIGHWAY_GLOW_INCLUDED
#define GEM_HIGHWAY_GLOW_INCLUDED

// Shader-stamped gem highway glow.
// See docs/design-plans/2026-06-25-shader-stamped-gem-highway-glow.md.
//
// This helper backs a single Shader Graph Custom Function node in
// Track.shadergraph. Gameplay code (TrackMaterial.SetGemGlowSources) uploads per
// highway material instance each frame:
//
//   _GemGlowPositions[i]  : x = trackX, y = trackZ, z = width, w = length
//   _GemGlowColors[i]     : rgb = resolved note color, a = proximity intensity
//   _GemGlowObjectToTrack : highway-mesh object space -> note (pool-local) space
//
// All bound per-material (never via Shader.SetGlobal*), so each highway reads
// only its own notes.
//
// Coordinate note: the highway mesh's object space does NOT share the note
// transform frame (its object Z is large/positive across the whole mesh). Notes
// are children of the note Pool, so their positions live in pool-local space
// (strikeline at z = -2, lane width in TRACK_WIDTH = 2 units). We convert each
// highway pixel from object space into that pool-local frame with the uploaded
// matrix, so the glow lands under the gem and the gameplay-side constants
// (width/length/band) stay meaningful.

#define GEM_GLOW_MAX_SOURCES 16

// Bound by name from C# (material.SetVectorArray / material.SetMatrix). Shader
// Graph's blackboard can't expose arrays or this matrix, but Unity still binds
// them by property name on the material instance.
float4   _GemGlowPositions[GEM_GLOW_MAX_SOURCES];
float4   _GemGlowColors[GEM_GLOW_MAX_SOURCES];
float4x4 _GemGlowObjectToTrack;

// Strikeline band gate in pool-local Z. Pixels outside this window skip the
// source loop entirely — this matters far more than the source cap, because a
// fixed loop over every covered pixel is the real cost on four highways. Keep
// roughly aligned with the gameplay collection window in TrackPlayer
// (GEM_GLOW_MIN_Z / GEM_GLOW_MAX_Z), widened slightly for lobe overhang.
#define GEM_GLOW_BAND_MIN -4.0
#define GEM_GLOW_BAND_MAX  7.0

// PositionOS is the highway pixel in the mesh's OBJECT space (Shader Graph
// Position node, Object space). It is converted to pool-local track space below.
void GemHighwayGlow_float(float3 PositionOS, float Count, float Intensity, out float3 GlowColor)
{
    GlowColor = float3(0.0, 0.0, 0.0);

    // Cheap zero path: nothing to do with no sources or no intensity.
    if (Intensity <= 0.0 || Count <= 0.0)
    {
        return;
    }

    // Object space -> note (pool-local) track space.
    float3 trackPos = mul(_GemGlowObjectToTrack, float4(PositionOS, 1.0)).xyz;
    float localX = trackPos.x;
    float localZ = trackPos.z;

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
