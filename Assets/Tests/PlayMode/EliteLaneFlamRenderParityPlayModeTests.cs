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

        private static void AssertFusedFlam(Component element, string expected)
        {
            var note = element.GetComponentsInChildren(Production("YARG.Themes.ThemeNote")).Single();
            Assert.That(note.GetType().GetProperty("NoteType").GetValue(note).ToString(), Is.EqualTo(expected));
            AssertSplitGroups(element, 0);
        }

        [UnityTest]
        public IEnumerator FusedFlamsPreserveDepthAndHeightAndUseTheProportionalLaneOffset()
        {
            int index = 0;
            var chartNotes = (from dynamics in new[] { DrumNoteType.Ghost, DrumNoteType.Neutral, DrumNoteType.Accent }
                from shape in new[] { "single", "flat", "V" }
                select new EliteDrumNote(EliteDrumPad.Snare, dynamics, EliteDrumsHatState.Indifferent,
                    EliteDrumsHatPedalType.Stomp, shape == "V", DrumNoteFlags.None, NoteFlags.None,
                    EliteDrumsChannelFlag.None, 1d + index * .25d, (uint) (960 + index++ * 240), false,
                    isFlatFlam: shape == "flat")).ToArray();
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(PreparedElitePlayerFixture.CreateChart(chartNotes), Profile(), EliteDrumsLayoutPreset.Compact);
            var player = Fixture.Player;
            var layout = (EliteDrumsResolvedLayout) player.GetType().GetProperty("Layout").GetValue(player);
            float laneWidth = layout.GetLaneWidth(2f);
            float laneCenter = layout.GetSourceX("snare", 2f, false);

            Bounds Measure(EliteDrumNote note)
            {
                var element = Spawn(note);
                var points = element.GetComponentsInChildren<MeshFilter>()
                    .Where(filter => filter.GetComponent<MeshRenderer>().enabled)
                    .SelectMany(filter => filter.sharedMesh.vertices.Select(vertex =>
                        player.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex)))).ToArray();
                Assert.That(points, Is.Not.Empty);
                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (var point in points) bounds.Encapsulate(point);
                return bounds;
            }

            var notes = Playable.Notes;
            for (int i = 0; i < notes.Count; i += 3)
            {
                var single = Measure(notes[i]);
                for (int shape = 1; shape <= 2; shape++)
                {
                    var flam = Measure(notes[i + shape]);
                    Assert.That(flam.size.x, Is.EqualTo(single.size.x * 1.5f).Within(.0001f), "In-game width");
                    Assert.That(flam.size.y, Is.EqualTo(single.size.y).Within(.0001f), "In-game height");
                    Assert.That(flam.size.z, Is.EqualTo(single.size.z).Within(.0001f), "In-game depth");
                    float expected = laneCenter / (1f - laneWidth * .5f) * (1f - flam.extents.x);
                    Assert.That(flam.center.x, Is.EqualTo(expected).Within(.0001f), "In-game offset");
                }
            }
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
                string fused = flat ? "EliteFlatFlam" : "EliteVFlam";
                AssertFusedFlam(Spawn(notes[0]), withLane ? "Normal" : fused);
                AssertFusedFlam(Spawn(notes[1]), fused);
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
