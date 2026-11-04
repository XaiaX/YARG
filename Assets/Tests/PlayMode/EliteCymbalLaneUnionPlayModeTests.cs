// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteCymbalLaneUnionPlayModeTests : EliteFlamRuntimeTestBase
    {
        [UnityTest]
        public IEnumerator OverlappingMergedRollsRenderOnePhysicalStrip()
        {
            var left = Stream("left", EliteDrumPad.LeftCrash, 1d, 2d, 3d);
            var ride = Stream("second-left", EliteDrumPad.LeftCrash, 1.5d, 2.5d, 3.5d);
            var song = PreparedElitePlayerFixture.CreateChart(left.Concat(ride).OrderBy(note => note.Time).ToArray());
            var tier = song.AuthoredDrumSources.Tiers.Values.Single();
            var authored = (InstrumentDifficulty<EliteDrumNote>)tier.GetType().GetField("_elite", INSTANCE).GetValue(tier);
            authored.SetEliteDrumNativeAuthoredLaneRecords(new[]
            {
                new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_LeftCrashLane,
                    left[0].Tick, left[^1].Tick, left.Select(note => note.SourceDefinition)),
                new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_LeftCrashLane,
                    ride[0].Tick, ride[^1].Tick, ride.Select(note => note.SourceDefinition))
            });
            var layout = EliteDrumsLayoutEditor.Place((EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full
                .CopyWithNewName("Physical roll union"), 2, "ride>left-crash");
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(song, Profile(), layout, noFail: true);
            var player = Fixture.Player;
            var segments = ((IEnumerable)player.GetType().GetField("_playableLaneSegments", INSTANCE).GetValue(player))
                .Cast<EliteDrumsPlayableLaneSegment>().ToArray();
            Assert.That(segments, Has.Length.EqualTo(1), "Rendering must cache the same physical union used for judging.");
            Assert.That(segments[0].Members, Has.Count.EqualTo(6));
            Assert.That(segments[0].StartTime, Is.EqualTo(1d));
            Assert.That(segments[0].EndTime, Is.EqualTo(3.5d));
            var pool = player.GetType().GetField("LanePool", INSTANCE).GetValue(player);
            foreach (var parent in Playable.Notes)
                player.GetType().GetMethod("OnNoteSpawned", INSTANCE).Invoke(player, new object[] { parent });
            var spawned = ((IEnumerable)pool.GetType().GetProperty("AllSpawned").GetValue(pool)).Cast<object>().ToArray();
            Assert.That(spawned, Has.Length.EqualTo(1), "One physical roll must not draw overlapping coplanar strips.");
        }

        [UnityTest]
        public IEnumerator OverlappingMergedRollsRenderThreeStripsAndKeepPracticeTargets()
        {
            var left = Stream("left", EliteDrumPad.LeftCrash, 1d, 2d, 3d, 4d);
            var ride = Stream("ride", EliteDrumPad.Ride, 2.5d, 3.5d, 4.5d);
            var right = Stream("right", EliteDrumPad.RightCrash, .5d, 1.5d, 2.5d, 4.5d);
            var song = PreparedElitePlayerFixture.CreateChart(left.Concat(ride).Concat(right).OrderBy(note => note.Time).ToArray());
            var authored = (InstrumentDifficulty<EliteDrumNote>)song.AuthoredDrumSources.Tiers.Values.Single()
                .GetType().GetField("_elite", INSTANCE).GetValue(song.AuthoredDrumSources.Tiers.Values.Single());
            authored.SetEliteDrumNativeAuthoredLaneRecords(new[]
            {
                new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_LeftCrashLane,
                    left[0].Tick, left[^1].Tick, left.Select(note => note.SourceDefinition)),
                new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_RideLane,
                    ride[0].Tick, ride[^1].Tick, ride.Select(note => note.SourceDefinition)),
                new EliteDrumNativeAuthoredLaneRecord(2, PhraseType.EliteDrums_RightCrashLane,
                    right[0].Tick, right[^1].Tick, right.Select(note => note.SourceDefinition))
            });
            var layout = EliteDrumsLayoutEditor.Place((EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full
                .CopyWithNewName("Duration-aware rolls"), 2, "ride>left-crash");
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(song, Profile(), layout, noFail: true);
            var player = Fixture.Player;
            var segments = ((IEnumerable)player.GetType().GetField("_playableLaneSegments", INSTANCE).GetValue(player))
                .Cast<EliteDrumsPlayableLaneSegment>().ToArray();
            Assert.That(segments.Select(segment => segment.Pad), Is.EquivalentTo(new[]
                { (int)EliteDrumPad.Ride, (int)EliteDrumPad.HiHat, (int)EliteDrumPad.RightCrash }));
            Assert.That(segments.Single(segment => segment.Pad == (int)EliteDrumPad.HiHat).StartTime, Is.EqualTo(1d));
            foreach (var parent in Playable.Notes)
                player.GetType().GetMethod("OnNoteSpawned", INSTANCE).Invoke(player, new object[] { parent });
            Assert.That(SpawnedLanes(), Has.Length.EqualTo(3));
            Assert.That(SpawnedLanes().Select(lane => lane.transform.localPosition.x).Distinct().Count(), Is.EqualTo(3));
            var practice = (InstrumentDifficulty<EliteDrumNote>)player.GetType().GetMethod("CreatePracticeTrack", INSTANCE)
                .Invoke(player, new object[] { 960u, 2200u });
            float[] thresholds = { 1, 2, 3, 4, 5, 6 };
            var parameters = EnginePreset.Default.Drums.Create(thresholds, thresholds, DrumsEngineParameters.DrumMode.Elite);
            _ = new EliteDrumsEngine(practice, song.SyncTrack, parameters, false, true, cymbalLayout: layout);
            Assert.That(practice.Notes.SelectMany(note => note.ChildNotes.Prepend(note))
                .Where(note => note.OriginalPad == (int)EliteDrumPad.LeftCrash).Select(note => note.Pad),
                Is.Not.Empty.And.All.EqualTo((int)EliteDrumPad.HiHat));
        }

        [UnityTest]
        public IEnumerator AdjacentNativeRollStripsHavePositiveGap()
        {
            foreach (int columns in new[] { 5, 8, 10 })
            foreach (bool lefty in new[] { false, true })
            {
                yield return InitializeAdjacentRolls(columns, lefty);
                var bounds = SpawnedLanes().Select(lane => BakedBounds(lane, Fixture.Player.transform))
                    .OrderBy(box => box.center.x).ToArray();
                float gap = bounds[1].min.x - bounds[0].max.x;
                Debug.Log($"{columns} columns, lefty={lefty}, adjacent gap={gap}");
                Assert.That(gap, Is.GreaterThan(2f / columns * .02f),
                    "Adjacent lane borders must leave a visible gap instead of overlapping coplanar faces.");
                yield return Fixture.Dispose();
                Fixture = null;
            }
        }

        [UnityTest]
        public IEnumerator NativeRollTailExtendsPastFinalGemCenter()
        {
            foreach (int columns in new[] { 5, 8, 10 })
            {
                yield return InitializeAdjacentRolls(columns, false);
                float noteSpeed = (float)Fixture.Player.GetType().GetProperty("NoteSpeed").GetValue(Fixture.Player);
                foreach (var lane in SpawnedLanes())
                {
                    var bounds = BakedBounds(lane, lane.transform);
                    float tail = bounds.max.z - 20f * noteSpeed;
                    Debug.Log($"{columns} columns: tail past last center={tail}");
                    Assert.That(tail, Is.GreaterThanOrEqualTo(2f / columns * .45f),
                        "The inclusive final cymbal needs lane coverage beyond its center.");
                    Assert.That((double)lane.GetType().GetField("EndTime").GetValue(lane), Is.EqualTo(21d),
                        "Tail geometry must not extend the logical phrase.");
                    Assert.That((double)lane.GetType().GetProperty("ElementTime").GetValue(lane), Is.EqualTo(1d));
                }
                yield return Fixture.Dispose();
                Fixture = null;
            }
        }

        private IEnumerator InitializeAdjacentRolls(int columns, bool lefty)
        {
            var left = Stream("left", EliteDrumPad.LeftCrash, 1d, 11d, 21d);
            var ride = Stream("neighbor", columns == 5 ? EliteDrumPad.Ride : EliteDrumPad.Tom1, 1d, 11d, 21d);
            var song = PreparedElitePlayerFixture.CreateChart(left.Concat(ride).OrderBy(note => note.Time).ToArray());
            var tier = song.AuthoredDrumSources.Tiers.Values.Single();
            var authored = (InstrumentDifficulty<EliteDrumNote>)tier.GetType().GetField("_elite", INSTANCE).GetValue(tier);
            authored.SetEliteDrumNativeAuthoredLaneRecords(new[]
            {
                new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_LeftCrashLane,
                    left[0].Tick, left[^1].Tick, left.Select(note => note.SourceDefinition)),
                new EliteDrumNativeAuthoredLaneRecord(1, columns == 5 ? PhraseType.EliteDrums_RideLane : PhraseType.EliteDrums_Tom1Lane,
                    ride[0].Tick, ride[^1].Tick, ride.Select(note => note.SourceDefinition))
            });
            var layout = columns == 5 ? EliteDrumsLayoutPreset.Compact : EliteDrumsLayoutPreset.Full;
            if (columns == 10)
            {
                layout = EliteDrumsLayoutEditor.PlaceKick(layout, 8, EliteDrumsLayoutSlotKind.Kick1);
                layout = EliteDrumsLayoutEditor.PlaceKick(layout, 9, EliteDrumsLayoutSlotKind.Kick2);
            }
            var profile = Profile();
            profile.LeftyFlip = lefty;
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(song, profile, layout, noFail: true);
            foreach (var parent in Playable.Notes)
                Fixture.Player.GetType().GetMethod("OnNoteSpawned", INSTANCE).Invoke(Fixture.Player, new object[] { parent });
            Assert.That(SpawnedLanes(), Has.Length.EqualTo(2));
            yield return null;
        }

        private Component[] SpawnedLanes()
        {
            var pool = Fixture.Player.GetType().GetField("LanePool", INSTANCE).GetValue(Fixture.Player);
            return ((IEnumerable)pool.GetType().GetProperty("AllSpawned").GetValue(pool)).Cast<Component>().ToArray();
        }

        private static Bounds BakedBounds(Component lane, Transform relativeTo)
        {
            var renderer = lane.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var mesh = new Mesh();
            try
            {
                renderer.BakeMesh(mesh);
                // The baked skin contains the armature scale. Apply only its rigid
                // pose, avoiding a second multiplication by the imported 100x scale.
                var vertices = mesh.vertices.Select(vertex => relativeTo.InverseTransformPoint(renderer.transform.position + renderer.transform.rotation * vertex)).ToArray();
                var bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (var vertex in vertices) bounds.Encapsulate(vertex);
                return bounds;
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        private static EliteDrumNote[] Stream(string id, EliteDrumPad pad, params double[] times) =>
            times.Select((time, index) => new EliteDrumNote(pad, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, (uint)(time * 960), false,
                new EliteDrumSourceDefinition(id, index, (int)pad, (uint)(time * 960), 0, time))).ToArray();
    }
}
