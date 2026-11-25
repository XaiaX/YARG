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
    public sealed partial class EliteCymbalLaneUnionPlayModeTests : EliteFlamRuntimeTestBase
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

        private static readonly string[] SharedFretCases =
        {
            "compact-pair", "compact-pair-lefty", "compact-triple", "compact-triple-lefty",
            "compact-triple-chord", "compact-triple-chord-lefty", "full"
        };

        [UnityTest]
        public IEnumerator AuthoredRollsAndAlternatingGemsUseTheSameColumns(
            [ValueSource(nameof(SharedFretCases))] string scenario)
        {
            bool full = scenario == "full";
            bool lefty = scenario.EndsWith("-lefty", StringComparison.Ordinal);
            bool ridePresent = !scenario.Contains("pair");
            bool extraChord = scenario.Contains("chord");
            var layout = full ? EliteDrumsLayoutPreset.Full : EliteDrumsLayoutPreset.Compact;
            var pads = ridePresent ? new[] { EliteDrumPad.Ride, EliteDrumPad.Tom3, EliteDrumPad.RightCrash }
                : new[] { EliteDrumPad.Tom3, EliteDrumPad.RightCrash };
            var phrases = ridePresent
                ? new[] { PhraseType.EliteDrums_RideLane, PhraseType.EliteDrums_Tom3Lane, PhraseType.EliteDrums_RightCrashLane }
                : new[] { PhraseType.EliteDrums_Tom3Lane, PhraseType.EliteDrums_RightCrashLane };
            // Lefty cases stagger the endpoints as well. Each member must keep its slot
            // for the entire roll, including its first and last note.
            var streams = pads.Select((pad, index) => Stream(pad.ToString(), pad,
                .125d + (lefty ? index / 64d : 0d), .21875d + .09375d * index,
                .625d + (lefty ? index / 64d : 0d))).ToArray();
            var heads = streams.SelectMany(stream => stream);
            if (extraChord) heads = heads.Concat(Stream("loose tom", EliteDrumPad.Tom2, streams[0][1].Time));
            var chartNotes = heads.GroupBy(note => note.Tick)
                .OrderBy(group => group.Key).Select(group =>
                {
                    var parent = group.First();
                    foreach (var child in group.Skip(1)) parent.AddChildNote(child);
                    return parent;
                }).ToArray();
            var song = PreparedElitePlayerFixture.CreateChart(chartNotes);
            var tier = song.AuthoredDrumSources.Tiers.Values.Single();
            var authored = (InstrumentDifficulty<EliteDrumNote>)tier.GetType().GetField("_elite", INSTANCE).GetValue(tier);
            authored.SetEliteDrumNativeAuthoredLaneRecords(streams.Select((notes, i) =>
                new EliteDrumNativeAuthoredLaneRecord(i, phrases[i], notes[0].Tick, notes[^1].Tick,
                    notes.Select(note => note.SourceDefinition))).ToArray());
            var profile = Profile();
            profile.LeftyFlip = lefty;
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(song, profile, layout, noFail: true);
            var player = Fixture.Player;
            foreach (var parent in Playable.Notes)
            {
                player.GetType().GetMethod("OnNoteSpawned", INSTANCE).Invoke(player, new object[] { parent });
                foreach (var note in parent.AllNotes)
                    player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(player, new object[] { note });
            }
            yield return null;

            var strips = SpawnedLanes().OrderBy(lane => lane.transform.localPosition.x).ToArray();
            Assert.That(strips, Has.Length.EqualTo(pads.Length));
            var physicalOrder = full ? new[] { EliteDrumPad.Tom3, EliteDrumPad.Ride, EliteDrumPad.RightCrash } : pads.ToArray();
            if (lefty) Array.Reverse(physicalOrder);
            var stripBounds = strips.Select(lane => BakedBounds(lane, player.transform)).ToArray();
            for (int i = 0; i < strips.Length; i++)
            {
                var strip = strips[i];
                var members = streams[Array.IndexOf(pads, physicalOrder[i])];
                Assert.That(strip.GetType().GetProperty("ElementTime").GetValue(strip), Is.EqualTo(members[0].Time));
                Assert.That(strip.GetType().GetField("EndTime").GetValue(strip), Is.EqualTo(members[^1].Time));
                if (i > 0)
                    Assert.That(stripBounds[i].min.x, Is.GreaterThan(stripBounds[i - 1].max.x),
                        "Each simultaneously active strip must have its own visible space.");
            }

            var elements = player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"))
                .Where(element => element.GetType().GetProperty("NoteRef").GetValue(element) != null).ToArray();
            Assert.That(elements, Has.Length.EqualTo(pads.Length * 3 + (extraChord ? 1 : 0)));
            foreach (var element in elements)
            {
                var note = (EliteDrumNote)element.GetType().GetProperty("NoteRef").GetValue(element);
                var renderers = element.GetComponentsInChildren<MeshRenderer>().Where(renderer => renderer.enabled).ToArray();
                Assert.That(renderers, Is.Not.Empty);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float left = player.transform.InverseTransformPoint(bounds.min).x;
                float right = player.transform.InverseTransformPoint(bounds.max).x;
                var pad = (EliteDrumPad)note.Pad;
                int slot = Array.IndexOf(physicalOrder, pad == EliteDrumPad.Tom2 ? EliteDrumPad.Ride : pad);
                float expected = strips[slot].transform.localPosition.x;
                if (extraChord)
                {
                    // The stray Tom 2 opens a second fret. Read its actual boundaries at
                    // this head's arrival, since both strips change shape during the return.
                    player.GetType().GetMethod("UpdateRollFrets", INSTANCE).Invoke(player, new object[] { note.Time });
                    var fret = Receptor(player, pad).transform;
                    float width = .4f * fret.localScale.x;
                    bool cymbal = pad == EliteDrumPad.Ride || pad == EliteDrumPad.RightCrash;
                    bool split = pad != EliteDrumPad.Ride || note.Tick == streams[0][1].Tick;
                    float shift = split ? width / 4f : Mathf.Max(0f, width - .4f) / 2f;
                    expected = fret.localPosition.x + (cymbal != lefty ? shift : -shift);
                    if (split)
                        Assert.That(right - left, Is.LessThanOrEqualTo(width / 2f + .002f),
                            "Each paired gem must fit inside its half of the moving fret.");
                }
                Assert.That((left + right) / 2f, Is.EqualTo(expected).Within(.015f),
                    $"At {note.Time}, {(EliteDrumPad)note.Pad} must follow its strip on {scenario}.");
            }
            // Each strip starts on its receptor, or on its own half if two roll lanes share it.
            for (int i = 0; i < strips.Length; i++)
            {
                var pad = physicalOrder[i];
                var first = streams[Array.IndexOf(pads, pad)][0];
                player.GetType().GetMethod("UpdateRollFrets", INSTANCE).Invoke(player, new object[] { first.Time });
                var fret = Receptor(player, pad).transform;
                float expected = fret.localPosition.x;
                if (!full && pad != EliteDrumPad.Ride)
                    expected += ((pad == EliteDrumPad.RightCrash) != lefty ? 1f : -1f) * .1f * fret.localScale.x;
                Assert.That(strips[i].transform.localPosition.x, Is.EqualTo(expected).Within(.005f));
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
