// pattern: Imperative Shell
using System;
using UnityEngine;

namespace YARG.Gameplay.Visuals
{
    [RequireComponent(typeof(MeshFilter))]
    public sealed class ModularKickVisual : MonoBehaviour
    {
        [SerializeField] private MeshFilter _beam;
        [SerializeField] private MeshFilter _mirroredBeam;
        [SerializeField, Tooltip("Baked bar meshes ordered by visible fret count, starting at one.")]
        private Mesh[] _meshesByLaneCount;

        private Beam _right, _left;

        public void Configure(int count) => ConfigureAtBoundaries(count, null);

        internal void ConfigureAtBoundaries(int count, float[] boundaries)
        {
            if (count < 1 || count > _meshesByLaneCount.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            var baked = _meshesByLaneCount[count - 1];
            if (boundaries != null && boundaries.Length != count + 1)
                throw new ArgumentException("Expected one edge on either side of each fret.", nameof(boundaries));
            bool uniform = true;
            if (boundaries != null)
                for (int i = 1; i < count; i++)
                    uniform &= Mathf.Abs(boundaries[i] - i / (float)count) < .000001f;
            if (uniform)
            {
                _beam.sharedMesh = baked;
                if (_mirroredBeam != null) _mirroredBeam.sharedMesh = baked;
                return;
            }
            // Both flam halves use the screen-space extent of the complete kick. Work
            // in their shared parent space to exclude the note's highway transform.
            var single = _meshesByLaneCount[0];
            _right ??= new Beam(_beam, _meshesByLaneCount.Length, single.bounds);
            if (_mirroredBeam != null) _left ??= new Beam(_mirroredBeam, _meshesByLaneCount.Length, single.bounds);
            float min = _right.Min, max = _right.Max;
            if (_left != null)
            {
                min = Mathf.Min(min, _left.Min);
                max = Mathf.Max(max, _left.Max);
            }
            // Split meshes use the same collar at every count. Take the most detailed
            // asset so a seam newly entering a half can use its authored collar.
            var collar = _left == null ? baked : _meshesByLaneCount[^1];
            _right.Place(boundaries, min, max, single, collar);
            _left?.Place(boundaries, min, max, single, collar);
        }

        private void OnDestroy()
        {
            _right?.Dispose();
            _left?.Dispose();
        }

        private sealed class Beam
        {
            private readonly MeshFilter _filter;
            private readonly float _start, _end;
            private readonly float[] _stops;
            private KickBeamTemplate _template;
            private Vector3[] _positions;
            private Mesh _mesh;
            internal float Min => Mathf.Min(_start, _end);
            internal float Max => Mathf.Max(_start, _end);

            internal Beam(MeshFilter filter, int maxLanes, Bounds bounds)
            {
                _filter = filter;
                _stops = new float[maxLanes + 1];
                var t = filter.transform;
                var local = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
                _start = local.MultiplyPoint3x4(Vector3.right * bounds.min.x).x;
                _end = local.MultiplyPoint3x4(Vector3.right * bounds.max.x).x;
            }

            internal void Place(float[] boundaries, float min, float max, Mesh single, Mesh collar)
            {
                var bounds = single.bounds;
                int index = 0;
                bool changed = false;
                void Push(float x)
                {
                    changed |= _stops[index] != x;
                    _stops[index++] = x;
                }
                Push(bounds.min.x);
                bool mirrored = _end < _start;
                for (int i = 1; i < boundaries.Length - 1; i++)
                {
                    float x = Mathf.LerpUnclamped(min, max, boundaries[mirrored ? boundaries.Length - 1 - i : i]);
                    float t = (x - _start) / (_end - _start);
                    if (t > 0.00001f && t < .99999f)
                        Push(bounds.min.x + t * bounds.size.x);
                }
                Push(bounds.max.x);
                var template = KickBeamTemplate.Get(single, collar, index - 2);
                if (_template == template && !changed)
                {
                    _filter.sharedMesh = _mesh;
                    return;
                }
                if (_mesh == null)
                {
                    _mesh = new Mesh { name = "Kick beam at impact", hideFlags = HideFlags.DontSave };
                    _mesh.MarkDynamic();
                }
                bool topologyChanged = template != _template;
                if (topologyChanged)
                {
                    _template = template;
                    if (_positions == null || _positions.Length < template.Vertices.Length)
                        _positions = new Vector3[template.Vertices.Length];
                    Array.Copy(template.Vertices, _positions, template.Vertices.Length);
                    _mesh.Clear();
                }
                template.Place(_positions, _stops);
                _mesh.SetVertices(_positions, 0, template.Vertices.Length);
                if (topologyChanged) template.SetSurfaces(_mesh);
                _mesh.RecalculateBounds();
                _filter.sharedMesh = _mesh;
            }

            internal void Dispose()
            {
                if (_mesh == null) return;
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
        }
    }
}
