// pattern: Functional Core
using System;
using System.Collections.Generic;
using YARG.Core.Chart;
using YARG.Core.Extensions;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Chart-time warning intervals for passages with explicit 2× kick gems.</summary>
    internal sealed class EliteDoubleKickWarningPlan
    {
        private readonly struct WarningWindow
        {
            internal readonly double LeadStart;
            internal readonly double ThroughStart;
            internal readonly double FirstKick;
            internal readonly double End;

            internal WarningWindow(double leadStart, double throughStart, double firstKick, double end) =>
                (LeadStart, ThroughStart, FirstKick, End) = (leadStart, throughStart, firstKick, end);
        }

        private readonly WarningWindow[] _windows;
        private readonly List<(double Time, bool IsKick)> _markers = new();

        internal static EliteDoubleKickWarningPlan Empty { get; } = new(Array.Empty<WarningWindow>());

        private EliteDoubleKickWarningPlan(WarningWindow[] windows) => _windows = windows;

        internal static EliteDoubleKickWarningPlan FromChart(InstrumentDifficulty<EliteDrumNote> prepared,
            SyncTrack sync, IReadOnlyList<uint> pairedKickTicks = null, bool extraKicksEnabled = true)
        {
            if (!extraKicksEnabled || prepared.Notes.Count == 0 || sync.Resolution == 0 ||
                sync.Tempos.Count == 0) return Empty;

            var pairs = pairedKickTicks is { Count: > 0 } ? new HashSet<uint>(pairedKickTicks) : null;
            var measureLines = sync.StrongBeatlines.FindAll(beat => beat.Type == BeatlineType.Measure);
            var windows = new List<WarningWindow>();
            double leadStart = 0, throughStart = 0, firstKick = 0;
            long endTick = -1;
            foreach (var chord in prepared.Notes)
            {
                if (chord.Tick < endTick || chord.IsBigRockEnding) continue;
                foreach (var note in chord.AllNotes)
                {
                    if (note.Pad != (int) EliteDrumPad.Kick || note.IsInvisibleTerminator || note.IsBigRockEnding)
                        continue;
                    if (!note.IsDoubleKick && !(note.IsKickFlam && pairs?.Contains(note.Tick) == true))
                        continue;

                    var measure = FindMeasure(sync, measureLines, note.Tick);
                    if (measure.Start > endTick)
                    {
                        if (endTick >= 0)
                            windows.Add(new WarningWindow(leadStart, throughStart, firstKick, TimeAtTick(sync, endTick)));
                        leadStart = FindLeadStart(sync, note.Tick, measure.Start);
                        throughStart = Math.Min(leadStart, TimeAtTick(sync, measure.Start));
                        firstKick = note.Time;
                    }
                    endTick = measure.End;
                    break;
                }
            }
            if (endTick < 0) return Empty;
            windows.Add(new WarningWindow(leadStart, throughStart, firstKick, TimeAtTick(sync, endTick)));
            var plan = new EliteDoubleKickWarningPlan(windows.ToArray());
            plan.BuildEmberMarkers(prepared, sync);
            return plan;
        }

        internal bool IsActive(double chartTime, bool throughMeasures)
        {
            if (!double.IsFinite(chartTime)) return false;
            SearchComparison<WarningWindow, double> compareStart = throughMeasures
                ? static (window, time) => window.ThroughStart.CompareTo(time)
                : static (window, time) => window.LeadStart.CompareTo(time);
            int index = _windows.UpperBound(chartTime, compareStart) - 1;
            if (index < 0) return false;
            var window = _windows[index];
            return chartTime < (throughMeasures ? window.End : window.FirstKick);
        }

        // Return only the portions occupying the visible highway. The elapsed part of an
        // active warning ends at the strike line, even immediately after a practice seek.
        internal void GetVisibleWindows(double now, double horizon, bool throughMeasures,
            List<(double Start, double End)> visible)
        {
            visible.Clear();
            if (_windows.Length == 0 || !(horizon > now) || !double.IsFinite(now) || !double.IsFinite(horizon)) return;

            SearchComparison<WarningWindow, double> compareEnd = throughMeasures
                ? static (window, time) => window.End.CompareTo(time)
                : static (window, time) => window.FirstKick.CompareTo(time);
            for (int i = _windows.UpperBound(now, compareEnd); i < _windows.Length; i++)
            {
                var window = _windows[i];
                double start = throughMeasures ? window.ThroughStart : window.LeadStart;
                if (start >= horizon) break;
                double end = throughMeasures ? window.End : window.FirstKick;
                if (end > start) visible.Add((Math.Max(start, now), Math.Min(end, horizon)));
            }
        }

        private void BuildEmberMarkers(InstrumentDifficulty<EliteDrumNote> prepared, SyncTrack sync)
        {
            const int MARKERS_PER_QUARTER = 8;
            var byTick = new SortedDictionary<long, (double Time, bool IsKick)>();
            foreach (var window in _windows)
            {
                var firstTempo = sync.Tempos[0];
                double startTick = window.ThroughStart < 0
                    ? firstTempo.Tick + (window.ThroughStart - firstTempo.Time) * sync.Resolution / firstTempo.SecondsPerBeat
                    : sync.TimeToTick(window.ThroughStart);
                long firstMarker = (long) Math.Floor(startTick * MARKERS_PER_QUARTER / sync.Resolution);
                for (long i = firstMarker; ; i++)
                {
                    long tick = (long) Math.Round(i * ((double) sync.Resolution / MARKERS_PER_QUARTER), MidpointRounding.AwayFromZero);
                    double time = TimeAtTick(sync, tick);
                    if (time >= window.End) break;
                    if (time >= window.ThroughStart) byTick[tick] = (time, false);
                }
            }

            // Use the prepared chart, including kicks inside chords. A kick on the grid
            // replaces its smaller marker. Off-grid kicks keep their own arrival times.
            foreach (var chord in prepared.Notes)
            {
                if (chord.IsBigRockEnding) continue;
                foreach (var note in chord.AllNotes)
                {
                    if (note.Pad == (int) EliteDrumPad.Kick && !note.IsInvisibleTerminator &&
                        !note.IsBigRockEnding && IsActive(note.Time, true))
                        byTick[note.Tick] = (note.Time, true);
                }
            }
            _markers.AddRange(byTick.Values);
        }

        private static readonly SearchComparison<(double Time, bool IsKick), double> _compareMarkerTime =
            static (marker, time) => marker.Time.CompareTo(time);

        internal void GetEmberMarkers(List<(double Start, double End)> visible,
            List<(double Time, bool IsKick)> markers)
        {
            markers.Clear();
            if (_markers.Count == 0) return;
            foreach (var (start, end) in visible)
            {
                int i = _markers.LowerBound(start, _compareMarkerTime, before: false);
                while (i < _markers.Count && _markers[i].Time < end)
                    markers.Add(_markers[i++]);
            }
        }

        internal float BeaterKickPulse(double now, bool throughMeasures)
        {
            const double DECAY_SECONDS = .12;
            for (int i = _markers.UpperBound(now, _compareMarkerTime) - 1; i >= 0; i--)
            {
                var marker = _markers[i];
                double age = now - marker.Time;
                if (age >= DECAY_SECONDS) break;
                if (marker.IsKick && IsActive(marker.Time, throughMeasures))
                    return (float) (1 - age / DECAY_SECONDS);
            }
            return 0;
        }

        private static (long Start, long End) FindMeasure(SyncTrack sync, List<Beatline> measureLines, uint tick)
        {
            int anchor = measureLines.LowerBound(tick);
            if (anchor >= 0 && anchor + 1 < measureLines.Count)
                return (measureLines[anchor].Tick, measureLines[anchor + 1].Tick);

            // The loaded beat track can end before the final measure boundary.
            if (sync.TimeSignatures.Count != 0)
            {
                var measure = (uint) Math.Floor(sync.GetMeasurePosition(tick));
                var start = sync.MeasureTickToQuarterTick(measure * sync.MeasureResolution);
                var end = sync.MeasureTickToQuarterTick((measure + 1) * sync.MeasureResolution);
                if (start <= tick && tick < end) return (start, end);
            }

            long measureLength = 4L * sync.Resolution;
            long fallbackStart = tick / measureLength * measureLength;
            return (fallbackStart, fallbackStart + measureLength);
        }

        private static double FindLeadStart(SyncTrack sync, uint tick, long measureStart)
        {
            var beats = sync.StrongBeatlines;
            int anchor = beats.LowerBound(tick);
            var timeSignature = sync.TimeSignatures.LowerBoundElement(tick);
            long beatLength = timeSignature == null ? sync.Resolution : Math.Max(1L,
                (long) timeSignature.GetTicksPerDenominatorBeat(sync.Resolution) *
                Math.Max(1u, timeSignature.Denominator / TimeSignatureChange.QUARTER_NOTE_DENOMINATOR));

            if (anchor >= 0)
            {
                // Extrapolate beyond a short beat track with the active meter.
                long extraBeats = anchor == beats.Count - 1 ? (tick - beats[anchor].Tick) / beatLength : 0;
                if (extraBeats > 0)
                    return TimeAtTick(sync, beats[anchor].Tick + (extraBeats - 1) * beatLength);
                if (anchor > 0) return beats[anchor - 1].Time;
                return TimeAtTick(sync, beats[0].Tick - beatLength);
            }

            long beatsIntoMeasure = (tick - measureStart) / beatLength;
            return TimeAtTick(sync, measureStart + (beatsIntoMeasure - 1) * beatLength);
        }

        private static double TimeAtTick(SyncTrack sync, long tick)
        {
            if (tick >= 0 && tick <= uint.MaxValue) return sync.TickToTime((uint) tick);
            var tempo = tick < 0 ? sync.Tempos[0] : sync.Tempos[^1];
            return tempo.Time + (tick - tempo.Tick) * tempo.SecondsPerBeat / sync.Resolution;
        }
    }
}
