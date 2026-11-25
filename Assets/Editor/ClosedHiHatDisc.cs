// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YARG.Themes;

namespace YARG.Editor
{
    internal static class ClosedHiHatDisc
    {
        private const int SPOKES = 64;
        private const float BOW_UV_RADIUS = .44f;
        private const float TIP_RADIUS = .06f / BOW_UV_RADIUS;
        private const float OUTER_RING_RADIUS = .99f;
        // sqrt(conical playing area / circular footprint area), measured before foreshortening.
        private const float EQUAL_AREA_DIAMETER_SCALE = 1.1262f;
        private const float LIP_UV_RADIUS = .5f;
        private const float ACCENT_LIP_SCALE = .9995f;
        // Peak normal travel on the 5x conical ring, as a fraction of rim-to-tip height.
        private const float RING_PROJECTION = .1463861f;

        // Mesh coordinates include the Rectangular highway's authored tilt and foreshortening.
        // UV2 is the original circular layout before that projection.
        internal static Mesh Build(Mesh source, ThemeNoteType type, float fraction, float thicknessScale,
            float? lipScaleOverride = null)
        {
            bool ghost = type == ThemeNoteType.ClosedHiHatGhost;
            bool accent = type == ThemeNoteType.ClosedHiHatAccent;
            if (!ghost && !accent && type != ThemeNoteType.ClosedHiHat)
                throw new ArgumentOutOfRangeException(nameof(type));
            float boundary = Mathf.Sqrt(fraction);
            if ((ghost || accent) && !(boundary > TIP_RADIUS && boundary < OUTER_RING_RADIUS))
                throw new ArgumentOutOfRangeException(nameof(fraction));

            var src = source.vertices;
            var uv = source.uv;
            var uv2 = source.uv2;
            var centerUv = new Vector2(.5f, .5f);
            var face = source.GetTriangles(1).Concat(source.GetTriangles(2)).Distinct().ToArray();
            Vector3 At(float u, float v) => src[face.First(i => (uv2[i] - new Vector2(u,v)).sqrMagnitude < 1e-9f)];
            var right = At(.94f, .5f);
            var left = At(.06f, .5f);
            var center = (right + left) * .5f;
            var x = right - center;
            var z = At(.5f, .94f) - center;
            var normal = Vector3.Cross(x,z).normalized;
            if (normal.y < 0) normal = -normal;
            float lift = (At(.5f, .5f) - center).magnitude * RING_PROJECTION;
            var shell = source.GetTriangles(0);
            var underside = src[shell.First(i => (uv2[i] - centerUv).sqrMagnitude < 1e-9f)];
            // Measure from the tilted underside. Move vertically so the planar footprint
            // stays centered, and shift the separate Accent ring together with the face.
            Vector3 BodyOffset(Vector3 point) => Vector3.up *
                (Vector3.Dot(point - underside, normal) * (thicknessScale - 1f) / normal.y);

            var points = new List<Vector3>();
            var ns = new List<Vector3>();
            var uvs = new List<Vector2>();
            var layout = new List<Vector2>();
            var slots = Enumerable.Range(0, accent ? 5 : ghost ? 4 : 3).Select(_ => new List<int>()).ToArray();
            int Add(Vector3 p, Vector3 n, Vector2 texture, Vector2 disc)
            {
                int i = points.Count;
                points.Add(p);
                ns.Add(n);
                uvs.Add(texture);
                layout.Add(disc);
                return i;
            }

            // The Accent lip already spans the common base. Fit the bevel into the space
            // between the enlarged face and that lip. Ghost and Neutral can grow as a unit.
            float faceGrowth = EQUAL_AREA_DIAMETER_SCALE - 1f;
            float lipScale = lipScaleOverride ?? (accent ? ACCENT_LIP_SCALE : EQUAL_AREA_DIAMETER_SCALE);
            float lipGrowth = LIP_UV_RADIUS * (lipScale - 1f);
            var mapping = new Dictionary<int,int>();
            foreach (int old in shell)
            {
                if (!mapping.TryGetValue(old, out int index))
                {
                    var radial = uv2[old] - centerUv;
                    float radius = radial.magnitude;
                    float growth = radius <= BOW_UV_RADIUS ? radius * faceGrowth :
                        Mathf.Lerp(BOW_UV_RADIUS * faceGrowth, lipGrowth,
                            Mathf.InverseLerp(BOW_UV_RADIUS, LIP_UV_RADIUS, radius));
                    var direction = radial.normalized;
                    var offset = (x * direction.x + z * direction.y) * (growth / BOW_UV_RADIUS);
                    index = Add(src[old] + offset + BodyOffset(src[old]), Vector3.zero, uv[old], uv2[old]);
                    mapping[old] = index;
                }
                slots[0].Add(index);
            }

            void Triangle(int slot, int a, int b, int c)
            {
                if (Vector3.Dot(Vector3.Cross(points[b] - points[a], points[c] - points[a]), ns[a]) < 0)
                    (c,b) = (b,c);
                slots[slot].Add(a);
                slots[slot].Add(b);
                slots[slot].Add(c);
            }

            int[] Loop(float radius, float height, int wall = 0)
            {
                var row = new int[SPOKES];
                for (int i = 0; i < SPOKES; i++)
                {
                    float angle = 2f * Mathf.PI * i / SPOKES;
                    var disc = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var tangent = -x * disc.y + z * disc.x;
                    var outward = Vector3.Cross(normal, tangent).normalized;
                    if (Vector3.Dot(outward, x * disc.x + z * disc.y) < 0) outward = -outward;
                    var tex = centerUv + disc * (BOW_UV_RADIUS * radius);
                    row[i] = Add(center + (x * disc.x + z * disc.y) * (radius * EQUAL_AREA_DIAMETER_SCALE) + normal * height,
                        wall == 0 ? normal : outward * wall, tex, tex);
                }
                return row;
            }

            void Join(int slot, int[] a, int[] b)
            {
                for (int i = 0; i < SPOKES; i++)
                {
                    int j = (i + 1) % SPOKES;
                    Triangle(slot, a[i], b[i], b[j]);
                    Triangle(slot, a[i], b[j], a[j]);
                }
            }

            center += BodyOffset(center);
            var tip = Loop(TIP_RADIUS, 0);
            var outer = Loop(1f, 0);
            int hub = Add(center, normal, centerUv, centerUv);
            for (int i = 0; i < SPOKES; i++)
                Triangle(2, hub, tip[i], tip[(i + 1) % SPOKES]);

            if (ghost)
            {
                var cap = Loop(boundary, 0);
                Join(3, tip, cap);
                Join(1, cap, outer);
            }
            else if (accent)
            {
                Join(1, tip, Loop(boundary, 0));
                Join(3, Loop(boundary, 0, -1), Loop(boundary, lift, -1));
                Join(4, Loop(boundary, lift), Loop(OUTER_RING_RADIUS, lift));
                Join(3, Loop(OUTER_RING_RADIUS, lift, 1), Loop(OUTER_RING_RADIUS, 0, 1));
                Join(1, Loop(OUTER_RING_RADIUS, 0), outer);
            }
            else
            {
                Join(1, tip, outer);
            }

            var mesh = new Mesh { name = "Volume " + type + " Top", subMeshCount = slots.Length };
            mesh.SetVertices(points);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, layout);
            for (int s = 0; s < slots.Length; s++) mesh.SetTriangles(slots[s], s);
            // Refit the bevel normals to the resized shell. Keep the analytic normals on the
            // face and the concentric ring walls, which have separate vertex loops.
            mesh.RecalculateNormals();
            var shellNormals = mesh.normals;
            foreach (int i in mapping.Values) ns[i] = shellNormals[i];
            mesh.SetNormals(ns);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
