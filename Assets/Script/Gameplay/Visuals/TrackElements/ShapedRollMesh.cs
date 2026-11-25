// pattern: Imperative Shell
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace YARG.Gameplay.Visuals
{
    // A neighboring fret can move underneath a long roll. Bake its ordinary
    // skinned lane once, cutting triangles only at changes of horizontal slope.
    // The resulting mesh travels down the highway like any other pooled lane.
    internal sealed class ShapedRollMesh
    {
        private struct Vertex
        {
            internal Vector3 Position, Normal;
            internal Vector2 UV;
            internal static Vertex Cut(Vertex a, Vertex b, float z)
            {
                float t = (z - a.Position.z) / (b.Position.z - a.Position.z);
                return new Vertex {
                    Position = Vector3.LerpUnclamped(a.Position, b.Position, t).WithZ(z),
                    Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t),
                    UV = Vector2.LerpUnclamped(a.UV, b.UV, t)
                };
            }
        }

        private readonly Transform _root;
        private readonly SkinnedMeshRenderer _skin;
        private readonly MeshRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly List<Vector3> _positions = new(), _normals = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<int> _indices = new();
        private readonly Vertex[] _a = new Vertex[8], _b = new Vertex[8], _c = new Vertex[8];

        internal ShapedRollMesh(Transform root, SkinnedMeshRenderer skin)
        {
            _root = root;
            _skin = skin;
            var child = new GameObject("Shaped roll") { layer = skin.gameObject.layer };
            child.transform.SetParent(skin.transform, false);
            _mesh = new Mesh { name = "Roll following fret boundaries", hideFlags = HideFlags.DontSave };
            child.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = child.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = skin.shadowCastingMode;
            _renderer.receiveShadows = skin.receiveShadows;
            _renderer.renderingLayerMask = skin.renderingLayerMask;
            _renderer.sortingLayerID = skin.sortingLayerID;
            _renderer.sortingOrder = skin.sortingOrder;
        }

        internal void Build(EliteDrumsRollLayout.PathPoint[] path, float length)
        {
            _renderer.sharedMaterials = _skin.sharedMaterials;
            // BakeMesh includes the imported armature scale. Convert its rigid pose into lane space.
            var toLane = _root.worldToLocalMatrix *
                Matrix4x4.TRS(_skin.transform.position, _skin.transform.rotation, Vector3.one);
            _mesh.Clear();
            _skin.BakeMesh(_mesh);
            var positions = _mesh.vertices;
            var normals = _mesh.normals;
            var uv = _mesh.uv;
            var vertices = new Vertex[positions.Length];
            var toNormal = toLane.inverse.transpose;
            // Clip in highway coordinates, but store the result in the imported mesh's
            // object space. Lane.shadergraph keeps only local X and Y for both materials.
            var toMesh = _renderer.transform.worldToLocalMatrix * _root.localToWorldMatrix;
            var toMeshNormal = toMesh.inverse.transpose;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vertex {
                    Position = toLane.MultiplyPoint3x4(positions[i]),
                    Normal = toNormal.MultiplyVector(normals[i]),
                    UV = uv.Length > i ? uv[i] : Vector2.zero
                };
            var triangles = new int[_mesh.subMeshCount][];
            for (int sub = 0; sub < triangles.Length; sub++) triangles[sub] = _mesh.GetTriangles(sub);
            _mesh.Clear();
            _positions.Clear();
            _normals.Clear();
            _uv.Clear();
            var submeshEnds = new int[triangles.Length];
            _indices.Clear();

            for (int sub = 0; sub < triangles.Length; sub++)
            {
                var indices = triangles[sub];
                for (int i = 0; i < indices.Length; i += 3)
                {
                    for (int j = 0; j < 3; j++) _a[j] = vertices[indices[i + j]];
                    float minZ = Mathf.Min(_a[0].Position.z, Mathf.Min(_a[1].Position.z, _a[2].Position.z));
                    float maxZ = Mathf.Max(_a[0].Position.z, Mathf.Max(_a[1].Position.z, _a[2].Position.z));
                    for (int span = 0; span < path.Length - 1; span++)
                    {
                        var from = path[span];
                        var to = path[span + 1];
                        float start = from.Fraction * length, end = to.Fraction * length;
                        // The first and last spans include the rounded end caps.
                        if (span > 0 && maxZ < start) break;
                        if (span + 2 < path.Length && minZ >= end) continue;
                        Vertex[] poly = _a;
                        int count = 3;
                        if (span > 0) { count = Clip(poly, count, _b, start, true); poly = _b; }
                        if (span + 2 < path.Length) { count = Clip(poly, count, _c, end, false); poly = _c; }
                        if (count < 3) continue;
                        int first = _positions.Count;
                        for (int k = 0; k < count; k++)
                        {
                            var v = poly[k];
                            float t = Mathf.InverseLerp(start, end, v.Position.z);
                            float widthScale = Mathf.Lerp(from.Width, to.Width, t) / path[0].Width;
                            float offset = Mathf.Lerp(from.Center, to.Center, t) - path[0].Center;
                            var position = v.Position.WithX(v.Position.x * widthScale + offset);
                            var normal = v.Normal.WithX(v.Normal.x / widthScale);
                            _positions.Add(toMesh.MultiplyPoint3x4(position));
                            _normals.Add(toMeshNormal.MultiplyVector(normal).normalized);
                            _uv.Add(v.UV);
                        }
                        for (int k = 2; k < count; k++)
                        {
                            _indices.Add(first);
                            _indices.Add(first + k - 1);
                            _indices.Add(first + k);
                        }
                    }
                }
                submeshEnds[sub] = _indices.Count;
            }

            _mesh.indexFormat = _positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.SetVertices(_positions);
            _mesh.SetNormals(_normals);
            _mesh.SetUVs(0, _uv);
            _mesh.subMeshCount = triangles.Length;
            int previous = 0;
            for (int sub = 0; sub < triangles.Length; sub++)
            {
                _mesh.SetTriangles(_indices, previous, submeshEnds[sub] - previous, sub, false);
                previous = submeshEnds[sub];
            }
            _mesh.RecalculateBounds();
            _mesh.RecalculateTangents();
            _renderer.enabled = true;
            _skin.enabled = false;
        }

        private static int Clip(Vertex[] input, int count, Vertex[] output, float z, bool keepAbove)
        {
            if (count == 0) return 0;
            int written = 0;
            var prev = input[count - 1];
            for (int i = 0; i < count; i++)
            {
                var cur = input[i];
                if ((prev.Position.z < z && cur.Position.z > z) || (prev.Position.z > z && cur.Position.z < z))
                    output[written++] = Vertex.Cut(prev, cur, z);
                if (keepAbove ? cur.Position.z >= z : cur.Position.z <= z) output[written++] = cur;
                prev = cur;
            }
            return written;
        }

        internal void Hide()
        {
            _renderer.enabled = false;
            _skin.enabled = true;
        }

        internal void Dispose() => Object.Destroy(_mesh);
    }
}
