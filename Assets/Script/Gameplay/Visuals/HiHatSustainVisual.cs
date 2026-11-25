// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Gameplay.Player;
using YARG.Settings;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Player-owned ringing tails, independent of pooled cymbal gems and scoring.</summary>
    public sealed class HiHatSustainVisual : MonoBehaviour
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<Vector2> _fade = new();
        private readonly List<Vector2> _state = new();
        private readonly List<Vector4> _waves = new();
        private readonly List<Vector3> _envelopes = new();
        private readonly Dictionary<EliteHiHatSustain, int> _indices = new();
        private HiHatSustainExpansion[] _expansions = Array.Empty<HiHatSustainExpansion>();
        private HiHatSustainExpansion _carriedExpansion;
        private int[] _runEnds;
        private bool[] _pedalOrigins;
        private (Vector2 Head, Vector3 Tail)[] _rollOffsets;
        private float _headUV;
        private readonly List<double> _rows = new();
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
        private float _lastSpeed, _lastFarZ;
        private float _waitingOpacity = .28f, _heldOpacity = .33f;
        private float _waitingGlow = .35f, _heldGlow = .7f;
        private bool _appearanceApplied;

        public void Configure(EliteDrumsEngine engine, float center, float width, Color color)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _width = width;
            _sync = null;
            _rollOffsets = null;
            _indices.Clear();
            _carriedExpansion = default;
            var sustains = engine.HiHatSustains;
            _runEnds = HiHatSustainExpansion.FindRunEnds(sustains);
            _expansions = new HiHatSustainExpansion[sustains.Count];
            _pedalOrigins = new bool[sustains.Count];
            for (int i = 0; i < sustains.Count; i++)
            {
                _indices.Add(sustains[i], i);
                _pedalOrigins[i] = HiHatSustainExpansion.HasVisiblePedal(sustains[i]);
            }
            SetColors(color, color);
            _first = 0;
            _lastTime = double.NegativeInfinity;
            transform.localPosition = new Vector3(center, .012f, 0);
            if (_mesh != null)
            {
                _material.SetVector("_HeadRange", new Vector4(0, 1, 0, 0));
                _mesh.Clear();
                _appearanceApplied = false;
                RefreshAppearance();
                return;
            }
            var template = Resources.Load<Material>("HiHatSustain");
            if (template == null) throw new InvalidOperationException("Hi-hat sustain material is missing.");
            _material = new Material(template) { name = "Hi-hat sustain (player)" };
            _mesh = new Mesh { name = "Hi-hat ringing intervals" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = _material;
            RefreshAppearance();
        }

        /// <summary>Head bounds are in the same player-local coordinates as the tail region.</summary>
        public void ConfigureEnvelopes(SyncTrack sync, float headCenter, float headWidth, float fretWidth)
        {
            if (sync == null || sync.Resolution == 0 || sync.Tempos.Count == 0)
                throw new ArgumentException("A timed sync track is required.", nameof(sync));
            if (!float.IsFinite(headCenter) || !float.IsFinite(headWidth) || headWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(headWidth));
            if (!float.IsFinite(fretWidth) || fretWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(fretWidth));
            _sync = sync;
            float relative = (headCenter - transform.localPosition.x) / _width + .5f;
            float half = headWidth / _width / 2;
            _material.SetVector("_HeadRange", new Vector4(Mathf.Clamp01(relative - half),
                Mathf.Clamp01(relative + half), _width, fretWidth));
        }

        internal void ConfigureRollHeads(EliteDrumsPlayer player)
        {
            _rollOffsets = null;
            var range = _material.GetVector("_HeadRange");
            _headUV = (range.x + range.y) * .5f;
            float localAxis = (_headUV - .5f) * _width;
            float baseCenter = transform.localPosition.x;
            int column = player.GetVisualPosition((int)EliteDrumNote.EliteDrumPad.HiHat);
            var sustains = _engine.HiHatSustains;
            for (int i = 0; i < sustains.Count; i++)
            {
                var generator = sustains[i].Generators[0];
                var edges = player.TryGetKickBoundaries(generator);
                bool slotted = player.TryGetRollSlot(generator, out float headCenter, out float headWidth);
                if (edges == null && !slotted) continue;
                if (!slotted)
                {
                    EliteDrumsRuntimeGeometry.GetRegion(player, generator, false, out headCenter, out headWidth);
                }
                // The shader's waiting head spans one fret. Store each mesh
                // edge's X offset at the waiting head and at full expansion.
                float scaleDelta = headWidth / range.w - 1f;
                float shift = headCenter - baseCenter - localAxis;
                var head = new Vector2(shift + (-_width / 2f - localAxis) * scaleDelta,
                    shift + (_width / 2f - localAxis) * scaleDelta);
                var tail = Vector3.zero;
                if (edges != null)
                {
                    int reach = player.Player.Profile.OneFretHiHatSustains ? 0 : 1;
                    float left = (edges[Mathf.Max(0, column - reach)] - .5f) * TrackPlayer.TRACK_WIDTH;
                    float right = (edges[Mathf.Min(edges.Length - 1, column + reach + 1)] - .5f) * TrackPlayer.TRACK_WIDTH;
                    float hat = ((edges[column] + edges[column + 1]) * .5f - .5f) * TrackPlayer.TRACK_WIDTH;
                    tail = new Vector3(left - (baseCenter - _width / 2f), hat - (baseCenter + localAxis),
                        right - (baseCenter + _width / 2f));
                }
                _rollOffsets ??= new (Vector2, Vector3)[sustains.Count];
                _rollOffsets[i] = (head, tail);
            }
        }

        public void NotifyActivation(EliteHiHatSustain sustain, double time)
        {
            if (!double.IsFinite(time) || sustain == null || !_indices.TryGetValue(sustain, out int index)) return;
            _carriedExpansion = HiHatSustainExpansion.Activate(_carriedExpansion, index, _runEnds[index], time, _pedalOrigins[index]);
            _expansions[index] = _carriedExpansion;
        }

        public void ResetInheritedWidth() => _carriedExpansion = default;

        private void Update() => RefreshAppearance();

        private void RefreshAppearance()
        {
            if (_material == null) return;
            var settings = SettingsManager.Settings;
            float waiting = Fraction(settings?.EliteHiHatWaitingOpacity.Value ?? 28, .28f, 1);
            float held = Fraction(settings?.EliteHiHatHeldOpacity.Value ?? 33, .33f, 1);
            float waitingGlow = Fraction(settings?.EliteHiHatWaitingGlow.Value ?? 35, .35f, 2);
            float heldGlow = Fraction(settings?.EliteHiHatHeldGlow.Value ?? 70, .7f, 2);
            bool opacityChanged = waiting != _waitingOpacity || held != _heldOpacity;
            if (_appearanceApplied && !opacityChanged && waitingGlow == _waitingGlow && heldGlow == _heldGlow) return;
            _waitingOpacity = waiting; _heldOpacity = held;
            _waitingGlow = waitingGlow; _heldGlow = heldGlow;
            _material.SetVector("_GlowLevels", new Vector4(waitingGlow, heldGlow, 0, 0));
            _appearanceApplied = true;
            // This component keeps updating while the gameplay loop is paused.
            if (opacityChanged && double.IsFinite(_lastTime) && _lastSpeed > 0) Render(_lastTime, _lastSpeed, _lastFarZ);
        }

        private static float Fraction(float percent, float fallback, float maximum)
        {
            return float.IsFinite(percent) ? Mathf.Clamp(percent * .01f, 0, maximum) : fallback;
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
            _lastSpeed = speed; _lastFarZ = farZ;
            if (visualTime < _lastTime) _first = 0;
            _lastTime = visualTime;
            _vertices.Clear(); _uv.Clear(); _fade.Clear(); _state.Clear(); _waves.Clear(); _envelopes.Clear(); _colors.Clear(); _triangles.Clear();
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
                color.a = missed ? .12f : held ? _heldOpacity : _waitingOpacity;
                AddInterval(sustain, i, held ? visualTime : Math.Max(visualTime, sustain.StartTime),
                    Math.Min(visibleEnd, sustain.EndTime), visualTime, speed, color, held, missed);
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices); _mesh.SetUVs(0, _uv); _mesh.SetUVs(1, _fade); _mesh.SetUVs(2, _state);
            _mesh.SetUVs(3, _waves);
            _mesh.SetUVs(4, _envelopes);
            _mesh.SetColors(_colors); _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
            _material.SetFloat("_VisualTime", (float)visualTime);
            _material.SetFloat("_FarZ", farZ);
        }

        private void AddInterval(EliteHiHatSustain sustain, int index, double start, double end,
            double now, float speed, Color color, bool held, bool missed)
        {
            if (end <= start) return;
            double duration = Math.Max(sustain.EndTime - sustain.StartTime, .000001);
            bool pedal = _pedalOrigins[index];
            double offset = _sync == null ? 0 : sustain.StartTime - _sync.TickToTime(sustain.StartTick);
            double front = held ? now : sustain.StartTime;
            double origin = QuarterNote(front);
            // A separate continuous clock avoids jumps when the fractional
            // quarter-note pulse wraps. One wave spacing takes one quarter.
            double slowPhase = QuarterNote(now);
            slowPhase -= Math.Floor(slowPhase);
            float expansion = pedal ? 1 : 0;
            if (!pedal)
            {
                var source = _expansions[index];
                if (!source.IsValid && _carriedExpansion.Contains(index)) source = _carriedExpansion;
                if (source.IsValid)
                    expansion = source.Immediate ? 1 : Mathf.SmoothStep(0, 1,
                        (float)((QuarterNote(_engine.CurrentTime) - QuarterNote(source.OriginTime)) * 4));
                else if (held)
                    // A direct visual reconfiguration can retain the engine's active
                    // interval, but must never reconstruct cleared future inheritance.
                    expansion = Mathf.SmoothStep(0, 1,
                        (float)((QuarterNote(_engine.CurrentTime) - QuarterNote(sustain.StartTime)) * 4));
            }
            int ending = sustain.HasFade ? 0 : pedal ? 2 : 1;
            double fadeDuration = sustain.EndTime - sustain.FadeStartTime;
            if (ending == 1)
            {
                // Isolated open hats get only a tiny soft edge at a clean cut.
                uint ticks = _sync == null ? 1 : Math.Max(1u, _sync.Resolution / 16);
                uint fadeTick = Math.Max(sustain.StartTick, sustain.EndTick - Math.Min(sustain.EndTick, ticks));
                fadeDuration = _sync == null ? Math.Min(.03, sustain.EndTime - sustain.StartTime)
                    : _sync.TickToTime(sustain.EndTick) - _sync.TickToTime(fadeTick);
            }
            _rows.Clear(); _rows.Add(start);
            if (_sync != null)
            {
                foreach (var tempo in _sync.Tempos)
                {
                    double boundary = tempo.Time + offset;
                    if (boundary >= end) break;
                    if (boundary > start) _rows.Add(boundary);
                }
            }
            _rows.Add(end);
            int first = _vertices.Count;
            // The highway bends vertices across X. Subdivide broad pedal tails so
            // their triangles follow that surface instead of cutting beneath it.
            int sections = Math.Max(1, Mathf.CeilToInt(_width / .06f));
            var (head, tail) = _rollOffsets?[index] ?? default;
            // Put a mesh column exactly on the bend between unequal neighbors.
            // Interpolating across that bend would move the wave axis off the fret.
            int axisSection = Mathf.RoundToInt(_headUV * sections);
            for (int i = 0; i <= sections; i++)
            {
                float u = (float)i / sections;
                if (tail != Vector3.zero && axisSection > 0 && axisSection < sections)
                    u = i <= axisSection ? _headUV * i / axisSection :
                        Mathf.Lerp(_headUV, 1, (float)(i - axisSection) / (sections - axisSection));
                // Unequal neighboring fret widths put the hi-hat off-center
                // in its pedal footprint. Keep the wave axis on its receptor.
                float tailOffset = u < _headUV ? Mathf.Lerp(tail.x, tail.y, Mathf.InverseLerp(0, _headUV, u))
                    : Mathf.Lerp(tail.y, tail.z, Mathf.InverseLerp(_headUV, 1, u));
                float remainingOffset = (Mathf.Lerp(head.x, head.y, u) - tailOffset) * (1f - expansion);
                foreach (double time in _rows) Add(u - .5f, tailOffset, remainingOffset, time);
                if (i == 0) continue;
                for (int row = 0; row < _rows.Count - 1; row++)
                {
                    int previous = first + (i - 1) * _rows.Count + row;
                    int next = previous + _rows.Count;
                    _triangles.Add(previous); _triangles.Add(previous + 1); _triangles.Add(next);
                    _triangles.Add(next); _triangles.Add(previous + 1); _triangles.Add(next + 1);
                }
            }
            return;

            double QuarterNote(double time)
            {
                if (_sync == null) return 0;
                // The public time-to-tick helpers round to integer MIDI ticks.
                // Interpolate within the tempo segment for smooth visual motion.
                double chartTime = time - offset;
                var tempo = _sync.Tempos.LowerBoundElement(chartTime) ?? _sync.Tempos[0];
                return tempo.Tick / (double)_sync.Resolution + (chartTime - tempo.Time) / tempo.SecondsPerBeat;
            }

            void Add(float side, float tailOffset, float remainingOffset, double time)
            {
                float along = (float)((time - sustain.StartTime) / duration);
                float z = TrackPlayer.STRIKE_LINE_POS + (float)(time - now) * speed;
                // The ringing tail opens from its own gem to the arrival-time
                // pedal footprint. A splash starts at the full footprint.
                _vertices.Add(new Vector3(side * _width + tailOffset + remainingOffset * (1f - along), 0, z));
                _uv.Add(new Vector2(side + .5f, (float)time));
                // Leave this unclamped: per-pixel clamping keeps the body opaque
                // until the actual fade boundary, even with only two mesh rows.
                float fade = ending == 2 || fadeDuration <= 0 ? 1 : (float)((sustain.EndTime - time) / fadeDuration);
                _fade.Add(new Vector2(fade, held ? 1 : 0));
                _state.Add(new Vector2(missed ? 1 : 0, pedal ? 1 : 0));
                _waves.Add(new Vector4((float)(QuarterNote(time) - origin), (float)slowPhase,
                    (float)(sustain.EndTime - time) * speed, (float)(time - front) * speed));
                // Full authored endpoints keep the waiting flare stable when the
                // visible geometry is clipped or its held front follows the fret.
                _envelopes.Add(new Vector3(expansion, ending, along));
                _colors.Add(color);
            }
        }

        public void Clear(bool resetActivationTimes = true)
        {
            _mesh?.Clear();
            if (resetActivationTimes)
            {
                Array.Clear(_expansions, 0, _expansions.Length);
                _carriedExpansion = default;
            }
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
