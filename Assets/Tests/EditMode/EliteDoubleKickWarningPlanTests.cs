using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Chart;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.EditMode
{
    public sealed class EliteDoubleKickWarningPlanTests
    {
        private const uint RESOLUTION = 480;
        private const double TICKS_PER_SECOND = 960;
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [TestCase(1920u, 2.0)]
        [TestCase(2040u, 2.125)]
        public void LeadStartsOnTheStrongMarkerBeforeTheAnchor(uint firstTick, double firstTime)
        {
            var plan = Build(Timing(), new[] { Kick(firstTick) });

            Assert.That(Active(plan, 1.499), Is.False);
            Assert.That(Active(plan, 1.5), Is.True);
            Assert.That(Active(plan, firstTime - 0.001), Is.True);
            Assert.That(Active(plan, firstTime), Is.False);
            Assert.That(Active(plan, 3.999, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 4, throughMeasures: true), Is.False);
        }

        [Test]
        public void ContiguousMeasuresShareOneLeadAndAnEmptyMeasureRearmsTheNext()
        {
            var plan = Build(Timing(), new[] { Kick(2160), Kick(4080), Kick(6000, extra: false), Kick(7920) });

            Assert.That(Active(plan, 1.5), Is.True);
            Assert.That(Active(plan, 2.25), Is.False);
            Assert.That(Active(plan, 3.75), Is.False, "An adjacent occupied measure shares the original lead.");
            Assert.That(Active(plan, 3.75, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 5.999, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 6, throughMeasures: true), Is.False);
            Assert.That(Active(plan, 7.499), Is.False);
            Assert.That(Active(plan, 7.5), Is.True);
            Assert.That(Active(plan, 8.25), Is.False);
            Assert.That(Active(plan, 9.999, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 10, throughMeasures: true), Is.False);
        }

        [Test]
        public void WholeMeasureModeIncludesTheStartOfAMeasureWithALateFirstKick()
        {
            var plan = Build(Timing(), new[] { Kick(3720), Kick(5520), Kick(9480) });

            Assert.That(Active(plan, 1.999, throughMeasures: true), Is.False);
            Assert.That(Active(plan, 2, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 2.999), Is.False);
            Assert.That(Active(plan, 3), Is.True);
            Assert.That(Active(plan, 3.875), Is.False);
            Assert.That(Active(plan, 4, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 5.75), Is.False);
            Assert.That(Active(plan, 5.999, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 6, throughMeasures: true), Is.False);
            Assert.That(Active(plan, 7.999, throughMeasures: true), Is.False);
            Assert.That(Active(plan, 8, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 8.999), Is.False);
            Assert.That(Active(plan, 9), Is.True);
            Assert.That(Active(plan, 9.875), Is.False);
            Assert.That(Active(plan, 10, throughMeasures: true), Is.False);
        }

        [Test]
        public void AuthoredStrongAndMeasureMarkersTakePrecedenceAndWeakMarkersAreIgnored()
        {
            var timing = Timing(new List<Beatline>
            {
                Beat(BeatlineType.Measure, 0), Beat(BeatlineType.Strong, 480),
                Beat(BeatlineType.Weak, 720), Beat(BeatlineType.Strong, 900),
                Beat(BeatlineType.Measure, 1440), Beat(BeatlineType.Strong, 1920),
                Beat(BeatlineType.Measure, 2880)
            });
            var plan = Build(timing, new[] { Kick(960) });

            Assert.That(Active(plan, 0.499), Is.False);
            Assert.That(Active(plan, 0.5), Is.True);
            Assert.That(Active(plan, 1), Is.False);
            Assert.That(Active(plan, 1.499, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 1.5, throughMeasures: true), Is.False);
        }

        [Test]
        public void MissingBeatMarkersFallBackToMeterAndTempo()
        {
            var plan = Build(Timing(new List<Beatline>(), numerator: 3), new[] { Kick(1560) });

            Assert.That(Active(plan, 0.999), Is.False);
            Assert.That(Active(plan, 1), Is.True);
            Assert.That(Active(plan, 1.625), Is.False);
            Assert.That(Active(plan, 2.999, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 3, throughMeasures: true), Is.False);
        }

        [Test]
        public void FinalMeasureEndsAtTheMeterBoundaryEvenWithoutAFollowingMarker()
        {
            var beats = new List<Beatline>
            {
                Beat(BeatlineType.Measure, 0), Beat(BeatlineType.Strong, 480),
                Beat(BeatlineType.Strong, 960), Beat(BeatlineType.Measure, 1440)
            };
            var plan = Build(Timing(beats, numerator: 3), new[] { Kick(1560) });

            Assert.That(Active(plan, 1), Is.True);
            Assert.That(Active(plan, 2.999, throughMeasures: true), Is.True);
            Assert.That(Active(plan, 3, throughMeasures: true), Is.False);
        }

        [Test]
        public void EarliestPassageCanWarnInPreRollAndQueriesAreIndependentOfPreviousTime()
        {
            var plan = Build(Timing(), new[] { Kick(0), Kick(4080) });

            Assert.That(Active(plan, 7, throughMeasures: true), Is.False);
            Assert.That(Active(plan, 3.5), Is.True);
            Assert.That(Active(plan, -0.5), Is.True);
            Assert.That(Active(plan, -0.501), Is.False);
            Assert.That(Active(plan, 0), Is.False);
            Assert.That(Active(plan, 0, throughMeasures: true), Is.True);
            Assert.That(Active(plan, -0.5), Is.True, "Restart can visit the same negative lead time again.");
            Assert.That(Active(plan, double.NaN), Is.False);
        }

        [Test]
        public void PostFilterPairMustBePlayableAndCarryAuthoredProvenance()
        {
            const uint tick = 2160;
            var timing = Timing();
            var pairedKick = Kick(tick, extra: false, flam: true);
            var chord = Note(EliteDrumPad.Snare, tick);
            chord.AddChildNote(pairedKick);

            Assert.That(Active(Build(timing, new[] { chord }, new[] { tick }), 1.75), Is.True);
            Assert.That(Active(Build(timing, new[] { chord }), 1.75), Is.False, "A regular kick flam is not 2× evidence.");
            Assert.That(Active(Build(timing, new[] { Kick(tick, extra: false) }, new[] { tick }), 1.75), Is.False,
                "A source tick alone cannot restore a stripped pair.");
            Assert.That(Active(Build(timing, new[] { chord }, new[] { tick }, extraKicksEnabled: false), 1.75), Is.False);
            Assert.That(Active(Build(timing, new[] { Kick(tick) }, extraKicksEnabled: false), 1.75), Is.False);
            Assert.That(Active(Build(timing, new[] { Note(EliteDrumPad.Kick, tick, extra: true, flags: NoteFlags.BigRockEnding) }), 1.75),
                Is.False);
            Assert.That(Active(Build(timing, Array.Empty<EliteDrumNote>()), 1.75, throughMeasures: true), Is.False);
        }

        [Test]
        public void VisibleWindowsIncludeTheApproachAndPreserveGapsAcrossSeeks()
        {
            var plan = Build(Timing(), new[] { Kick(1920), Kick(5760) });
            var visible = new List<(double Start, double End)>();
            void Sample(double now, double horizon, bool through)
            {
                var method = plan.GetType().GetMethod("GetVisibleWindows", INSTANCE);
                Assert.That(method, Is.Not.Null);
                method.Invoke(plan, new object[] { now, horizon, through, visible });
            }

            var empty = Build(Timing(), Array.Empty<EliteDrumNote>());
            empty.GetType().GetMethod("GetVisibleWindows", INSTANCE)
                .Invoke(empty, new object[] { 0d, 2d, true, visible });
            Assert.That(visible, Is.Empty);
            Sample(1, 7, false);
            Assert.That(visible, Is.EqualTo(new[] { (1.5, 2.0), (5.5, 6.0) }));
            Sample(4, 5.5, true);
            Assert.That(visible, Is.Empty, "A trailing or leading edge alone occupies no distance.");
            Sample(1.8, 3, true);
            Assert.That(visible, Is.EqualTo(new[] { (1.8, 3.0) }));
        }

        [Test]
        public void EmbersFollowThirtySecondsAcrossTempoChangesAndEmphasizeEveryPlayableKick()
        {
            var sync = Timing();
            sync.Tempos.Add(new TempoChange(240, 2.0, 1920));
            var chord = Note(EliteDrumPad.Snare, 1950);
            chord.AddChildNote(new EliteDrumNote(EliteDrumPad.Kick, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None,
                NoteFlags.None, EliteDrumsChannelFlag.None, 2.015625, 1950, false));
            var plan = Build(sync, new[] { Kick(1920), chord });
            var markers = new List<(double Time, bool IsKick)>();
            void Read(double start, double end, bool through)
            {
                var windows = new List<(double Start, double End)>();
                plan.GetType().GetMethod("GetVisibleWindows", INSTANCE).Invoke(plan,
                    new object[] { start, end, through, windows });
                var get = plan.GetType().GetMethod("GetEmberMarkers", INSTANCE);
                Assert.That(get, Is.Not.Null);
                get.Invoke(plan, new object[] { windows, markers });
            }
            Read(1.75, 2.135, true);
            Assert.That(markers, Is.EqualTo(new[] { (1.75, false), (1.8125, false), (1.875, false), (1.9375, false),
                (2.0, true), (2.015625, true), (2.03125, false), (2.0625, false), (2.09375, false), (2.125, false) }));
            Read(1.75, 2.135, false);
            Assert.That(markers, Is.EqualTo(new[] { (1.75, false), (1.8125, false), (1.875, false), (1.9375, false) }));
            Read(4, 5, true);
            Assert.That(markers, Is.Empty);
        }

        private static object Build(SyncTrack sync, EliteDrumNote[] notes, IReadOnlyList<uint> pairedKickTicks = null,
            bool extraKicksEnabled = true)
        {
            var type = Type.GetType("YARG.Gameplay.Visuals.EliteDoubleKickWarningPlan, Assembly-CSharp", true);
            var difficulty = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert);
            difficulty.Notes.AddRange(notes);
            return type.GetMethod("FromChart", STATIC).Invoke(null, new object[] { difficulty, sync, pairedKickTicks, extraKicksEnabled });
        }

        private static bool Active(object plan, double time, bool throughMeasures = false) =>
            (bool) plan.GetType().GetMethod("IsActive", INSTANCE).Invoke(plan, new object[] { time, throughMeasures });

        private static SyncTrack Timing(List<Beatline> beats = null, uint numerator = 4)
        {
            var sync = new SyncTrack(RESOLUTION, new List<TempoChange> { new(120, 0, 0) },
                new List<TimeSignatureChange> { new(numerator, 4, 0, 0, 0, 0, 0, 0) },
                beats ?? new List<Beatline>());
            if (beats == null) sync.GenerateBeatlines(12d);
            return sync;
        }

        private static Beatline Beat(BeatlineType type, uint tick) => new(type, tick / TICKS_PER_SECOND, tick);

        private static EliteDrumNote Kick(uint tick, bool extra = true, bool flam = false) =>
            Note(EliteDrumPad.Kick, tick, extra, flam);

        private static EliteDrumNote Note(EliteDrumPad pad, uint tick, bool extra = false, bool flam = false,
            NoteFlags flags = NoteFlags.None) =>
            new(pad, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, flam,
                DrumNoteFlags.None, flags, EliteDrumsChannelFlag.None, tick / TICKS_PER_SECOND, tick, extra);
    }
}
