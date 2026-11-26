// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using YARG.Core.Game;
using YARG.Core.Chart;
using YARG.Gameplay.Player;
using static YARG.Gameplay.Visuals.EliteDrumsRollLayout;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Draws upcoming arrival-time fret seams on the scrolling highway.</summary>
    internal sealed class EliteFretBoundaryDotVisual : MonoBehaviour
    {
        private const float DOT_HEIGHT = .004f;
        private const float DOT_HALF_WIDTH = .034f;
        private const float DOT_HALF_DEPTH = .066f;
        private const double TIME_TOLERANCE = 1e-8;
        private const int MAX_NORMAL_ROWS_PER_FRAME = 512;

        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<Color32> _tints = new();
        private readonly List<int> _indices = new();
        private BoundaryDotRow[] _rows = Array.Empty<BoundaryDotRow>();
        private BoundaryDot[] _normalDots = Array.Empty<BoundaryDot>();
        private Color32[] _fretTints = Array.Empty<Color32>();
        private bool _colorAllCenters;
        private EliteDrumsRollLayout _layout;
        private double _quarterSpacing;
        private Mesh _mesh;
        private Material _material;

        internal (Color32 Left, Color32 Right, bool Paired)[] SplitTints { get; set; }
        internal bool CymbalOnLeft { get; set; }
        internal Func<EliteDrumNote, Color32> CymbalCueTint { get; set; }
        internal bool Diamond { get; set; }
        internal float SmallScale { get; set; } = 1f;
        internal bool MarkCollisionFrets { get; set; }
        internal float SizeScale { get; set; } = 1f;

        internal static EliteFretBoundaryDotVisual CreateFixedGrid(Transform parent,
            YargProfile profile, SyncTrack sync,
            IReadOnlyList<GuitarNote> notes, Dictionary<int, int> ordering,
            Func<int, Color32> fretColor)
        {
            var root = new GameObject("Fret timing dots") { layer = parent.gameObject.layer };
            root.transform.SetParent(parent, false);
            var visual = root.AddComponent<EliteFretBoundaryDotVisual>();
            visual.Diamond = profile.DiamondFretDots;
            visual.SizeScale = Mathf.Clamp(profile.FretDotSizePercent, 25, 200) / 100f;
            visual.SmallScale = visual.SizeScale;
            var colors = new Color32[ordering.Count];
            foreach (var pair in ordering) colors[pair.Value] = fretColor(pair.Key);
            var heads = new List<(double, int)>();
            if (profile.ColoredFretDots == true)
            {
                foreach (var chord in notes)
                {
                    if (chord.IsBigRockEnding) continue;
                    int mask = 0;
                    foreach (var note in chord.AllNotes)
                        mask |= ordering.TryGetValue(note.Fret, out int column) ? 1 << column : (1 << ordering.Count) - 1;
                    heads.Add((chord.Time, mask));
                }
            }
            int spacing = profile.FretDotSubdivision ?? 32;
            visual.SetRows(new EliteDrumsRollLayout(ordering.Count, sync, heads.ToArray()),
                spacing is 8 or 16 or 32 or 64 or 128 ? spacing : 32, true, false, profile.ColoredFretDots == true, colors);
            return visual;
        }

        internal void SetRows(EliteDrumsRollLayout layout, int subdivision, bool alwaysShow, bool colorHeldCenters,
            bool colorAllCenters, Color32[] fretTints)
        {
            _layout = layout;
            _quarterSpacing = 4d / subdivision;
            _rows = layout.GetBoundaryDotRows(_quarterSpacing, alwaysShow, colorHeldCenters, colorAllCenters);
            _normalDots = alwaysShow ? layout.GetNormalBoundaryDots(colorAllCenters) : Array.Empty<BoundaryDot>();
            _fretTints = fretTints ?? Array.Empty<Color32>();
            _colorAllCenters = colorAllCenters;
            Clear();
            if (_mesh != null || (_rows.Length == 0 && _normalDots.Length == 0)) return;

            _mesh = new Mesh { name = "Elite fret boundary dots", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _material = new Material(Resources.Load<Shader>("EliteFretBoundaryDots"));
            _material.SetFloat("_Diamond", Diamond ? 1f : 0f);
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = _material;
        }

        internal void Render(double visualTime, float speed, float farZ)
        {
            if (_mesh == null) return;
            double visibleEnd = visualTime + (farZ - TrackPlayer.STRIKE_LINE_POS) / speed;
            if (speed <= 0 || !float.IsFinite(speed) || !double.IsFinite(visualTime) || !double.IsFinite(visibleEnd))
            {
                Clear();
                return;
            }

            _vertices.Clear();
            _uvs.Clear();
            _tints.Clear();
            _indices.Clear();
            AddVisibleRows(visualTime, speed, visibleEnd);

            Clear();
            if (_vertices.Count == 0) return;
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_tints);
            _mesh.SetTriangles(_indices, 0);
        }

        private void AddVisibleRows(double visualTime, float speed, double visibleEnd)
        {
            int index = FirstRowAtOrAfter(visualTime - TIME_TOLERANCE);
            bool drawGrid = _normalDots.Length != 0;
            // Only generate the visible part of the normal grid, even at 1/128.
            double gridQuarter = drawGrid
                ? Math.Ceiling(_layout.QuarterAt(visualTime - TIME_TOLERANCE) / _quarterSpacing) * _quarterSpacing : 0;
            // Near-zero note speeds can place thousands of overlapping normal rows
            // on screen. Keep a subset on the original grid in that case.
            double gridStep = drawGrid ? _quarterSpacing * Math.Max(1, Math.Ceiling(
                (_layout.QuarterAt(visibleEnd) - gridQuarter) / (_quarterSpacing * (MAX_NORMAL_ROWS_PER_FRAME - 1)))) : 0;
            double gridTime = drawGrid ? _layout.TimeAtQuarter(gridQuarter) : double.PositiveInfinity;
            while (true)
            {
                double scheduledTime = index < _rows.Length ? _rows[index].Time : double.PositiveInfinity;
                if (!(Math.Min(scheduledTime, gridTime) <= visibleEnd)) break;

                if (scheduledTime <= gridTime + TIME_TOLERANCE)
                {
                    AddRow(scheduledTime, _rows[index++].Dots);
                    // Scheduled rows already include the actual seams for this subdivision.
                    if (scheduledTime < gridTime - TIME_TOLERANCE) continue;
                }
                else
                {
                    int occupied = _colorAllCenters ? _layout.GetOccupiedCenterColumns(gridTime) : 0;
                    AddRow(gridTime, _normalDots, occupied);
                }
                gridQuarter += gridStep;
                gridTime = _layout.TimeAtQuarter(gridQuarter);
            }

            void AddRow(double time, BoundaryDot[] dots, int occupied = 0)
            {
                float z = TrackPlayer.STRIKE_LINE_POS + (float)(time - visualTime) * speed;
                foreach (var dot in dots)
                {
                    if (dot.ColorColumn >= 0 && (occupied & (1 << dot.ColorColumn)) != 0) continue;
                    if (SmallScale == 0f && !dot.CollisionFret) continue;
                    float progress = MarkCollisionFrets ? (dot.CollisionFret ? 1f : 0f) : dot.Expansion;
                    float scale = Mathf.Lerp(SmallScale, SizeScale, progress);
                    if (scale <= 0f) continue;
                    if (dot.SplitOffset > 0 && SplitTints != null && SplitTints[dot.ColorColumn].Paired)
                    {
                        var pair = SplitTints[dot.ColorColumn];
                        if (dot.CymbalCue != null)
                        {
                            if (CymbalOnLeft) pair.Left = CymbalCueTint(dot.CymbalCue);
                            else pair.Right = CymbalCueTint(dot.CymbalCue);
                        }
                        AddDot(dot.X - dot.SplitOffset, z, scale, pair.Left);
                        AddDot(dot.X + dot.SplitOffset, z, scale, pair.Right);
                    }
                    else AddDot(dot.X, z, scale, dot.ColorColumn < 0 ? default : _fretTints[dot.ColorColumn]);
                }
            }
        }

        private int FirstRowAtOrAfter(double time)
        {
            int low = 0, high = _rows.Length;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (_rows[mid].Time < time) low = mid + 1;
                else high = mid;
            }
            return low;
        }

        private void AddDot(float x, float z, float scale, Color32 tint)
        {
            int first = _vertices.Count;
            _vertices.Add(new Vector3(x - DOT_HALF_WIDTH * scale, DOT_HEIGHT, z - DOT_HALF_DEPTH * scale));
            _vertices.Add(new Vector3(x - DOT_HALF_WIDTH * scale, DOT_HEIGHT, z + DOT_HALF_DEPTH * scale));
            _vertices.Add(new Vector3(x + DOT_HALF_WIDTH * scale, DOT_HEIGHT, z + DOT_HALF_DEPTH * scale));
            _vertices.Add(new Vector3(x + DOT_HALF_WIDTH * scale, DOT_HEIGHT, z - DOT_HALF_DEPTH * scale));
            _uvs.Add(new Vector2(-1, -1));
            _uvs.Add(new Vector2(-1, 1));
            _uvs.Add(new Vector2(1, 1));
            _uvs.Add(new Vector2(1, -1));
            // The tint alpha selects the neutral shader color when zero.
            for (int i = 0; i < 4; i++) _tints.Add(tint);
            _indices.Add(first);
            _indices.Add(first + 1);
            _indices.Add(first + 2);
            _indices.Add(first);
            _indices.Add(first + 2);
            _indices.Add(first + 3);
        }

        internal void Clear()
        {
            if (_mesh != null && _mesh.vertexCount > 0) _mesh.Clear();
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
