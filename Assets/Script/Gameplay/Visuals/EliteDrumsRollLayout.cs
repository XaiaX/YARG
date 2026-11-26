// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Gameplay.Player;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Arrival-time layout shared by frets, strips, hand notes and kick rings.</summary>
    internal sealed class EliteDrumsRollLayout
    {
        private const int MERGE_GAP_QUARTERS = 3; // One dotted half at normal fret width.
        private const double QUARTER_TOLERANCE = 1e-8;
        private const double HEAD_TIME_TOLERANCE = 1e-8;

        internal sealed class Strip
        {
            internal readonly EliteDrumsPlayableLaneSegment Segment;
            internal readonly int Receptor;
            internal readonly int Column;
            internal readonly bool FullWidth;
            internal float Center, Width;
            internal int SideIndex, SideCount = 1;
            internal PathPoint[] Path;

            internal Strip(EliteDrumsPlayableLaneSegment segment, int receptor, int column, bool fullWidth)
            {
                Segment = segment;
                Receptor = receptor;
                Column = column;
                FullWidth = fullWidth;
            }
        }

        internal readonly struct PathPoint
        {
            internal readonly float Fraction, Center, Width;
            internal PathPoint(float fraction, float center, float width) =>
                (Fraction, Center, Width) = (fraction, center, width);
        }

        internal readonly struct BoundaryDot
        {
            internal readonly float X;
            internal readonly int ColorColumn;

            internal readonly EliteDrumNote CymbalCue;
            internal readonly float SplitOffset;
            internal readonly float Expansion;
            internal readonly bool CollisionFret;
            internal BoundaryDot(float x, int colorColumn = -1, float expansion = 0f, bool collisionFret = false, float splitOffset = 0f, EliteDrumNote cymbalCue = null) =>
                (X, ColorColumn, Expansion, CollisionFret, SplitOffset, CymbalCue) = (x, colorColumn, expansion, collisionFret, splitOffset, cymbalCue);
        }

        internal readonly struct BoundaryDotRow
        {
            internal readonly double Time;
            internal readonly BoundaryDot[] Dots;

            internal BoundaryDotRow(double time, BoundaryDot[] dots) =>
                (Time, Dots) = (time, dots);
        }

        private sealed class Window
        {
            internal List<EliteDrumNote> Cymbals;
            internal double Begin, Full, Release, End; // quarter-note coordinates
        }

        private readonly Dictionary<EliteDrumNote, List<Strip>> _starts = new();
        private readonly Dictionary<EliteDrumNote, (float Center, float Width)> _gemSlots = new();
        private readonly List<Window>[] _windows;
        private readonly IReadOnlyList<EliteDrumNote> _notes;
        private readonly Func<EliteDrumNote, int> _centerDotFootprint;
        private readonly SyncTrack _sync;
        private readonly float[] _edges;
        private readonly int _columns;
        private readonly float[] _restingEdges;
        private readonly float _highwayWidth = TrackPlayer.TRACK_WIDTH;
        private double _sampleTime = double.NaN;
        private bool _deformed;
        private (double Time, int Columns)[] _noteHeads;
        internal bool HasExpansion { get; }

        internal bool TryGetStarts(EliteDrumNote parent, out List<Strip> strips) =>
            _starts.TryGetValue(parent, out strips);

        internal bool TryGetSlot(EliteDrumNote note, out (float Center, float Width) slot) =>
            _gemSlots.TryGetValue(note, out slot);

        internal EliteDrumsRollLayout(IEnumerable<Strip> source, IReadOnlyList<EliteDrumNote> notes,
            int columns, bool lefty, Func<EliteDrumNote, int> receptorOf,
            Func<EliteDrumNote, EliteDrumNote> collisionPartner, Func<int, int> columnOf,
            SyncTrack sync, Func<EliteDrumNote, int> centerDotFootprint, bool dynamicWidths = true,
            bool expandFretsForFlams = false, bool usesKickBars = true, float[] restingEdges = null,
            float highwayWidth = TrackPlayer.TRACK_WIDTH, bool fitGems = false)
        {
            _columns = columns;
            _restingEdges = restingEdges;
            _highwayWidth = highwayWidth;
            _notes = notes;
            _centerDotFootprint = centerDotFootprint;
            _sync = sync;
            _edges = new float[columns + 1];
            _windows = new List<Window>[columns];
            var strips = source.OrderBy(strip => strip.Segment.StartTime).ToArray();
            var shared = new List<Strip[]>();
            var reservations = new List<(int Column, EliteDrumNote First, EliteDrumNote Last)>();
            foreach (var column in strips.Where(strip => !strip.FullWidth).GroupBy(strip => strip.Column))
            {
                var run = new List<Strip>();
                double end = double.NegativeInfinity;
                foreach (var strip in column)
                {
                    if (strip.Segment.StartTime >= end)
                    {
                        Reserve(run);
                        run.Clear();
                        end = double.NegativeInfinity;
                    }
                    run.Add(strip);
                    end = Math.Max(end, strip.Segment.EndTime);
                }
                Reserve(run);
            }

            var flams = new List<(EliteDrumNote Note, int Column)>();
            var pairs = new List<(EliteDrumNote Drum, EliteDrumNote Cymbal, int Column)>();
            if (columns > 2 && (dynamicWidths || fitGems))
            {
                foreach (var chord in notes)
                {
                    if (dynamicWidths && expandFretsForFlams)
                    {
                        foreach (var head in chord.AllNotes)
                        {
                            if (head.IsInvisibleTerminator || !(head.IsFlam || head.IsFlatFlam)) continue;
                            bool kickFret = !usesKickBars && EliteDrumsPlayer.IsFootPad(head.Pad);
                            if (!kickFret && !EliteDrumsNoteElement.IsHandFlamPad(head.Pad)) continue;
                            int column = columnOf(receptorOf(head));
                            // Dedicated kicks already fit at the arrival center.
                            if (!kickFret) flams.Add((head, column));
                            reservations.Add((column, chord, chord));
                        }
                    }
                    if (chord.ChildNotes.Count == 0) continue;
                    foreach (var drum in chord.AllNotes)
                    {
                        // Visit each physical pair once, including chords that collide on two frets.
                        if (drum.IsInvisibleTerminator || EliteDrumsPlayer.IsCymbal(drum.Pad)) continue;
                        var cymbal = collisionPartner(drum);
                        if (cymbal == null) continue;
                        int column = columnOf(receptorOf(drum));
                        pairs.Add((drum, cymbal, column));
                        if (dynamicWidths) reservations.Add((column, chord, chord));
                    }
                }
            }

            HasExpansion = reservations.Count != 0;
            foreach (var reservation in reservations.OrderBy(r => r.First.Time))
            {
                var window = PlanWindow(reservation.First, reservation.Last);
                var windows = _windows[reservation.Column] ??= new List<Window>();
                if (windows.Count > 0 && window.Begin <= windows[^1].End + MERGE_GAP_QUARTERS)
                {
                    // A chord inside an existing roll must preserve the roll's later return.
                    windows[^1].Release = Math.Max(windows[^1].Release, window.Release);
                    windows[^1].End = Math.Max(windows[^1].End, window.End);
                }
                else windows.Add(window);
            }

            List<double> transitionTimes = null;
            foreach (var strip in strips)
            {
                (strip.Center, strip.Width) = StripAt(strip, strip.Segment.StartTime);
                if (!strip.FullWidth && HasExpansion)
                    strip.Path = GetPath(strip, transitionTimes ??= GetTransitionTimes());
                var parent = strip.Segment.Members[0].ParentOrSelf;
                if (!_starts.TryGetValue(parent, out var starts))
                    _starts.Add(parent, starts = new List<Strip>());
                starts.Add(strip);
            }

            foreach (var group in shared)
            {
                var roles = group.GroupBy(s => s.Receptor).ToDictionary(g => g.Key, g => g.First());
                double end = group.Max(s => s.Segment.EndTime);
                for (int i = FirstOnset(notes, group[0].Segment.StartTime); i < notes.Count && notes[i].Time <= end; i++)
                    foreach (var note in notes[i].AllNotes)
                        if (!note.IsInvisibleTerminator && roles.TryGetValue(receptorOf(note), out var strip))
                            _gemSlots[note] = StripAt(strip, note.Time);
            }

            foreach (var (drum, cymbal, column) in pairs)
            {
                Sample(drum.ParentOrSelf.Time);
                _gemSlots.TryAdd(drum, Slot(column, lefty ? 1 : 0, 2));
                _gemSlots.TryAdd(cymbal, Slot(column, lefty ? 0 : 1, 2));
            }

            // A concurrent roll or collision already owns its half of the fret.
            foreach (var (head, column) in flams)
            {
                Sample(head.Time);
                _gemSlots.TryAdd(head, Slot(column, 0, 1));
            }

            void Reserve(List<Strip> run)
            {
                if (run.Count < 2) return;
                var voices = run.GroupBy(s => s.Receptor)
                    .OrderBy(g => EliteDrumsPlayer.IsCymbal(g.First().Segment.Pad) != lefty)
                    .ThenBy(g => lefty ? -g.Key : g.Key).ToArray();
                if (voices.Length < 2) return;
                shared.Add(run.ToArray());
                for (int i = 0; i < voices.Length; i++)
                    foreach (var strip in voices[i])
                    {
                        strip.SideIndex = i;
                        strip.SideCount = voices.Length;
                    }

                if (!dynamicWidths || columns <= 2) return; // Two half-width heads already fit at the usual width.
                var first = run[0].Segment.Members[0];
                var last = run.Select(s => s.Segment.Members[^1]).OrderBy(n => n.Time).Last();
                reservations.Add((run[0].Column, first, last));
            }
        }

        internal EliteDrumsRollLayout(int columns, SyncTrack sync, (double Time, int Columns)[] heads)
        {
            _columns = columns;
            _sync = sync;
            _noteHeads = heads;
        }

        private Window PlanWindow(EliteDrumNote first, EliteDrumNote last)
        {
            const int QUARTERS_PER_HALF_NOTE = 2;
            double full, release;
            if (_sync != null && _sync.Resolution > 0 && _sync.Tempos.Count > 0)
            {
                var beats = _sync.StrongBeatlines;
                long halfTicks = (long)QUARTERS_PER_HALF_NOTE * _sync.Resolution;
                long safeTick = (long)first.Tick - halfTicks;
                var approachBeat = safeTick >= 0 ? beats.LowerBoundElement((uint)safeTick) : null;
                full = (approachBeat?.Tick ?? (double)safeTick) / _sync.Resolution;

                // LowerBound includes an exact hit. Otherwise advance to the next strong beat.
                int next = beats.LowerBound(last.Tick);
                if (next < 0 || beats[next].Tick != last.Tick) next++;
                long releaseTick = last.Tick;
                if (next < beats.Count)
                {
                    long beatTick = beats[next].Tick;
                    releaseTick = beatTick - last.Tick >= halfTicks ? beatTick - halfTicks : beatTick;
                }
                release = (double)releaseTick / _sync.Resolution;
            }
            else
            {
                full = QuarterAt(first.Time) - QUARTERS_PER_HALF_NOTE;
                release = QuarterAt(last.Time);
            }
            return new Window
            {
                Begin = full - QUARTERS_PER_HALF_NOTE, Full = full,
                Release = release, End = release + QUARTERS_PER_HALF_NOTE
            };
        }

        // Only gather color cues for automatic colored dots. Keep the last collision's
        // cymbal through contraction, and use the next one during the approach.
        internal void PrepareDotCymbals(Func<EliteDrumNote, int> columnOf)
        {
            if (!HasExpansion) return;
            foreach (var note in _gemSlots.Keys.Where(n => EliteDrumsPlayer.IsCymbal(n.Pad)).OrderBy(n => n.Time))
            {
                var window = FindWindow(_windows[columnOf(note)], QuarterAt(note.Time));
                if (window != null) (window.Cymbals ??= new List<EliteDrumNote>()).Add(note);
            }
        }

        internal double QuarterAt(double time)
        {
            if (_sync == null || _sync.Resolution == 0 || _sync.Tempos.Count == 0) return time * 2;
            var tempo = _sync.Tempos.LowerBoundElement(time) ?? _sync.Tempos[0];
            return (double)tempo.Tick / _sync.Resolution + (time - tempo.Time) / tempo.SecondsPerBeat;
        }

        internal double TimeAtQuarter(double quarter)
        {
            if (_sync == null || _sync.Resolution == 0 || _sync.Tempos.Count == 0) return quarter * .5;
            double tick = quarter * _sync.Resolution;
            var tempo = _sync.Tempos.LowerBoundElement((uint)Math.Clamp(tick, 0, uint.MaxValue)) ?? _sync.Tempos[0];
            return tempo.Time + (quarter - (double)tempo.Tick / _sync.Resolution) * tempo.SecondsPerBeat;
        }

        internal BoundaryDot[] GetNormalBoundaryDots(bool colorAllCenters)
        {
            var dots = new BoundaryDot[colorAllCenters ? _columns : _columns - 1];
            for (int i = 0; i < dots.Length; i++)
                dots[i] = colorAllCenters ? GetCenterDot(i, null) : new BoundaryDot(GetBoundaryDotX(i + 1, null));
            return dots;
        }

        internal BoundaryDotRow[] GetBoundaryDotRows(double quarterSpacing, bool alwaysShow, bool colorHeldCenters,
            bool colorAllCenters)
        {
            if (!HasExpansion) return Array.Empty<BoundaryDotRow>();

            var quarters = new SortedSet<double>();
            foreach (var windows in _windows)
            {
                if (windows == null) continue;
                foreach (var window in windows)
                {
                    quarters.Add(window.Begin);
                    quarters.Add(window.Full);
                    quarters.Add(window.Release);
                    quarters.Add(window.End);
                    for (double quarter = Math.Ceiling(window.Begin / quarterSpacing) * quarterSpacing;
                         quarter < window.End; quarter += quarterSpacing)
                        quarters.Add(quarter);
                }
            }

            var rows = new List<BoundaryDotRow>(quarters.Count);
            var expanded = new bool[_columns];
            var progress = new float[_columns];
            var active = new bool[_columns];
            var cueWindows = new Window[_columns];
            var dots = new List<BoundaryDot>(_columns + 1);
            double previousQuarter = double.NegativeInfinity;
            foreach (double quarter in quarters)
            {
                // An off-grid ramp endpoint can coincide with another ramp's
                // endpoint despite fractional-tick roundoff in quarter coordinates.
                if (quarter - previousQuarter < QUARTER_TOLERANCE) continue;
                previousQuarter = quarter;
                bool moving = false;
                float movingProgress = 0;
                for (int column = 0; column < _columns; column++)
                {
                    var window = FindWindow(_windows[column], quarter, QUARTER_TOLERANCE);
                    expanded[column] = window != null && quarter > window.Full + QUARTER_TOLERANCE &&
                        quarter < window.Release - QUARTER_TOLERANCE;
                    active[column] = window != null;
                    cueWindows[column] = window;
                    progress[column] = (float)LinearExpansion(window, quarter);
                    if (window != null && !expanded[column])
                    {
                        moving = true;
                        movingProgress = Math.Max(movingProgress, progress[column]);
                    }
                }

                double time = TimeAtQuarter(quarter);
                var edges = Sample(time);
                bool allCenters = colorAllCenters && (moving || alwaysShow);
                bool heldCenters = colorHeldCenters && !moving;
                int occupied = allCenters || heldCenters ? GetOccupiedCenterColumns(time) : 0;
                dots.Clear();
                for (int seam = 0; seam <= _columns; seam++)
                {
                    bool interior = seam > 0 && seam < _columns;
                    bool adjacentToExpansion = (seam > 0 && expanded[seam - 1]) ||
                        (seam < _columns && expanded[seam]);
                    if (!allCenters && ((interior && (moving || alwaysShow)) || (!moving && !heldCenters && adjacentToExpansion)))
                    {
                        float fraction = Math.Max(seam > 0 ? progress[seam - 1] : 0, seam < _columns ? progress[seam] : 0);
                        bool bordersWindow = (seam > 0 && active[seam - 1]) || (seam < _columns && active[seam]);
                        dots.Add(new BoundaryDot(GetBoundaryDotX(seam, edges), expansion: bordersWindow ? fraction : movingProgress, collisionFret: bordersWindow));
                    }
                    if (seam < _columns && (allCenters || (heldCenters && expanded[seam])) &&
                        (occupied & (1 << seam)) == 0)
                    {
                        var cues = cueWindows[seam]?.Cymbals;
                        var cue = cues == null ? null : cues[Math.Min(FirstOnset(cues, time), cues.Count - 1)];
                        dots.Add(GetCenterDot(seam, edges, active[seam] ? progress[seam] : movingProgress, active[seam], cue));
                    }
                }
                // Even an entirely occupied row takes precedence over the normal grid.
                if (dots.Count > 0 || alwaysShow) rows.Add(new BoundaryDotRow(time, dots.ToArray()));
            }
            return rows.ToArray();
        }

        internal int GetOccupiedCenterColumns(double time)
        {
            var heads = _noteHeads ??= BuildNoteHeadFootprints();
            // Fractional subdivisions can round to a nearby head's MIDI tick.
            // Match the playable heads' actual arrival times instead.
            double start = time - HEAD_TIME_TOLERANCE, end = time + HEAD_TIME_TOLERANCE;
            int low = 0, high = heads.Length;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (heads[mid].Time < start) low = mid + 1;
                else high = mid;
            }
            int columns = 0;
            for (int i = low; i < heads.Length && heads[i].Time <= end; i++)
                columns |= heads[i].Columns;
            return columns;
        }

        private (double Time, int Columns)[] BuildNoteHeadFootprints()
        {
            var heads = new Dictionary<double, int>();
            foreach (var chord in _notes)
            {
                if (chord.IsBigRockEnding) continue;
                foreach (var note in chord.AllNotes)
                {
                    if (note.IsInvisibleTerminator) continue;
                    int columns = _centerDotFootprint(note);
                    if (columns != 0)
                        heads[note.Time] = heads.GetValueOrDefault(note.Time) | columns;
                }
            }
            return heads.OrderBy(head => head.Key).Select(head => (head.Key, head.Value)).ToArray();
        }

        private BoundaryDot GetCenterDot(int column, float[] edges, float expansion = 0f, bool collisionFret = false, EliteDrumNote cue = null) =>
            new((GetBoundaryDotX(column, edges) + GetBoundaryDotX(column + 1, edges)) * .5f, column, expansion, collisionFret,
                collisionFret ? (GetBoundaryDotX(column + 1, edges) - GetBoundaryDotX(column, edges)) *
                    .25f * expansion * expansion * (3f - 2f * expansion) : 0f, cue);

        private float GetBoundaryDotX(int seam, float[] edges) =>
            ((edges?[seam] ?? RestingEdge(seam)) - .5f) * _highwayWidth;

        private float RestingEdge(int seam) => _restingEdges?[seam] ?? (float)seam / _columns;

        // Only a roll crossing a changing layout needs extra cross-sections.
        // Sampling the easing curves at load time keeps long straight runs cheap.
        private List<double> GetTransitionTimes()
        {
            var times = new SortedSet<double>();
            foreach (var windows in _windows)
                if (windows != null)
                    foreach (var window in windows)
                    {
                        AddRamp(window.Begin, window.Full);
                        AddRamp(window.Release, window.End);
                    }
            return times.ToList();

            void AddRamp(double start, double end)
            {
                const int subdivisions = 8;
                for (int i = 0; i <= subdivisions; i++)
                {
                    times.Add(TimeAtQuarter(start + (end - start) * i / subdivisions));
                }
            }
        }

        private PathPoint[] GetPath(Strip strip, List<double> transitions)
        {
            double start = strip.Segment.StartTime, end = strip.Segment.EndTime;
            int i = transitions.BinarySearch(start);
            i = i < 0 ? ~i : i + 1;
            if (i == transitions.Count || transitions[i] >= end)
            {
                var last = StripAt(strip, end);
                if (last.Center == strip.Center && last.Width == strip.Width) return null;
            }
            var points = new List<PathPoint>();
            Add(start);
            for (; i < transitions.Count && transitions[i] < end; i++) Add(transitions[i]);
            Add(end);
            return points.All(p => Math.Abs(p.Center - strip.Center) < .00001f &&
                Math.Abs(p.Width - strip.Width) < .00001f) ? null : points.ToArray();

            void Add(double time)
            {
                var slot = StripAt(strip, time);
                points.Add(new PathPoint((float)((time - start) / (end - start)), slot.Center, slot.Width));
            }
        }

        private static Window FindWindow(List<Window> windows, double quarter, double tolerance = 0)
        {
            if (windows == null) return null;
            int low = 0, high = windows.Count;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (windows[mid].End < quarter - tolerance) low = mid + 1;
                else high = mid;
            }
            return low == windows.Count || quarter < windows[low].Begin - tolerance ? null : windows[low];
        }

        internal float ExpansionAtQuarter(int column, double quarter) => (float)Weight(_windows[column], quarter);

        private static double Weight(List<Window> windows, double quarter)
        {
            double t = LinearExpansion(FindWindow(windows, quarter), quarter);
            return t * t * (3 - 2 * t);
        }

        private static double LinearExpansion(Window w, double quarter)
        {
            if (w == null) return 0;
            return Math.Clamp(quarter < w.Full ? (quarter - w.Begin) / (w.Full - w.Begin) :
                quarter > w.Release ? (w.End - quarter) / (w.End - w.Release) : 1, 0, 1);
        }

        // Borrow equally from every unexpanded fret. Normalization also accommodates
        // simultaneous shared frets, keeping every width positive and both rails fixed.
        // The returned buffer is borrowed until the next sample.
        internal float[] Sample(double time)
        {
            if (!HasExpansion) return _restingEdges;
            if (time != _sampleTime)
            {
                _sampleTime = time;
                double quarter = QuarterAt(time), total = 0;
                double extra = 2 * EliteDrumsRuntimeGeometry.GetPairWidthFactor(_columns) - 1;
                _deformed = false;
                for (int i = 0; i < _columns; i++)
                {
                    double amount = Weight(_windows[i], quarter);
                    _deformed |= amount > 0;
                    double width = 1 + extra * amount;
                    double demand = width * (_columns - 1) / (_columns - width);
                    total += demand * (RestingEdge(i + 1) - RestingEdge(i));
                    _edges[i + 1] = (float)total;
                }
                if (_deformed)
                    for (int i = 1; i <= _columns; i++) _edges[i] /= (float)total;
            }
            return _deformed ? _edges : _restingEdges;
        }

        internal (float Center, float Width) StripAt(Strip strip, double time)
        {
            Sample(time);
            return Slot(strip.Column, strip.SideIndex, strip.SideCount);
        }

        private (float Center, float Width) Slot(int column, int index, int count)
        {
            float left = _deformed ? _edges[column] : RestingEdge(column);
            float right = _deformed ? _edges[column + 1] : RestingEdge(column + 1);
            float width = (right - left) * _highwayWidth / count;
            return ((left - .5f) * _highwayWidth + (index + .5f) * width, width);
        }

        private static int FirstOnset(IReadOnlyList<EliteDrumNote> notes, double time)
        {
            int low = 0, high = notes.Count;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (notes[mid].Time < time) low = mid + 1;
                else high = mid;
            }
            return low;
        }
    }
}
