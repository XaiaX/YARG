// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YARG.Editor
{
    // Editor-only. Keep the single-note mesh coordinates, UV seams, material slots and transforms.
    internal static class PillCymbalFlamMesh
    {
        private const float PLANE_EPSILON = .0000002f;
        // Fraction of one original base diameter on each side of the seam.
        private const float SPINE_HALF_WIDTH = .041f;

        internal sealed class Result
        {
            public readonly Dictionary<MeshFilter, Mesh> Replacements = new();
            public Mesh Spine, FlatMarker;
        }

        private struct Vertex
        {
            public Vector3 p, n;
            public Vector2 uv, uv2;
            public Color32 color;

            public Vertex Move(Vector3 offset)
            {
                var v = this;
                v.p += offset;
                return v;
            }

            public static Vertex OnPlane(Vertex a, Vertex b, float x)
            {
                float t = (x - a.p.x) / (b.p.x - a.p.x);
                Vector3 p = Vector3.LerpUnclamped(a.p, b.p, t);
                p.x = x;
                return new Vertex { p = p, n = Vector3.LerpUnclamped(a.n, b.n, t).normalized,
                    uv = Vector2.LerpUnclamped(a.uv, b.uv, t), uv2 = Vector2.LerpUnclamped(a.uv2, b.uv2, t),
                    color = Color32.Lerp(a.color, b.color, t) };
            }
        }

        private sealed class Surface
        {
            private readonly Dictionary<Vertex, int> _index = new();
            private readonly List<Vertex> _vertices = new();
            private readonly List<int>[] _slots;

            public Surface(int slots) => _slots = Enumerable.Range(0, slots).Select(_ => new List<int>()).ToArray();

            private int Index(Vertex v)
            {
                if (_index.TryGetValue(v, out int i)) return i;
                _index.Add(v, _vertices.Count);
                _vertices.Add(v);
                return _vertices.Count - 1;
            }

            public void Polygon(int slot, IList<Vertex> polygon)
            {
                for (int i = 2; i < polygon.Count; i++)
                {
                    var a = polygon[0]; var b = polygon[i-1]; var c = polygon[i];
                    if (Vector3.Cross(b.p - a.p, c.p - a.p).sqrMagnitude < 1e-20f) continue;
                    _slots[slot].Add(Index(a));
                    _slots[slot].Add(Index(b));
                    _slots[slot].Add(Index(c));
                }
            }

            public Mesh Finish(string name, bool normals = true, bool uv = true, bool uv2 = false, bool colors = false)
            {
                if (_vertices.Count == 0) throw new InvalidOperationException("Empty cymbal flam surface: " + name);
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices.Select(v => v.p).ToList());
                if (normals) mesh.SetNormals(_vertices.Select(v => v.n).ToList());
                if (uv) mesh.SetUVs(0, _vertices.Select(v => v.uv).ToList());
                if (uv2) mesh.SetUVs(1, _vertices.Select(v => v.uv2).ToList());
                if (colors) mesh.SetColors(_vertices.Select(v => v.color).ToList());
                mesh.subMeshCount = _slots.Length;
                for (int i = 0; i < _slots.Length; i++) mesh.SetTriangles(_slots[i], i, true);
                mesh.RecalculateBounds();
                if (normals && uv) mesh.RecalculateTangents();
                return mesh;
            }
        }

        private static Vertex[] Read(Mesh mesh)
        {
            var positions = mesh.vertices; var normals = mesh.normals;
            var uv = mesh.uv; var uv2 = mesh.uv2; var colors = mesh.colors32;
            return positions.Select((p, i) => new Vertex { p = p,
                n = normals.Length == 0 ? Vector3.up : normals[i],
                uv = uv.Length == 0 ? default : uv[i], uv2 = uv2.Length == 0 ? default : uv2[i],
                color = colors.Length == 0 ? (Color32)Color.white : colors[i] }).ToArray();
        }

        private static List<Vertex> Clip(IList<Vertex> polygon, float x, int keep)
        {
            var result = new List<Vertex>(polygon.Count + 1);
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i+1) % polygon.Count];
                float d = keep * (a.p.x-x), next = keep * (b.p.x-x);
                if (d >= -PLANE_EPSILON)
                {
                    if (Mathf.Abs(d) <= PLANE_EPSILON) a.p.x = x;
                    result.Add(a);
                }
                if ((d > PLANE_EPSILON && next < -PLANE_EPSILON) || (next > PLANE_EPSILON && d < -PLANE_EPSILON))
                    result.Add(Vertex.OnPlane(a, b, x));
            }
            return result;
        }

        private static void Triangles(Mesh source, Action<int, Vertex[]> visit)
        {
            var vertices = Read(source);
            for (int slot = 0; slot < source.subMeshCount; slot++)
            {
                var indices = source.GetTriangles(slot);
                for (int i = 0; i < indices.Length; i += 3)
                    visit(slot, new[] { vertices[indices[i]], vertices[indices[i+1]], vertices[indices[i+2]] });
            }
        }

        // Move the original semicircles apart and join every surface crossed by the sagittal plane.
        // A bridge's middle row also gives the baked lid shadow a sample in the waist between bells.
        private static Mesh ExtendBase(Mesh source, float shift, Surface marker, float lidRadius)
        {
            var result = new Surface(source.subMeshCount);
            var bounds = source.bounds;
            float x = bounds.center.x;
            // The source metal atlas is a disc. Keep each deck, bevel and side vertex on its
            // original texture ring, then distribute that ring around the entire pill.
            // This puts the atlas's broad brushed highlight toward the player (about -61°).
            float angle = -151f * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
            Vertex OnPill(Vertex v, float offset, bool metal)
            {
                v.p.x += offset;
                if (!metal) return v;
                var radial = new Vector2((v.p.x - x) / (bounds.extents.x + shift),
                    (v.p.z - bounds.center.z) / bounds.extents.z).normalized;
                float radius = (v.uv - Vector2.one * .5f).magnitude;
                v.uv = Vector2.one * .5f + radius * new Vector2(
                    cosine * radial.x - sine * radial.y, sine * radial.x + cosine * radial.y);
                return v;
            }
            Triangles(source, (slot, triangle) =>
            {
                bool metal = marker != null && slot == 0;
                foreach (int side in new[] {-1, 1})
                {
                    var half = Clip(triangle, x, side);
                    result.Polygon(slot, half.Select(v => OnPill(v, side * shift, metal)).ToArray());
                    if (side < 0 || half.Count < 3 || !triangle.Any(v => v.p.x > x + PLANE_EPSILON)) continue;
                    for (int i = 0; i < half.Count; i++)
                    {
                        var a = half[i]; var b = half[(i+1) % half.Count];
                        if (a.p.x != x || b.p.x != x || (a.p-b.p).sqrMagnitude < 1e-15f) continue;
                        var reach = Vector3.right * shift;
                        if (metal)
                        {
                            // Sample the straight run with the same mapping as the semicircular
                            // ends. Short UV chords stay inside their brushed-metal band.
                            for (int segment = -2; segment < 2; segment++)
                            {
                                float left = segment * shift * .5f;
                                float right = (segment + 1) * shift * .5f;
                                result.Polygon(slot, new[] { OnPill(b, right, true), OnPill(a, right, true),
                                    OnPill(a, left, true), OnPill(b, left, true) });
                            }
                        }
                        else
                        {
                            result.Polygon(slot, new[] { b.Move(reach), a.Move(reach), a, b });
                            result.Polygon(slot, new[] { b, a, a.Move(-reach), b.Move(-reach) });
                        }

                        if (marker == null) continue;
                        float front = ((a.p.z+b.p.z)*.5f-bounds.center.z) / bounds.extents.z;
                        var outward = Vector3.Cross(a.p-b.p, -reach).normalized;
                        if (front < .55f || outward.z < .35f) continue;
                        var pad = outward * (bounds.size.x * .0014f);
                        var halfMarker = Vector3.right * (bounds.size.x * .085f);
                        marker.Polygon(0, new[] { b.Move(pad+halfMarker), a.Move(pad+halfMarker),
                            a.Move(pad-halfMarker), b.Move(pad-halfMarker) });
                    }
                }
            });
            var mesh = result.Finish(source.name + " Pill", source.normals.Length > 0, source.uv.Length > 0,
                source.uv2.Length > 0, source.colors32.Length > 0);
            if (lidRadius > 0)
            {
                // The two overlapping lids cast a union of round shadows on the wider basin.
                mesh.colors32 = mesh.vertices.Select(p =>
                {
                    float dx = (Mathf.Abs(p.x - bounds.center.x) - shift) / bounds.extents.x;
                    float dz = (p.z - bounds.center.z) / bounds.extents.z + .36f;
                    float d = Mathf.Sqrt(dx*dx + dz*dz);
                    byte light = (byte)Mathf.RoundToInt(255 * Mathf.SmoothStep(0, 1,
                        Mathf.InverseLerp(lidRadius-.12f, lidRadius+.12f, d)));
                    return new Color32(light, light, light, 255);
                }).ToArray();
            }
            return mesh;
        }

        private readonly struct Section
        {
            public readonly Vector3 a, b;
            public Section(Vector3 a, Vector3 b) { this.a = a; this.b = b; }
        }

        private static Mesh JoinBells(Mesh source, float shift, float halfSpine, List<Section> cuts)
        {
            var result = new Surface(source.subMeshCount);
            float center = source.bounds.center.x;
            Triangles(source, (slot, triangle) =>
            {
                foreach (int side in new[] {-1, 1})
                {
                    // The bisector selects the higher of two identical overlapping cone surfaces.
                    // Leave just enough of that envelope out to insert the solid seam.
                    var translated = triangle.Select(v => v.Move(Vector3.right * (side*shift))).ToArray();
                    float cut = center + side*halfSpine;
                    var exterior = Clip(translated, cut, side);
                    if (exterior.Count < 3) continue;
                    if (side > 0 && cuts != null)
                    {
                        for (int i = 0; i < exterior.Count; i++)
                        {
                            var a = exterior[i].p; var b = exterior[(i+1) % exterior.Count].p;
                            if (a.x == cut && b.x == cut && (a-b).sqrMagnitude > 1e-15f)
                                cuts.Add(new Section(a, b));
                        }
                    }
                    result.Polygon(slot, exterior);
                }
            });
            return result.Finish(source.name + " Joined", source.normals.Length > 0, source.uv.Length > 0,
                source.uv2.Length > 0, source.colors32.Length > 0);
        }

        private static Mesh CenterSpine(List<Section> cuts, float x, float halfWidth, Vector2 uvRadii)
        {
            if (cuts.Count == 0) throw new InvalidOperationException("The two cymbal surfaces do not overlap.");
            float end = cuts.Max(s => Mathf.Max(s.a.z,s.b.z));
            float start = cuts.Min(s => Mathf.Min(s.a.z,s.b.z));
            float lift = halfWidth * .27f;
            var surface = new Surface(1);
            // Sample each change in the original mesh's longitudinal section, so even a thin
            // raised accent lip is covered at both ends. Merge subpixel samples.
            var levels = cuts.SelectMany(s => new[] {s.a.z,s.b.z}).OrderBy(z=>z).ToList();
            var zRows = new List<float> { start };
            foreach (float z in levels)
                if (z-zRows[zRows.Count-1] > halfWidth*.035f && end-z > halfWidth*.035f) zRows.Add(z);
            zRows.Add(end);
            var rows = new List<Vector3[]>();
            foreach (float z in zRows)
            {
                var heights = new List<float>();
                foreach (var s in cuts)
                {
                    float lo = Mathf.Min(s.a.z,s.b.z), hi = Mathf.Max(s.a.z,s.b.z);
                    if (z < lo-PLANE_EPSILON || z > hi+PLANE_EPSILON) continue;
                    if (hi-lo < PLANE_EPSILON) { heights.Add(s.a.y); heights.Add(s.b.y); }
                    else heights.Add(Mathf.Lerp(s.a.y,s.b.y,(z-s.a.z)/(s.b.z-s.a.z)));
                }
                float roof = heights.Max(), floor = heights.Min();
                rows.Add(new[] {
                    new Vector3(x-halfWidth, floor-lift*.2f, z), new Vector3(x-halfWidth, roof+lift*.35f, z),
                    new Vector3(x-halfWidth*.75f, roof+lift, z), new Vector3(x+halfWidth*.75f, roof+lift, z),
                    new Vector3(x+halfWidth, roof+lift*.35f, z), new Vector3(x+halfWidth, floor-lift*.2f, z)
                });
            }

            // Triangle edges inside the original bows produce dozens of redundant samples.
            // Retain profile corners and bound the vertical error below the bevel clearance.
            var simplified = new List<Vector3[]> { rows[0] };
            void Simplify(int first, int last)
            {
                var a = rows[first];
                var b = rows[last];
                int split = -1;
                float maxError = halfWidth * .035f;
                for (int i = first + 1; i < last; i++)
                {
                    var row = rows[i];
                    float t = Mathf.InverseLerp(a[0].z, b[0].z, row[0].z);
                    float error = Mathf.Max(Mathf.Abs(row[0].y - Mathf.Lerp(a[0].y, b[0].y, t)),
                        Mathf.Abs(row[2].y - Mathf.Lerp(a[2].y, b[2].y, t)));
                    if (error <= maxError) continue;
                    maxError = error;
                    split = i;
                }
                if (split < 0) simplified.Add(b);
                else
                {
                    Simplify(first, split);
                    Simplify(split, last);
                }
            }
            Simplify(0, rows.Count-1);
            rows = simplified;

            // Unwrap the crest, bevels and sidewalls together onto one area of the metal
            // texture. Adjacent faces share their boundary UVs even
            // where the longitudinal profile turns sharply. The short return at each end
            // uses the same six UVs, with enough area for a valid tangent.
            var crossSection = new[]
            {
                new Vector2(0f, -.12f), new Vector2(.10f, 0f), new Vector2(.20f, .025f),
                new Vector2(.80f, .025f), new Vector2(.90f, 0f), new Vector2(1f, -.12f)
            };
            var strip = rows.Select(row =>
            {
                float along = Mathf.InverseLerp(start, end, row[0].z);
                return row.Select((position, i) =>
                {
                    var section = crossSection[i];
                    float radius = Mathf.Lerp(uvRadii.x, uvRadii.y, section.x);
                    float angle = (-64f + 20f * (along + section.y)) * Mathf.Deg2Rad;
                    return new Vertex { p = position, uv = new Vector2(.5f + radius * Mathf.Cos(angle),
                        .5f + radius * Mathf.Sin(angle)) };
                }).ToArray();
            }).ToArray();

            void Face(params Vertex[] vertices)
            {
                // Unity's Vector3.normalized zeroes small vectors. A valid bevel facet here
                // can have area below that threshold, which would feed a zero normal to HDR metal.
                var cross = Vector3.zero;
                for (int i = 2; i < vertices.Length; i++)
                {
                    var triangle = Vector3.Cross(vertices[i-1].p-vertices[0].p, vertices[i].p-vertices[0].p);
                    if (triangle.sqrMagnitude > cross.sqrMagnitude) cross = triangle;
                }
                if (cross.sqrMagnitude < 1e-20f) return;
                var n = cross / cross.magnitude;
                for (int i = 0; i < vertices.Length; i++) vertices[i].n = n;
                surface.Polygon(0, vertices);
            }
            // The contour above circulates clockwise in local XY seen from +Z.
            for (int i = 1; i < strip.Length; i++)
            for (int j = 0; j < 6; j++)
            {
                int k = (j+1)%6;
                Face(strip[i-1][j], strip[i][j], strip[i][k], strip[i-1][k]);
            }
            Face(strip[0]);
            Face(strip[strip.Length-1].Reverse().ToArray());
            return surface.Finish("Cymbal flam center metal");
        }

        internal static Result Build(GameObject root, Vector2 spineRadii)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name);
            var basis = filters["Base"];
            var top = filters["Top"];
            var baseMesh = basis.sharedMesh;
            var topMesh = top.sharedMesh;
            float baseWidth = baseMesh.bounds.size.x;
            float bellRadius = topMesh.bounds.extents.x;
            var cuts = new List<Section>();
            var marker = new Surface(1);
            var result = new Result();
            foreach (var pair in filters)
            {
                var f = pair.Value;
                // Existing prefab X axes are parallel (the entire Geometry group faces the highway).
                float ratio = (f.transform.worldToLocalMatrix * basis.transform.localToWorldMatrix)
                    .MultiplyVector(Vector3.right).magnitude;
                float width = baseWidth * ratio;
                float shift = width * .25f;
                if (f == top && bellRadius <= shift)
                    throw new InvalidOperationException("Cymbal flam loses its central overlap at " + root.name);
                switch (pair.Key)
                {
                    case "Base":
                    case "Interior":
                    case "Base Glow":
                    case "Well pulse":
                        float lid = 0;
                        if (pair.Key == "Well pulse")
                        {
                            var face = topMesh.GetSubMesh(1).bounds;
                            float toWell = (f.transform.worldToLocalMatrix * top.transform.localToWorldMatrix)
                                .MultiplyVector(Vector3.right).magnitude;
                            lid = face.size.x * toWell / f.sharedMesh.bounds.size.x;
                        }
                        result.Replacements[f] = ExtendBase(f.sharedMesh, shift, f == basis ? marker : null, lid);
                        break;
                    case "Top":
                    case "Accent ring":
                    case "Light":
                        result.Replacements[f] = JoinBells(f.sharedMesh, shift, width*SPINE_HALF_WIDTH,
                            pair.Key == "Light" ? null : cuts);
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected volume cymbal mesh: " + pair.Key);
                }
            }
            float localWidth = baseWidth * (top.transform.worldToLocalMatrix * basis.transform.localToWorldMatrix)
                .MultiplyVector(Vector3.right).magnitude;
            result.Spine = CenterSpine(cuts, topMesh.bounds.center.x, localWidth*SPINE_HALF_WIDTH, spineRadii);
            result.FlatMarker = marker.Finish("Cymbal flat flam front marker");
            return result;
        }
    }
}
