#ifndef GEM_HIGHWAY_GLOW_INCLUDED
#define GEM_HIGHWAY_GLOW_INCLUDED

// Shader-stamped gem highway glow.
// See docs/design-plans/2026-06-25-shader-stamped-gem-highway-glow.md.
//
// This helper backs a single Shader Graph Custom Function node in
// Track.shadergraph. Gameplay code (TrackMaterial.SetGemGlowSources) uploads per
// highway material instance each frame:
//
//   _GemGlowPositions[i] : x = objX, y = objZ, z = width, w = length  (all in
//                          highway-mesh OBJECT space; gameplay converts from the
//                          note pool-local frame on the CPU)
//   _GemGlowColors[i]    : rgb = resolved note color, a = proximity intensity
//
// Both arrays are bound per-material (material.SetVectorArray), so each highway
// reads only its own notes.
//
// Coordinate note: the highway mesh's object space does NOT share the note
// transform frame, so an earlier attempt converted per-pixel in the shader with
// an uploaded matrix. material.SetMatrix to a raw custom-function global proved
// unreliable (read back as identity) while SetVectorArray worked, so the
// conversion now happens on the CPU: gameplay transforms each note position into
// this mesh's object space before upload, and the shader compares them directly
// against the Shader Graph Object-space Position node. No matrix needed here.

#define GEM_GLOW_MAX_SOURCES 16

// Bound by name from C# (material.SetVectorArray) on the material instance.
float4 _GemGlowPositions[GEM_GLOW_MAX_SOURCES];
float4 _GemGlowColors[GEM_GLOW_MAX_SOURCES];

// PositionOS is the highway pixel in the mesh's OBJECT space (Shader Graph
// Position node, Object space) — the same frame the uploaded sources live in.
void GemHighwayGlow_float(float3 PositionOS, float Count, float Intensity, out float3 GlowColor)
{
    GlowColor = float3(0.0, 0.0, 0.0);

    // Cheap zero path: nothing to do with no sources or no intensity.
    if (Intensity <= 0.0 || Count <= 0.0)
    {
        return;
    }

    float px = PositionOS.x;
    float pz = PositionOS.z;

    int count = (int) min(Count, (float) GEM_GLOW_MAX_SOURCES);

    [loop]
    for (int i = 0; i < count; i++)
    {
        float4 p = _GemGlowPositions[i];   // objX, objZ, width, length
        float4 c = _GemGlowColors[i];      // rgb, intensity

        float width  = max(p.z, 1e-4);
        float length = max(p.w, 1e-4);

        float dx = (px - p.x) / width;
        float dz = (pz - p.y) / length;

        // Symmetric lobe for now; asymmetric forward bias can be reintroduced once
        // the object-space travel direction is confirmed.
        float falloff = saturate(1.0 - dx * dx) * saturate(1.0 - dz * dz);
        falloff *= falloff; // sharpen so adjacent lanes don't wash together

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
