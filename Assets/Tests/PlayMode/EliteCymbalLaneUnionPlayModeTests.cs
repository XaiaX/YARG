// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteCymbalLaneUnionPlayModeTests : EliteFlamRuntimeTestBase
    {
        [UnityTest]
        public IEnumerator OverlappingMergedRollsRenderOnePhysicalStrip()
        {
            var left = Stream("left", EliteDrumPad.LeftCrash, 1d, 2d, 3d);
            var ride = Stream("ride", EliteDrumPad.Ride, 1.5d, 2.5d, 3.5d);
            var song = PreparedElitePlayerFixture.CreateChart(left.Concat(ride).OrderBy(note => note.Time).ToArray());
            var tier = song.AuthoredDrumSources.Tiers.Values.Single();
            var authored = (InstrumentDifficulty<EliteDrumNote>)tier.GetType().GetField("_elite", INSTANCE).GetValue(tier);
            authored.SetEliteDrumNativeAuthoredLaneRecords(new[]
            {
                new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_LeftCrashLane,
                    left[0].Tick, left[^1].Tick, left.Select(note => note.SourceDefinition)),
                new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_RideLane,
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

        private static EliteDrumNote[] Stream(string id, EliteDrumPad pad, params double[] times) =>
            times.Select((time, index) => new EliteDrumNote(pad, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, (uint)(time * 960), false,
                new EliteDrumSourceDefinition(id, index, (int)pad, (uint)(time * 960), 0, time))).ToArray();
    }
}
