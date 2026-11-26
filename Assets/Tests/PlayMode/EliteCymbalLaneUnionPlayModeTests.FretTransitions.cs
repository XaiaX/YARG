// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteCymbalLaneUnionPlayModeTests
    {
        [UnityTest]
        public IEnumerator TwoSharedFretsLeaveEqualRoomOnEveryDonor()
        {
            yield return ShowStaggeredRolls(
                (EliteDrumPad.Tom1, PhraseType.EliteDrums_Tom1Lane, .125, .875),
                (EliteDrumPad.LeftCrash, PhraseType.EliteDrums_LeftCrashLane, .25, .9375),
                (EliteDrumPad.Tom3, PhraseType.EliteDrums_Tom3Lane, .1875, .8125),
                (EliteDrumPad.RightCrash, PhraseType.EliteDrums_RightCrashLane, .3125, 1));
            Fixture.Player.GetType().GetMethod("UpdateRollFrets", INSTANCE).Invoke(Fixture.Player, new object[] { .5d });
            var pads = new[] { EliteDrumPad.Snare, EliteDrumPad.HiHat, EliteDrumPad.Tom1,
                EliteDrumPad.Tom2, EliteDrumPad.Tom3 };
            var frets = pads.Select(pad => Receptor(Fixture.Player, pad).transform).ToArray();
            Assert.That(frets[2].localScale.x, Is.GreaterThan(1f));
            Assert.That(frets[4].localScale.x, Is.EqualTo(frets[2].localScale.x).Within(.0001f));
            Assert.That(frets[0].localScale.x, Is.InRange(.01f, .99f));
            foreach (var i in new[] { 1, 3 })
                Assert.That(frets[i].localScale.x, Is.EqualTo(frets[0].localScale.x).Within(.0001f));
            float edge = -1f;
            foreach (var fret in frets)
            {
                Assert.That(fret.localPosition.x - .2f * fret.localScale.x, Is.EqualTo(edge).Within(.0001f));
                edge += .4f * fret.localScale.x;
            }
            Assert.That(edge, Is.EqualTo(1f).Within(.0001f));
        }

        [UnityTest]
        public IEnumerator FlamExpansionKeepsSnareHeadsCenteredAtAuthoredSize()
        {
            var chart = PreparedElitePlayerFixture.CreateChart(Note(EliteDrumPad.Snare, 1),
                Note(EliteDrumPad.Snare, 5, flam: true), Note(EliteDrumPad.Snare, 5.5),
                Note(EliteDrumPad.Snare, 6, flat: true));
            var profile = Profile();
            profile.ExpandFretsForFlams = true;
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(chart, profile, EliteDrumsLayoutPreset.Compact, noFail: true);
            var player = Fixture.Player;
            var fret = Receptor(player, EliteDrumPad.Snare).transform;
            var update = player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            var spawn = player.GetType().GetMethod("SpawnNote", INSTANCE);
            var type = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
            float singleWidth = 0f;
            foreach (var note in Playable.Notes)
            {
                update.Invoke(player, new object[] { note.Time });
                spawn.Invoke(player, new object[] { note });
                var element = player.GetComponentsInChildren(type).Single(e =>
                    ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));
                var (min, max) = EliteLayoutRuntimePlayModeTests.ActiveMeshBounds(element, player.transform);
                float width = max - min;
                Assert.That((min + max) * .5f, Is.EqualTo(fret.localPosition.x).Within(.002f));
                if (singleWidth == 0f) singleWidth = width;
                else
                {
                    Assert.That(fret.localScale.x, Is.GreaterThan(1f));
                    if (note.IsFlam || note.IsFlatFlam)
                        Assert.That(width, Is.InRange(singleWidth * 1.35f, .4f * fret.localScale.x + .001f));
                    else Assert.That(width, Is.EqualTo(singleWidth).Within(.001f));
                }
            }
        }

        private static readonly string[] KickExpansionCases = { "dedicated", "beam", "disabled", "fixed-width" };

        [UnityTest]
        public IEnumerator KickFlamOnlyExpandsAnEnabledDedicatedFret(
            [ValueSource(nameof(KickExpansionCases))] string scenario)
        {
            var preset = EliteDrumsLayoutPreset.Compact;
            if (scenario != "beam")
                preset.Slots[5] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            var profile = Profile();
            profile.ExpandFretsForFlams = scenario != "disabled";
            profile.DynamicFretWidths = scenario != "fixed-width";
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(PreparedElitePlayerFixture.CreateChart(Note(EliteDrumPad.Kick, 4, flam: true)),
                profile, preset, noFail: true);
            var player = Fixture.Player;
            var update = player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            update.Invoke(player, new object[] { 0d });
            var donor = Receptor(player, EliteDrumPad.Snare).transform;
            float baseline = donor.localScale.x;
            update.Invoke(player, new object[] { 4d });
            if (scenario == "dedicated")
            {
                Assert.That(donor.localScale.x, Is.LessThan(baseline));
                Assert.That(Receptor(player, EliteDrumPad.Kick).transform.localScale.x, Is.GreaterThan(baseline));
            }
            else Assert.That(donor.localScale.x, Is.EqualTo(baseline).Within(.0001f));
        }

        private static readonly string[] WeightedLayouts = { "fixed", "grow", "lefty", "multiplayer" };

        [UnityTest]
        public IEnumerator WeightedFretsKeepNotesAndKickRingsOnTheirSavedColumns(
            [ValueSource(nameof(WeightedLayouts))] string scenario)
        {
            var preset = EliteDrumsLayoutPreset.Compact;
            preset.FretWeights[0] = 2f;
            preset.FretWeights[1] = 2f;
            preset.FretWeights[9] = 2f; // Empty slots reserve no space.
            preset.GrowHighway = scenario != "fixed";
            preset.FitGemsToFret = true;
            var profile = Profile();
            profile.DynamicFretWidths = false;
            profile.LeftyFlip = scenario == "lefty";
            var hit = Note(EliteDrumPad.Tom1, 4);
            hit.AddChildNote(Note(EliteDrumPad.LeftCrash, 4));
            var chart = PreparedElitePlayerFixture.CreateChart(Note(EliteDrumPad.Snare, 2, flat: true),
                Note(EliteDrumPad.Snare, 3), hit, Note(EliteDrumPad.Kick, 5));
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(chart, profile, preset, noFail: true,
                secondProfile: scenario == "multiplayer" ? Profile() : null);
            var player = Fixture.Player;
            float width = (float)player.GetType().GetProperty("HighwayWidth").GetValue(player);
            Assert.That(width, Is.EqualTo(scenario is "grow" or "lefty" ? 2.8f : 2f).Within(.0001f));
            var kick = Playable.Notes.Last();
            var seams = (float[])player.GetType().GetMethod("TryGetKickBoundaries", INSTANCE)
                .Invoke(player, new object[] { kick });
            Assert.That(seams.Length, Is.EqualTo(6));
            var edges = profile.LeftyFlip ? new[] { 0, 1, 2, 3, 5, 7 } : new[] { 0, 2, 4, 5, 6, 7 };
            for (int i = 0; i < edges.Length; i++)
                Assert.That(seams[i], Is.EqualTo(edges[i] / 7f).Within(.0001f));

            var spawn = player.GetType().GetMethod("SpawnNote", INSTANCE);
            var elementType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
            foreach (var chord in Playable.Notes.Take(3))
            {
                foreach (var note in chord.AllNotes)
                {
                    spawn.Invoke(player, new object[] { note });
                    var element = player.GetComponentsInChildren(elementType).Single(e =>
                        ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));
                    var (min, max) = EliteLayoutRuntimePlayModeTests.ActiveMeshBounds(element, player.transform);
                    int lane = note.Pad == (int)EliteDrumPad.Snare ? 0 : 2;
                    if (profile.LeftyFlip) lane = 4 - lane;
                    float left = (seams[lane] - .5f) * width;
                    float right = (seams[lane + 1] - .5f) * width;
                    Assert.That(min, Is.GreaterThan(left));
                    Assert.That(max, Is.LessThan(right));
                    float mid = (left + right) * .5f;
                    if (lane == 2)
                    {
                        bool onRight = (note.Pad == (int)EliteDrumPad.LeftCrash) != profile.LeftyFlip;
                        if (onRight) Assert.That(min, Is.GreaterThanOrEqualTo(mid - .001f));
                        else Assert.That(max, Is.LessThanOrEqualTo(mid + .001f));
                    }
                    else Assert.That((min + max) * .5f, Is.EqualTo(mid).Within(.003f));
                }
            }
        }

        private static readonly string[] OrdinaryCollisionLayouts = { "compact", "lefty", "lanes-off", "full" };

        [UnityTest]
        public IEnumerator OrdinaryCollisionWindowsFollowTheResolvedFrets(
            [ValueSource(nameof(OrdinaryCollisionLayouts))] string scenario)
        {
            var last = Stream("tom3", EliteDrumPad.Tom3, 3, 7.5);
            var crashes = Stream("crashes", EliteDrumPad.RightCrash, 3, 7.5);
            for (int i = 0; i < last.Length; i++) last[i].AddChildNote(crashes[i]);
            var middle = Stream("tom2", EliteDrumPad.Tom2, 3.5)[0];
            middle.AddChildNote(Stream("ride", EliteDrumPad.Ride, 3.5)[0]);
            var chart = PreparedElitePlayerFixture.CreateChart(last[0], middle, last[1]);
            bool compact = scenario != "full";
            var profile = Profile();
            profile.LeftyFlip = scenario == "lefty";
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(chart, profile, compact ? EliteDrumsLayoutPreset.Compact :
                EliteDrumsLayoutPreset.Full, noFail: true, enableLanes: scenario != "lanes-off");

            var player = Fixture.Player;
            var update = player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            var tom3 = Receptor(player, EliteDrumPad.Tom3).transform;
            var tom2 = Receptor(player, EliteDrumPad.Tom2).transform;
            var donor = Receptor(player, EliteDrumPad.Snare).transform;
            float baseline = tom3.localScale.x;
            update.Invoke(player, new object[] { 3d });
            Assert.That(tom2.localScale.x, Is.EqualTo(tom3.localScale.x).Within(.0002f));
            if (compact)
            {
                Assert.That(tom3.localScale.x, Is.GreaterThan(baseline));
                Assert.That(donor.localScale.x, Is.LessThan(baseline));
                foreach (var chord in Playable.Notes)
                {
                    var slots = chord.ChildNotes.Prepend(chord).Select(n =>
                    {
                        object[] args = { n, 0f, 0f };
                        Assert.That(player.GetType().GetMethod("TryGetRollSlot", INSTANCE).Invoke(player, args), Is.True);
                        return (float)args[1];
                    }).ToArray();
                    Assert.That((slots[1] - slots[0]) * (profile.LeftyFlip ? -1 : 1), Is.GreaterThan(0f));
                }
            }
            else Assert.That(tom3.localScale.x, Is.EqualTo(baseline).Within(.0002f));

            // A dotted half at normal width between the two tom 3 reservations
            // keeps that fret open while the tom 2 reservation ends independently.
            update.Invoke(player, new object[] { 5d });
            Assert.That(tom3.localScale.x / baseline, Is.EqualTo(compact ? 1.6f : 1f).Within(.0002f));
            Assert.That(tom2.localScale.x / baseline, Is.EqualTo(compact ? .85f : 1f).Within(.0002f));
            update.Invoke(player, new object[] { 9d });
            Assert.That(tom3.localScale.x, Is.EqualTo(baseline).Within(.0002f));
        }

        private static readonly bool[] MirroredLayouts = { false, true };

        [UnityTest]
        public IEnumerator NeighboringHandHeadsFitTheirArrivalFrets([ValueSource(nameof(MirroredLayouts))] bool lefty)
        {
            // Keep Tom 2/ride open. On the next two chords the donor fret holds
            // Tom 3 and right crash respectively, as in Child in Time.
            var hits = Stream("tom2", EliteDrumPad.Tom2, 3, 6);
            var ride = Stream("ride", EliteDrumPad.Ride, 3, 4, 4.5, 6);
            var tom = Stream("tom3", EliteDrumPad.Tom3, 4, 5);
            hits[0].AddChildNote(ride[0]);
            hits[1].AddChildNote(ride[3]);
            ride[1].AddChildNote(tom[0]);
            ride[2].AddChildNote(Stream("crash", EliteDrumPad.RightCrash, 4.5)[0]);
            tom[1].AddChildNote(Stream("kick", EliteDrumPad.Kick, 5)[0]);
            var song = PreparedElitePlayerFixture.CreateChart(hits[0], ride[1], ride[2], tom[1], hits[1]);
            var profile = Profile();
            profile.LeftyFlip = lefty;
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(song, profile, EliteDrumsLayoutPreset.Compact, noFail: true);
            var player = Fixture.Player;
            var update = player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            var spawn = player.GetType().GetMethod("SpawnNote", INSTANCE);
            var noteType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
            foreach (var chord in Playable.Notes.Where(n => n.Time >= 4 && n.Time <= 5))
            {
                update.Invoke(player, new object[] { chord.Time });
                foreach (var head in chord.AllNotes)
                {
                    if (head.Pad == (int)EliteDrumPad.Kick) continue;
                    spawn.Invoke(player, new object[] { head });
                    var element = player.GetComponentsInChildren(noteType).Single(e =>
                        ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), head));
                    var mesh = EliteLayoutRuntimePlayModeTests.ActiveMeshBounds(element, player.transform);
                    var fret = Receptor(player, (EliteDrumPad)head.Pad).transform;
                    float halfWidth = .2f * fret.localScale.x;
                    if (chord.Time == 5)
                    {
                        Assert.That(mesh.max - mesh.min, Is.GreaterThan(2 * halfWidth),
                            "A lone donor tom with a kick retains its ordinary size.");
                        continue;
                    }
                    Assert.That(mesh.min, Is.GreaterThanOrEqualTo(fret.localPosition.x - halfWidth - .001f),
                        $"Left edge of {(EliteDrumPad)head.Pad} at {chord.Time}s");
                    Assert.That(mesh.max, Is.LessThanOrEqualTo(fret.localPosition.x + halfWidth + .001f),
                        $"Right edge of {(EliteDrumPad)head.Pad} at {chord.Time}s");
                }
            }
        }

        [UnityTest]
        public IEnumerator PairedHitWithinSharedRollsPreservesTheirLaterReturn()
        {
            var tom = Stream("tom", EliteDrumPad.Tom3, 3, 5, 7);
            var crash = Stream("crash", EliteDrumPad.RightCrash, 3.5, 5, 6.5);
            tom[1].AddChildNote(crash[1]);
            var song = PreparedElitePlayerFixture.CreateChart(tom[0], crash[0], tom[1], crash[2], tom[2]);
            var tier = song.AuthoredDrumSources.Tiers.Values.Single();
            var track = (InstrumentDifficulty<EliteDrumNote>)tier.GetType().GetField("_elite", INSTANCE).GetValue(tier);
            track.SetEliteDrumNativeAuthoredLaneRecords(new[] {
                new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_Tom3Lane,
                    tom[0].Tick, tom[^1].Tick, tom.Select(n => n.SourceDefinition)),
                new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_RightCrashLane,
                    crash[0].Tick, crash[^1].Tick, crash.Select(n => n.SourceDefinition))
            });
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(song, Profile(), EliteDrumsLayoutPreset.Compact, noFail: true);
            var update = Fixture.Player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            var fret = Receptor(Fixture.Player, EliteDrumPad.Tom3).transform;
            foreach (var (time, width) in new[] { (5d, 1.6f), (7d, 1.6f), (7.5d, 1.3f), (8d, 1f) })
            {
                update.Invoke(Fixture.Player, new object[] { time });
                Assert.That(fret.localScale.x, Is.EqualTo(width).Within(.0002f), $"Fret width at {time}s");
            }
        }

        private static readonly int[] NormalWidthGaps = { 2, 3, 4 };

        [UnityTest]
        public IEnumerator SameFretStaysOpenAcrossADottedHalfAtNormalWidth(
            [ValueSource(nameof(NormalWidthGaps))] int gapInQuarters)
        {
            // At 120 BPM the first return ends at 2.5s. The second approach starts
            // four quarters before its first hit. All anchors land on strong beats.
            double second = 4.5 + .5 * gapInQuarters;
            yield return ShowStaggeredRolls(
                (EliteDrumPad.Tom3, PhraseType.EliteDrums_Tom3Lane, .5, 1.375),
                (EliteDrumPad.RightCrash, PhraseType.EliteDrums_RightCrashLane, .625, 1.5),
                (EliteDrumPad.Tom3, PhraseType.EliteDrums_Tom3Lane, second, second + .375),
                (EliteDrumPad.RightCrash, PhraseType.EliteDrums_RightCrashLane, second + .125, second + .5));
            var update = Fixture.Player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            var fret = Receptor(Fixture.Player, EliteDrumPad.Tom3).transform;
            foreach (var (time, width) in new[] { (1d, 1.6f), (3d, gapInQuarters <= 3 ? 1.6f : 1f),
                         (second, 1.6f), (second + 1.5, 1f) })
            {
                update.Invoke(Fixture.Player, new object[] { time });
                Assert.That(fret.localScale.x, Is.EqualTo(width).Within(.0002f), $"gap {gapInQuarters}, time {time}");
            }
        }

        private static readonly string[] ReturnMarkers = { "exact", "near", "half-note", "far", "missing" };

        [UnityTest]
        public IEnumerator StrongBeatTransitionsFollowTheTempoMap(
            [ValueSource(nameof(ReturnMarkers))] string marker)
        {
            var tom = Stream("tom", EliteDrumPad.Tom3, 2d, 2.875d);
            var crash = Stream("crash", EliteDrumPad.RightCrash, 2.125d, 3d);
            var chart = PreparedElitePlayerFixture.CreateChart(tom.Concat(crash).OrderBy(n => n.Time).ToArray());
            // The chart is at 120 BPM until a point half a quarter into the return.
            chart.SyncTrack.Tempos.Add(new TempoChange(240, 3.25, 3120));
            var tier = chart.AuthoredDrumSources.Tiers.Values.Single();
            var track = (InstrumentDifficulty<EliteDrumNote>)tier.GetType().GetField("_elite", INSTANCE).GetValue(tier);
            track.SetEliteDrumNativeAuthoredLaneRecords(new[] {
                new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_Tom3Lane,
                    tom[0].Tick, tom[^1].Tick, tom.Select(n => n.SourceDefinition)),
                new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_RightCrashLane,
                    crash[0].Tick, crash[^1].Tick, crash.Select(n => n.SourceDefinition))
            });
            // First head at q4. The latest eligible strong marker is q1.5.
            chart.SyncTrack.Beatlines.Add(new Beatline(BeatlineType.Measure, .75, 720));
            chart.SyncTrack.Beatlines.Add(new Beatline(BeatlineType.Weak, 1, 960));
            chart.SyncTrack.Beatlines.Add(new Beatline(BeatlineType.Strong, 2, 1920));
            uint after = marker switch
            {
                "exact" => 2880u, "near" => 3120u, "half-note" => 3840u, "far" => 3960u, _ => 0u
            };
            if (after != 0)
                chart.SyncTrack.Beatlines.Add(new Beatline(BeatlineType.Strong, chart.SyncTrack.TickToTime(after), after));
            // Keep this authored grid through the fixture's ten-second highway initialization.
            chart.SyncTrack.Beatlines.Add(new Beatline(BeatlineType.Weak, 12, chart.SyncTrack.TimeToTick(12)));
            chart.SyncTrack.FinishLoading(chart.SyncTrack.TimeToTick(12));
            var profile = Profile();
            profile.OpenHatPulseCadence = DrumOpenHatPulseCadence.Eighth;
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(chart, profile, EliteDrumsLayoutPreset.Compact, noFail: true);
            var update = Fixture.Player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            var fret = Receptor(Fixture.Player, EliteDrumPad.Tom3).transform;
            var (release, mid, end) = marker switch
            {
                "near" => (3.25, 3.5, 3.75),
                "far" => (3.125, 3.4375, 3.6875),
                _ => (3.0, 3.375, 3.625),
            };
            foreach (var sample in new[] { (.25, 1.3f), (.75, 1.6f), (2d, 1.6f),
                         (release, 1.6f), (mid, 1.3f), (end, 1f) })
            {
                update.Invoke(Fixture.Player, new object[] { sample.Item1 });
                Assert.That(fret.localScale.x, Is.EqualTo(sample.Item2).Within(.0002f), $"{marker} at {sample.Item1}s");
            }
        }

        private static readonly string[] FretTransitionCases = { "compact", "compact-lefty", "full" };

        [UnityTest]
        public IEnumerator SharedFretAndArrivingGemsFollowChartTime(
            [ValueSource(nameof(FretTransitionCases))] string scenario)
        {
            bool mirrored = scenario == "compact-lefty";
            bool compact = scenario != "full";
            var layout = compact ? EliteDrumsLayoutPreset.Compact : EliteDrumsLayoutPreset.Full;
            var toms = Stream("roll tom", EliteDrumPad.Tom3, 3d, 3.5d, 4d);
            var crashes = Stream("roll crash", EliteDrumPad.RightCrash, 3d, 3.5d, 4d);
            var loose = Stream("loose tom", EliteDrumPad.Tom3, .25d, 1.5d, 4.5d);
            var donors = Stream("donor tom", EliteDrumPad.Tom2, .375d, 3.375d);
            var kicks = Stream("kick", EliteDrumPad.Kick, .375d, 1.5d, 3.5d, 4.5d, 5.125d);
            var heads = toms.Concat(crashes).Concat(loose).Concat(donors).Concat(kicks);
            var chart = PreparedElitePlayerFixture.CreateChart(heads.GroupBy(note => note.Tick)
                .OrderBy(group => group.Key).Select(group =>
                {
                    var parent = group.First();
                    foreach (var child in group.Skip(1)) parent.AddChildNote(child);
                    return parent;
                }).ToArray());
            var tier = chart.AuthoredDrumSources.Tiers.Values.Single();
            var track = (InstrumentDifficulty<EliteDrumNote>)tier.GetType().GetField("_elite", INSTANCE).GetValue(tier);
            track.SetEliteDrumNativeAuthoredLaneRecords(new[] {
                new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_Tom3Lane,
                    toms[0].Tick, toms[^1].Tick, toms.Select(n => n.SourceDefinition)),
                new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_RightCrashLane,
                    crashes[0].Tick, crashes[^1].Tick, crashes.Select(n => n.SourceDefinition))
            });
            // Both the safe cutoff (q4) and last hit (q8) land exactly on strong beats.
            foreach (uint tick in new uint[] { 0, 1920, 2880, 3840, 10560 })
                chart.SyncTrack.Beatlines.Add(new Beatline(BeatlineType.Strong, tick / 960d, tick));
            chart.SyncTrack.FinishLoading(10560);
            var profile = Profile();
            profile.LeftyFlip = mirrored;
            profile.OpenHatPulseCadence = DrumOpenHatPulseCadence.Quarter;
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(chart, profile, layout, noFail: true);

            var player = Fixture.Player;
            var columns = compact ? 5 : 8;
            float nominalWidth = 2f / columns;
            var pads = compact ? new[] { EliteDrumPad.Snare, EliteDrumPad.HiHat, EliteDrumPad.Tom1,
                    EliteDrumPad.Tom2, EliteDrumPad.Tom3 }
                : new[] { EliteDrumPad.Snare, EliteDrumPad.HiHat, EliteDrumPad.LeftCrash, EliteDrumPad.Tom1,
                    EliteDrumPad.Tom2, EliteDrumPad.Tom3, EliteDrumPad.Ride, EliteDrumPad.RightCrash };
            var frets = pads.Select(pad => Receptor(player, pad).transform).OrderBy(fret => fret.localPosition.x).ToArray();
            var nominalScales = frets.Select(fret => fret.localScale.x).ToArray();
            var update = player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            Assert.That(update, Is.Not.Null);
            var boundariesAt = player.GetType().GetMethod("TryGetKickBoundaries", INSTANCE);
            Assert.That(boundariesAt, Is.Not.Null);
            var playable = Playable.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();

            // At 120 BPM two quarters last one second. Seek backwards after release
            // so a history-dependent transition cannot pass this check.
            foreach (var frame in new[] { (.375d, 0f), (1.5d, .5f), (3.5d, 1f), (4.5d, .5f),
                         (5.125d, 0f), (3.5d, 1f), (.375d, 0f) })
            {
                double time = frame.Item1;
                float phase = compact ? frame.Item2 : 0f;
                update.Invoke(player, new object[] { time });
                var kick = playable.Single(n => n.Pad == (int)EliteDrumPad.Kick && n.Time == time);
                var kickEdges = (float[])boundariesAt.Invoke(player, new object[] { kick });
                if (phase > 0f) Assert.That(kickEdges, Is.Not.Null);
                if (kickEdges != null) Assert.That(kickEdges, Has.Length.EqualTo(columns + 1));
                float left = -1f;
                for (int i = 0; i < columns; i++)
                {
                    bool shared = i == (mirrored ? 0 : columns - 1);
                    float width = nominalWidth * (1f + phase * (shared ? .6f : -.15f));
                    Assert.That(frets[i].localPosition.x, Is.EqualTo(left + width / 2f).Within(.0004f),
                        $"{scenario}, receptor {i}, chart time {time}");
                    Assert.That(frets[i].localScale.x / nominalScales[i] * nominalWidth, Is.EqualTo(width).Within(.0004f));
                    if (kickEdges != null) Assert.That(kickEdges[i], Is.EqualTo((left + 1f) / 2f).Within(.0003f));
                    left += width;
                }
                Assert.That(left, Is.EqualTo(1f).Within(.0001f));
                if (kickEdges != null) Assert.That(kickEdges[^1], Is.EqualTo(1f).Within(.0003f));
            }

            var spawn = player.GetType().GetMethod("SpawnNote", INSTANCE);
            var sample = playable.Where(n => (n.Pad == (int)EliteDrumPad.Tom3 && (n.Time < 3d || n.Time > 4d))
                || n.Pad == (int)EliteDrumPad.Tom2).ToArray();
            foreach (var note in sample)
                spawn.Invoke(player, new object[] { note });
            var geometry = new Dictionary<EliteDrumNote, Bounds>();
            foreach (var element in player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement")))
            {
                var note = (EliteDrumNote)element.GetType().GetProperty("NoteRef").GetValue(element);
                if (note == null || !sample.Contains(note)) continue;
                var renderers = element.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
                Assert.That(renderers, Is.Not.Empty);
                var bounds = renderers[0].bounds;
                foreach (var part in renderers) bounds.Encapsulate(part.bounds);
                geometry.Add(note, bounds);
            }
            Assert.That(geometry.Count, Is.EqualTo(sample.Length));
            foreach (var padGroup in sample.GroupBy(note => note.Pad))
            {
                var reference = padGroup.OrderBy(note => note.Time).First();
                var baseline = geometry[reference];
                foreach (var note in padGroup.Where(n => n != reference))
                {
                    Assert.That(geometry[note].size.x, Is.EqualTo(baseline.size.x).Within(.002f),
                        "A lone drum keeps its ordinary gem width while its fret moves.");
                    float center = player.transform.InverseTransformPoint(geometry[note].center).x;
                    float original = player.transform.InverseTransformPoint(baseline.center).x;
                    if (!compact) Assert.That(center, Is.EqualTo(original).Within(.003f));
                    else if (note.Pad == (int)EliteDrumPad.Tom2)
                        Assert.That(center, Is.EqualTo(mirrored ? -.19f : .19f).Within(.003f));
                    else
                        Assert.That((original - center) * (mirrored ? -1f : 1f), Is.GreaterThan(.04f),
                            "The loose tom should move toward the drum side of the expanding shared fret.");
                }
            }

            // The last seek put the frets back at baseline. A newly spawned beam
            // must already have the rings that match its future impact time.
            var futureKick = playable.Single(n => n.Pad == (int)EliteDrumPad.Kick && n.Time == 3.5d);
            spawn.Invoke(player, new object[] { futureKick });
            var kickElement = player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"))
                .Single(element => ReferenceEquals(element.GetType().GetProperty("NoteRef").GetValue(element), futureKick));
            var kickVisual = kickElement.GetComponentInChildren(Production("YARG.Gameplay.Visuals.ModularKickVisual"));
            Assert.That(kickVisual, Is.Not.Null);
            var beam = (MeshFilter)kickVisual.GetType().GetField("_beam", INSTANCE).GetValue(kickVisual);
            var xs = beam.sharedMesh.vertices.Select(v => player.transform.InverseTransformPoint(beam.transform.TransformPoint(v)).x)
                .ToArray();
            for (int i = 1; i < columns; i++)
            {
                float seam = compact ? -1f + .34f * i : -1f + nominalWidth * i;
                if (mirrored) seam = -seam;
                var collar = xs.Where(x => Mathf.Abs(x - seam) < .045f).ToArray();
                Assert.That(collar, Is.Not.Empty, $"Missing kick collar at impact seam {seam}.");
                Assert.That(collar.Max() - collar.Min(), Is.GreaterThan(.004f));
                Assert.That((collar.Min() + collar.Max()) * .5f, Is.EqualTo(seam).Within(.002f));
            }
        }
    }
}
