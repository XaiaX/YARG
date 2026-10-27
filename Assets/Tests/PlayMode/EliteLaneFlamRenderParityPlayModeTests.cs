using System;
using System.Collections;
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
    public sealed class EliteLaneFlamRenderParityPlayModeTests : EliteFlamRuntimeTestBase
    {
        private static EliteDrumNote SourceNote(EliteDrumPad pad, double time, int ordinal, bool flat)
        {
            uint tick = (uint) (time * 960);
            var source = new EliteDrumSourceDefinition($"render-parity-{ordinal}", ordinal, (int) pad, tick, 0, time);
            return new EliteDrumNote(pad, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent,
                EliteDrumsHatPedalType.Stomp, !flat, DrumNoteFlags.None, NoteFlags.None,
                EliteDrumsChannelFlag.None, time, tick, false, source, isFlatFlam: flat);
        }

        private static SongChart Chart(bool flat, bool withLane)
        {
            var member = SourceNote(EliteDrumPad.Snare, 1, 0, flat);
            var nonmember = SourceNote(EliteDrumPad.Snare, 1.5, 1, flat);
            var kick = SourceNote(EliteDrumPad.Kick, 2, 2, flat);
            var outsideKick = SourceNote(EliteDrumPad.Kick, 4, 3, flat);
            var chart = PreparedElitePlayerFixture.CreateChart(member, nonmember, kick, outsideKick);
            var tier = chart.AuthoredDrumSources.Tiers.Values.Single();
            var difficulty = (InstrumentDifficulty<EliteDrumNote>) tier.GetType()
                .GetField("_elite", INSTANCE).GetValue(tier);
            if (withLane)
                difficulty.SetEliteDrumNativeAuthoredLaneRecords(new[]
                {
                    new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                        960, 2880, new[] { member.SourceDefinition }),
                    new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_KickLane,
                        960, 2880, new[] { kick.SourceDefinition })
                });
            return chart;
        }

        private Component Spawn(EliteDrumNote note)
        {
            Fixture.Player.GetType().GetMethod("SpawnNote", INSTANCE)
                .Invoke(Fixture.Player, new object[] { note });
            return Fixture.Player.GetComponentsInChildren(
                Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                .Single(e => ReferenceEquals(e.GetType().GetProperty("NoteRef").GetValue(e), note));
        }

        private static void AssertSplitGroups(Component element, int expected)
        {
            var groups = (Array) element.GetType().GetField("_splitGemGroups", INSTANCE).GetValue(element);
            int active = groups.Cast<Component>().Count(group => group != null && group.gameObject.activeSelf);
            Assert.That(active, Is.EqualTo(expected),
                "The actual spawned theme groups must match prepared hand-flam intent.");
            Assert.That(element.GetComponentsInChildren<Renderer>()
                .Any(r => r.gameObject.activeInHierarchy), Is.True, "The note must render, not merely select a group.");
        }

        [UnityTest]
        public IEnumerator SourceMembershipSuppressesOnlyHandLaneMemberAndDoesNotLeakToAnotherPreparedChart()
        {
            foreach (bool flat in new[] { false, true })
            foreach (bool withLane in new[] { true, false, true })
            {
                Fixture = new InitializedElitePlayerFixture();
                yield return Fixture.Initialize(Chart(flat, withLane), Profile(), EliteDrumsLayoutPreset.Full);
                var notes = Playable.Notes;
                Assert.That(notes, Has.Count.EqualTo(4));
                Assert.That(notes[0].IsFlam || notes[0].IsFlatFlam, Is.EqualTo(!withLane));
                Assert.That(notes[1].IsFlam || notes[1].IsFlatFlam, Is.True,
                    "A same-pad note inside the time bounds but absent from MemberSources is not a lane member.");
                Assert.That(notes[0].DrumFlags, Is.EqualTo(DrumNoteFlags.None));
                Assert.That(notes[1].DrumFlags, Is.EqualTo(DrumNoteFlags.None));
                AssertSplitGroups(Spawn(notes[0]), withLane ? 0 : 2);
                AssertSplitGroups(Spawn(notes[1]), 2);
                foreach (var kick in notes.Skip(2))
                {
                    bool laneMember = withLane && ReferenceEquals(kick, notes[2]);
                    Assert.That(kick.IsFlam || kick.IsFlatFlam, Is.EqualTo(!laneMember),
                        "Only the authored same-pad kick-lane member falls back to an ordinary kick.");
                    var element = Spawn(kick);
                    AssertSplitGroups(element, 0);
                    var groups = (Array) element.GetType().GetField("NoteGroups", INSTANCE).GetValue(element);
                    var selected = element.GetType().BaseType.GetField("NoteGroup", INSTANCE).GetValue(element);
                    int expected = (int)element.GetType().GetMethod("GetGemGroup",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                        .Invoke(null, new object[] { kick, true });
                    Assert.That(selected, Is.SameAs(groups.GetValue(expected)),
                        "Only an effective kick flam selects the stacked model; lane members use ordinary kicks.");
                }
                yield return Fixture.Dispose();
                Fixture = null;
            }
        }
    }
}
