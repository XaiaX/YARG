// pattern: Imperative Shell
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteCymbalLaneUnionPlayModeTests
    {
        [UnityTest]
        public IEnumerator TomRollDuringContractionRetainsItsLengthInTheLaneShader()
        {
            yield return ShowStaggeredRolls(
                (EliteDrumPad.Tom3, PhraseType.EliteDrums_Tom3Lane, .125, .875),
                (EliteDrumPad.RightCrash, PhraseType.EliteDrums_RightCrashLane, .25, 1),
                (EliteDrumPad.Tom3, PhraseType.EliteDrums_Tom3Lane, 1.125, 1.875));
            var tom = SpawnedLanes().Single(lane => StartOf(lane) == 1.125);
            var update = Fixture.Player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            var fret = Receptor(Fixture.Player, EliteDrumPad.Tom3).transform;
            foreach (var time in new[] { 1.25, 1.5, 1.75 })
            {
                update.Invoke(Fixture.Player, new object[] { time });
                var section = LaneCrossSection(tom, time);
                Assert.That(section.Center, Is.EqualTo(fret.localPosition.x).Within(.012f));
                Assert.That(section.Width, Is.EqualTo(.97f * .4f * fret.localScale.x).Within(.012f));
            }
        }

        [UnityTest]
        public IEnumerator LaterNeighborKeepsItsOwnFretAfterTheTripleEnds()
        {
            // Right crash and Tom 3 share the rightmost fret. Ride spans the collision
            // and the later left crash, but the left crash never touches the triple.
            yield return ShowStaggeredRolls(
                (EliteDrumPad.Tom3, PhraseType.EliteDrums_Tom3Lane, .125, .5),
                (EliteDrumPad.RightCrash, PhraseType.EliteDrums_RightCrashLane, .1875, .5625),
                (EliteDrumPad.Ride, PhraseType.EliteDrums_RideLane, .25, .8125),
                (EliteDrumPad.LeftCrash, PhraseType.EliteDrums_LeftCrashLane, .6875, 1));

            var strips = SpawnedLanes();
            Assert.That(strips, Has.Length.EqualTo(4));
            // The left crash is on an independent donor fret during recovery. It
            // follows that fret's moving edges throughout its own strip.
            var leftCrash = strips.Single(lane => StartOf(lane) == .6875);
            foreach (var time in new[] { .75d, .9375d })
            {
                Fixture.Player.GetType().GetMethod("UpdateRollFrets", INSTANCE).Invoke(Fixture.Player, new object[] { time });
                var fret = Receptor(Fixture.Player, EliteDrumPad.LeftCrash).transform;
                var section = LaneCrossSection(leftCrash, time);
                Assert.That(section.Center, Is.EqualTo(fret.localPosition.x).Within(.012f));
                Assert.That(section.Width, Is.EqualTo(.97f * .4f * fret.localScale.x).Within(.012f));
            }
        }

        [UnityTest]
        public IEnumerator WiderPairTailMakesRoomForALaterNeighborItWouldCover()
        {
            // The crash ends before the ride starts. The still-active Tom 3 would
            // overhang the ride's fret if the isolated pair kept its wider placement.
            yield return ShowStaggeredRolls(
                (EliteDrumPad.Tom3, PhraseType.EliteDrums_Tom3Lane, .125, .8125),
                (EliteDrumPad.RightCrash, PhraseType.EliteDrums_RightCrashLane, .1875, .375),
                (EliteDrumPad.Ride, PhraseType.EliteDrums_RideLane, .625, 1));

            var strips = SpawnedLanes();
            Assert.That(strips, Has.Length.EqualTo(3));
            var ride = LaneCrossSection(strips.Single(lane => StartOf(lane) == .625d), .71875d);
            var tom = LaneCrossSection(strips.Single(lane => StartOf(lane) == .125d), .71875d);
            Assert.That(ride.Center, Is.EqualTo(.19f).Within(.006f));
            Assert.That(ride.Width, Is.EqualTo(.97f * .34f).Within(.006f));
            Assert.That(tom.Center, Is.EqualTo(.52f).Within(.006f));
            Assert.That(tom.Width, Is.EqualTo(.97f * .32f).Within(.006f));
            Assert.That(ride.Center + ride.Width / 2f, Is.LessThan(tom.Center - tom.Width / 2f));
        }

        private static double StartOf(Component lane) => (double)lane.GetType().GetProperty("ElementTime").GetValue(lane);

        private (float Center, float Width) LaneCrossSection(Component lane, double time)
        {
            var shaped = lane.GetComponentsInChildren<MeshFilter>()
                .FirstOrDefault(filter => filter.name == "Shaped roll" && filter.GetComponent<MeshRenderer>().enabled);
            var skin = lane.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var baked = shaped == null ? new Mesh() : null;
            try
            {
                var mesh = shaped != null ? shaped.sharedMesh : baked;
                if (shaped == null) skin.BakeMesh(baked);
                // Both lane materials project object-space XY onto Z=0 before drawing.
                // A strip can have a correct CPU bounding box but collapse to a bar in the shader.
                var vertices = mesh.vertices.Select(vertex => lane.transform.InverseTransformPoint(shaped != null
                    ? shaped.transform.TransformPoint(new Vector3(vertex.x, vertex.y, 0f))
                    : skin.transform.position + skin.transform.rotation * new Vector3(vertex.x, vertex.y, 0f))).ToArray();
                float speed = (float)Fixture.Player.GetType().GetProperty("NoteSpeed").GetValue(Fixture.Player);
                float z = (float)(time - StartOf(lane)) * speed;
                int[] triangles = mesh.triangles;
                var intersections = new List<float>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    for (int side = 0; side < 3; side++)
                    {
                        var a = vertices[triangles[i + side]];
                        var b = vertices[triangles[i + (side + 1) % 3]];
                        if ((a.z < z) == (b.z < z)) continue;
                        intersections.Add(Mathf.Lerp(a.x, b.x, (z - a.z) / (b.z - a.z)));
                    }
                }
                Assert.That(intersections, Is.Not.Empty, $"No rendered lane cross-section at {time}s");
                float min = intersections.Min(), max = intersections.Max();
                return (lane.transform.localPosition.x + (min + max) / 2f, max - min);
            }
            finally { if (baked != null) UnityEngine.Object.DestroyImmediate(baked); }
        }

        private IEnumerator ShowStaggeredRolls(params (EliteDrumPad Pad, PhraseType Phrase, double Start, double End)[] rolls)
        {
            var streams = rolls.Select((roll, i) => Stream($"roll-{i}", roll.Pad, roll.Start, roll.End)).ToArray();
            var song = PreparedElitePlayerFixture.CreateChart(streams.SelectMany(stream => stream)
                .OrderBy(note => note.Time).ToArray());
            var tier = song.AuthoredDrumSources.Tiers.Values.Single();
            var authored = (InstrumentDifficulty<EliteDrumNote>)tier.GetType().GetField("_elite", INSTANCE).GetValue(tier);
            authored.SetEliteDrumNativeAuthoredLaneRecords(streams.Select((notes, i) =>
                new EliteDrumNativeAuthoredLaneRecord(i, rolls[i].Phrase, notes[0].Tick, notes[^1].Tick,
                    notes.Select(note => note.SourceDefinition))).ToArray());
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(song, Profile(), EliteDrumsLayoutPreset.Compact, noFail: true);
            foreach (var note in Playable.Notes)
                Fixture.Player.GetType().GetMethod("OnNoteSpawned", INSTANCE).Invoke(Fixture.Player, new object[] { note });
            yield return null;
        }
    }
}
