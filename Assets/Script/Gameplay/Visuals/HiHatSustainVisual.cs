// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Gameplay.Player;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Player-owned ringing tails, independent of pooled cymbal gems and scoring.</summary>
    public sealed class HiHatSustainVisual : MonoBehaviour
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<Vector2> _fade = new();
        private readonly List<Vector2> _onset = new();
        private readonly List<Color> _colors = new();
        private readonly List<int> _triangles = new();
        private EliteDrumsEngine _engine;
        private Mesh _mesh;
        private Material _material;
        private float _width;
        private SyncTrack _sync;
        private Color _color;
        private Color _starPowerColor;
        private int _first;
        private double _lastTime = double.NegativeInfinity;

        public void Configure(EliteDrumsEngine engine, float center, float width, Color color)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _width = width;
            _sync = null;
            SetColors(color, color);
            _first = 0;
            _lastTime = double.NegativeInfinity;
            transform.localPosition = new Vector3(center, .012f, 0);
            if (_mesh != null)
            {
                _material.SetVector("_HeadRange", new Vector4(0, 1, 0, 0));
                _mesh.Clear();
                return;
            }
            var template = Resources.Load<Material>("HiHatSustain");
            if (template == null) throw new InvalidOperationException("Hi-hat sustain material is missing.");
            _material = new Material(template) { name = "Hi-hat sustain (player)" };
            _mesh = new Mesh { name = "Hi-hat ringing intervals" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = _material;
        }

        /// <summary>Head bounds are in the same player-local coordinates as the tail region.</summary>
        public void ConfigureEnvelopes(SyncTrack sync, float headCenter, float headWidth)
        {
            if (sync == null || sync.Resolution == 0 || sync.Tempos.Count == 0)
                throw new ArgumentException("A timed sync track is required.", nameof(sync));
            if (!float.IsFinite(headCenter) || !float.IsFinite(headWidth) || headWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(headWidth));
            _sync = sync;
            float relative = (headCenter - transform.localPosition.x) / _width + .5f;
            float half = headWidth / _width / 2;
            _material.SetVector("_HeadRange", new Vector4(Mathf.Clamp01(relative - half),
                Mathf.Clamp01(relative + half), 0, 0));
        }

        public void SetColors(Color normal, Color starPower)
        {
            _color = SustainLine.GetBrighterColorIfTooDark(normal);
            _starPowerColor = SustainLine.GetBrighterColorIfTooDark(starPower);
        }

        public void SetBeatPhase(float phase)
        {
            if (_material != null && float.IsFinite(phase))
                _material.SetFloat("_BeatPhase", phase - Mathf.Floor(phase));
        }

        public void Render(double visualTime, float speed, float farZ)
        {
            if (_mesh == null || speed <= 0 || !double.IsFinite(visualTime)) return;
            if (visualTime < _lastTime) _first = 0;
            _lastTime = visualTime;
            _vertices.Clear(); _uv.Clear(); _fade.Clear(); _onset.Clear(); _colors.Clear(); _triangles.Clear();
            var sustains = _engine.HiHatSustains;
            while (_first < sustains.Count && sustains[_first].EndTime <= visualTime) _first++;
            double visibleEnd = visualTime + (farZ - TrackPlayer.STRIKE_LINE_POS) / speed;
            for (int i = _first; i < sustains.Count; i++)
            {
                var sustain = sustains[i];
                if (sustain.StartTime >= visibleEnd) break;
                // An early renewal already owns the ringing strip at the fret.
                // Do not draw the remaining front of the interval it replaced.
                if (_engine.ActiveHiHatSustain is { } active && !ReferenceEquals(active, sustain) &&
                    sustain.EndTick <= active.StartTick && active.StartTime > visualTime) continue;
                bool held = ReferenceEquals(sustain, _engine.ActiveHiHatSustain);
                bool missed = _engine.WasHiHatSustainBroken(sustain);
                if (!held && sustain.StartTime < _engine.CurrentTime)
                {
                    missed = true;
                    foreach (var generator in sustain.Generators)
                        if (generator.WasHit) { missed = _engine.WasHiHatSustainBroken(sustain); break; }
                }
                bool starPower = false;
                foreach (var generator in sustain.Generators) starPower |= generator.IsStarPower;
                if (((DrumsEngineParameters)_engine.BaseParameters).NoStarPowerOverlap &&
                    _engine.EngineStats.IsStarPowerActive) starPower = false;
                var color = missed ? Color.gray : starPower ? _starPowerColor : _color;
                color.a = missed ? .12f : held ? .85f : .4f;
                AddInterval(sustain, held ? visualTime : Math.Max(visualTime, sustain.StartTime),
                    Math.Min(visibleEnd, sustain.EndTime), visualTime, speed, color, held);
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices); _mesh.SetUVs(0, _uv); _mesh.SetUVs(1, _fade); _mesh.SetUVs(2, _onset);
            _mesh.SetColors(_colors); _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
            _material.SetFloat("_VisualTime", (float)visualTime);
            _material.SetFloat("_FarZ", farZ);
        }

        private void AddInterval(EliteHiHatSustain sustain, double start, double end,
            double now, float speed, Color color, bool held)
        {
            if (end <= start) return;
            double spreadDuration = 0;
            if (_sync != null)
            {
                uint spreadTick = (uint)Math.Min(uint.MaxValue,
                    (ulong)sustain.StartTick + Math.Max(1u, _sync.Resolution / 8));
                spreadDuration = _sync.TickToTime(spreadTick) - _sync.TickToTime(sustain.StartTick);
            }
            bool splash = false;
            foreach (var generator in sustain.Generators)
                splash |= generator.OriginalPad == (int)EliteDrumNote.EliteDrumPad.HatPedal && generator.IsSplash;
            int first = _vertices.Count;
            // The highway bends vertices across X. Subdivide broad pedal tails so
            // their triangles follow that surface instead of cutting beneath it.
            int sections = Math.Max(1, Mathf.CeilToInt(_width / .06f));
            for (int i = 0; i <= sections; i++)
            {
                float side = (float)i / sections - .5f;
                Add(side, start); Add(side, end);
                if (i == 0) continue;
                int previous = first + (i - 1) * 2;
                _triangles.Add(previous); _triangles.Add(previous + 1); _triangles.Add(previous + 2);
                _triangles.Add(previous + 2); _triangles.Add(previous + 1); _triangles.Add(previous + 3);
            }
            return;

            void Add(float side, double time)
            {
                float z = TrackPlayer.STRIKE_LINE_POS + (float)(time - now) * speed;
                _vertices.Add(new Vector3(side * _width, 0, z));
                _uv.Add(new Vector2(side + .5f, (float)time));
                // Visual endings always fade, including charted renewals/cuts.
                // HasFade describes authored ringing semantics, not shader opacity.
                float fade = (float)((sustain.EndTime - time) / (sustain.EndTime - sustain.StartTime));
                _fade.Add(new Vector2(fade, held ? 1 : 0));
                // Preserve authored onset when the front is clipped at the fret.
                // Clamp in the fragment shader so interpolation retains 1/32 timing.
                float progress = spreadDuration > 0 ? (float)((time - sustain.StartTime) / spreadDuration) : 1;
                _onset.Add(new Vector2(progress, splash ? 1 : 0));
                _colors.Add(color);
            }
        }

        public void Clear()
        {
            _mesh?.Clear();
            _first = 0;
            _lastTime = double.NegativeInfinity;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
