// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using YARG.Gameplay.Player;
using YARG.Settings;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Scrolls the charted strips with the notes and anchors the flame sprites at the beaters.</summary>
    internal sealed class EliteDoubleKickWarningVisual : MonoBehaviour
    {
        private static readonly int _timeId = Shader.PropertyToID("_SongSeconds");
        private readonly List<Vector3> _vertices = new(16);
        private readonly List<Vector4> _uv = new(16);
        private readonly List<Vector2> _sparkData = new(16);
        private readonly List<int> _triangles = new(24);
        private TrackPlayer _player;
        private MaterialPropertyBlock _properties;
        private MeshRenderer _renderer;
        private Mesh _mesh;

        private void Awake()
        {
            _player = GetComponentInParent<TrackPlayer>();
            _properties = new MaterialPropertyBlock();
            _mesh = new Mesh { name = "Elite double kick warning", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sharedMaterial = Resources.Load<Material>("EliteDoubleKickWarning");
            _renderer.enabled = false;
        }

        internal void Show(double now, float speed, float farZ, List<(double Start, double End)> windows,
            bool rails, List<(double Time, bool IsKick)> markers, bool staggerSparks, bool flames, float kickPulse)
        {
            _vertices.Clear();
            _uv.Clear();
            _sparkData.Clear();
            _triangles.Clear();
            const float strike = TrackPlayer.STRIKE_LINE_POS;
            float edge = (_player != null ? _player.HighwayWidth : TrackPlayer.TRACK_WIDTH) * .5f;
            if (rails)
            {
                foreach (var (start, end) in windows)
                {
                    float front = (float) ((start - now) * speed);
                    float back = (float) ((end - now) * speed);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var inner = new Vector3(side * (edge - .015f), .065f, strike + front);
                        var outer = new Vector3(side * (edge + .023f), .065f, strike + front);
                        var span = Vector3.forward * (back - front);
                        AddQuad(inner, outer, inner + span, outer + span, DoubleKickWarningStyle.EdgeRails,
                            front / (farZ - strike), back / (farZ - strike), 0, 0);
                    }
                }
            }

            foreach (var (time, isKick) in markers)
            {
                float distance = (float) ((time - now) * speed);
                float fadePosition = distance / (farZ - strike);
                float height = isKick ? .295f : .17f;
                float halfWidth = isKick ? .106f : .055f;
                var spark = new Vector2(staggerSparks ? SparkPhaseOffset(time) : 0f, isKick ? 1f : 0f);
                for (int side = -1; side <= 1; side += 2)
                {
                    var center = new Vector3(side * (edge + .042f), .065f, strike + distance);
                    // The card spans the camera's horizontal axis at every distance.
                    // Lean its tip slightly up-highway to expose the face from above.
                    var half = Vector3.right * halfWidth;
                    var rise = new Vector3(side * height * .10f, height, height * .30f);
                    AddQuad(center - half, center - half + rise, center + half, center + half + rise,
                        DoubleKickWarningStyle.EmberTrails, fadePosition, fadePosition,
                        side < 0 ? 1 : 0, side < 0 ? 0 : 1, spark);
                }
            }

            if (flames)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    var center = new Vector3(side * (edge + .026f), .095f, strike);
                    var half = Vector3.right * (.081f * (1f + .55f * kickPulse));
                    var rise = new Vector3(0, .24f, .055f) * (1f + .70f * kickPulse);
                    AddQuad(center - half, center + half, center - half + rise, center + half + rise,
                        DoubleKickWarningStyle.BeaterFlames, 0, 1, 0, 0);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uv);
            _mesh.SetUVs(1, _sparkData);
            _mesh.SetTriangles(_triangles, 0);
            // Ten minutes contains whole cycles of both spark layers and the beater flames.
            _properties.SetFloat(_timeId, (float) (now % 600d));
            _renderer.SetPropertyBlock(_properties);
            _renderer.enabled = _vertices.Count > 0;
        }

        // Hash the chart time so seeking reproduces a marker's phase and adjacent
        // subdivisions do not advance in a sequence along the rail.
        private static float SparkPhaseOffset(double chartTime)
        {
            ulong bits = unchecked((ulong) BitConverter.DoubleToInt64Bits(chartTime));
            uint hash = (uint) (bits >> 32) ^ (uint) bits;
            hash = unchecked((hash ^ (hash >> 16)) * 0x85ebca6bu);
            hash = unchecked((hash ^ (hash >> 13)) * 0xc2b2ae35u);
            hash ^= hash >> 16;
            return (hash >> 8) * (1f / 16777216f);
        }

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, DoubleKickWarningStyle style,
            float nearV, float farV, float nearU, float farU, Vector2 spark = default)
        {
            int i = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);
            _uv.Add(new Vector4(0, nearV, (float) style, nearU));
            _uv.Add(new Vector4(1, nearV, (float) style, nearU));
            _uv.Add(new Vector4(0, farV, (float) style, farU));
            _uv.Add(new Vector4(1, farV, (float) style, farU));
            for (int vertex = 0; vertex < 4; vertex++) _sparkData.Add(spark);
            _triangles.Add(i);
            _triangles.Add(i + 2);
            _triangles.Add(i + 1);
            _triangles.Add(i + 1);
            _triangles.Add(i + 2);
            _triangles.Add(i + 3);
        }

        internal void Hide() => _renderer.enabled = false;

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
