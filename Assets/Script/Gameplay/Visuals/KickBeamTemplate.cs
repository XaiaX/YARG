// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Builds reusable kick topology from the authored tube, emitters and a single collar.</summary>
    internal sealed class KickBeamTemplate
    {
        // Finite: a baked asset per dynamic/flam and physical lane count, then at
        // most one topology per visible collar count. Impact positions aren't keys.
        private static readonly ConditionalWeakTable<Mesh, Dictionary<(Mesh, int), KickBeamTemplate>> Cache = new();
        internal readonly Vector3[] Vertices;
        private readonly int[] _stops;
        private readonly Vector3[] _normals;
        private readonly Vector4[] _tangents;
        private readonly Vector2[] _uv;
        private readonly List<int>[] _triangles;
        private readonly int _lastStop;

        internal static KickBeamTemplate Get(Mesh single, Mesh collars, int count)
        {
            var cache = Cache.GetOrCreateValue(single);
            if (!cache.TryGetValue((collars, count), out var template))
                cache.Add((collars, count), template = new KickBeamTemplate(single, collars, count));
            return template;
        }

        private KickBeamTemplate(Mesh single, Mesh collars, int count)
        {
            _lastStop = count + 1;
            var vertices = new List<Vector3>();
            var stops = new List<int>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv = new List<Vector2>();
            _triangles = new List<int>[single.subMeshCount];
            for (int i = 0; i < _triangles.Length; i++) _triangles[i] = new List<int>();
            var body = new MeshSource(single);
            float start = single.bounds.min.x, end = single.bounds.max.x;
            float middle = (start + end) * .5f;
            for (int segment = 0; segment <= count; segment++)
            {
                // Faces crossing the midpoint are the uninterrupted tube (including
                // its ghost skin). Faces on one end are the metal emitter. Repeat the
                // tube/its UVs per cell and include only the two outermost emitters.
                Add(body, (min, max) => (min < middle && max > middle) ||
                    (segment == 0 && max < middle) || (segment == count && min > middle), x =>
                {
                    bool left = x < middle;
                    float offset = left ? (segment == 0 ? x - start : 0f) : (segment == count ? x - end : 0f);
                    return (left ? segment : segment + 1, offset);
                });
            }
            if (count > 0)
            {
                var rings = new MeshSource(collars);
                // The tube (submesh zero) has a cross-section at each authored seam.
                // Pick one interior station, midway between its neighbors as cutoffs;
                // this selects only the collar, leaving both neighboring tubes out.
                var stations = new List<float>();
                foreach (int i in rings.Triangles[0]) stations.Add(rings.Vertices[i].x);
                stations.Sort();
                var distinct = new List<float>();
                foreach (float x in stations)
                    if (distinct.Count == 0 || x - distinct[^1] > .0001f) distinct.Add(x);
                int selected = distinct.Count / 2;
                float center = distinct[selected];
                float minX = (center + distinct[selected - 1]) * .5f;
                float maxX = (center + distinct[selected + 1]) * .5f;
                for (int seam = 1; seam <= count; seam++)
                    Add(rings, (min, max) => min > minX && max < maxX, x => (seam, x - center));
            }
            Vertices = vertices.ToArray();
            _stops = stops.ToArray();
            _normals = normals.ToArray();
            _tangents = tangents.ToArray();
            _uv = uv.ToArray();

            void Add(MeshSource source, Func<float, float, bool> include, Func<float, (int Stop, float Offset)> place)
            {
                var remap = new Dictionary<int, int>();
                for (int slot = 0; slot < source.Triangles.Length; slot++)
                {
                    var indices = source.Triangles[slot];
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        float x = source.Vertices[indices[i]].x;
                        float y = source.Vertices[indices[i + 1]].x;
                        float z = source.Vertices[indices[i + 2]].x;
                        if (!include(Math.Min(x, Math.Min(y, z)), Math.Max(x, Math.Max(y, z)))) continue;
                        for (int corner = i; corner < i + 3; corner++)
                        {
                            int original = indices[corner];
                            if (!remap.TryGetValue(original, out int mapped))
                            {
                                mapped = vertices.Count;
                                remap.Add(original, mapped);
                                var vertex = source.Vertices[original];
                                var placement = place(vertex.x);
                                vertex.x = placement.Offset;
                                vertices.Add(vertex);
                                stops.Add(placement.Stop);
                                normals.Add(source.Normals[original]);
                                tangents.Add(source.Tangents[original]);
                                uv.Add(source.UV[original]);
                            }
                            _triangles[slot].Add(mapped);
                        }
                    }
                }
            }
        }

        internal void Place(Vector3[] output, float[] stops)
        {
            for (int i = 0; i < Vertices.Length; i++)
            {
                int station = _stops[i];
                float x = Vertices[i].x + stops[station];
                if (station > 0 && station < _lastStop && Vertices[i].x != 0f)
                {
                    // Clip a collar as its seam enters a flam half or approaches
                    // another collar. The end housings stay fixed around the gap.
                    float min = station == 1 ? stops[0] : (stops[station - 1] + stops[station]) * .5f;
                    float max = station + 1 == _lastStop ? stops[_lastStop] : (stops[station] + stops[station + 1]) * .5f;
                    x = Mathf.Clamp(x, min, max);
                }
                output[i].x = x;
            }
        }

        internal void SetSurfaces(Mesh mesh)
        {
            mesh.normals = _normals;
            mesh.tangents = _tangents;
            mesh.uv = _uv;
            mesh.subMeshCount = _triangles.Length;
            for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i, false);
        }

        private sealed class MeshSource
        {
            internal readonly Vector3[] Vertices, Normals;
            internal readonly Vector4[] Tangents;
            internal readonly Vector2[] UV;
            internal readonly int[][] Triangles;

            internal MeshSource(Mesh mesh)
            {
                Vertices = mesh.vertices;
                Normals = mesh.normals;
                Tangents = mesh.tangents;
                UV = mesh.uv;
                Triangles = new int[mesh.subMeshCount][];
                for (int i = 0; i < Triangles.Length; i++) Triangles[i] = mesh.GetTriangles(i);
            }
        }
    }
}
