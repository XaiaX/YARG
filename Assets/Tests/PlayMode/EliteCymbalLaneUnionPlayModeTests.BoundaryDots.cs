// pattern: Imperative Shell
using System;
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
        public IEnumerator BoundaryDotsArriveOnEveryPhysicalSeamDuringMergedTransitions(
            [ValueSource(nameof(MirroredLayouts))] bool lefty)
        {
            var chart = CreateBoundaryDotTransitionChart();
            var profile = Profile();
            profile.LeftyFlip = lefty;
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(chart, profile, EliteDrumsLayoutPreset.Compact,
                noFail: true, enableLanes: false);

            var player = Fixture.Player;
            var update = player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
            var dots = player.transform.Find("Elite fret boundary dots");
            Assert.That(dots, Is.Not.Null);
            var mesh = dots.GetComponent<MeshFilter>().sharedMesh;
            var noteSpeed = (float)player.GetType().GetProperty("NoteSpeed").GetValue(player);

            var settings = Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetValue(null);
            var spacingProperty = settings.GetType().GetProperty("EliteFretBoundaryDotSpacing");
            Assert.That(spacingProperty, Is.Not.Null, "Gameplay needs a fret boundary dot spacing setting.");
            var spacing = spacingProperty.GetValue(settings);
            var valueProperty = spacing.GetType().GetProperty("Value");
            var setSpacing = spacing.GetType().GetMethod("SetValueWithoutNotify");
            var previous = valueProperty.GetValue(spacing);
            var alwaysOption = settings.GetType().GetProperty("AlwaysShowEliteFretBoundaryDots").GetValue(settings);
            var setAlways = alwaysOption.GetType().GetMethod("SetValueWithoutNotify");
            var wasAlwaysOn = alwaysOption.GetType().GetProperty("Value").GetValue(alwaysOption);
            try
            {
                setAlways.Invoke(alwaysOption, new object[] { false });
                foreach (int denominator in new[] { 32, 16, 64, 128, 8 })
                {
                    setSpacing.Invoke(spacing, new[] { Enum.ToObject(valueProperty.PropertyType, denominator) });
                    update.Invoke(player, new object[] { 1d });
                    var vertices = mesh.vertices;
                    float[] centers = Enumerable.Range(0, vertices.Length / 4)
                        .Select(i => (vertices[4 * i].z + vertices[4 * i + 2].z) * .5f)
                        .Where(z => z <= -2f + .5f * noteSpeed + .0001f).Distinct().ToArray();
                    var expected = Enumerable.Range(0, denominator / 4 + 1)
                        .Select(i => -2f + (float)i * 2 / denominator * noteSpeed);
                    Assert.That(centers, Is.EqualTo(expected).Within(.0001f), $"Rows at 1/{denominator}");
                }

                var frets = new[] { EliteDrumPad.Snare, EliteDrumPad.HiHat, EliteDrumPad.Tom1,
                    EliteDrumPad.Tom2, EliteDrumPad.Tom3 };
                if (lefty) Array.Reverse(frets);
                var columns = Array.ConvertAll(frets, pad => Receptor(player, pad).transform);

                // Check the sparsest setting across both overlapping and merged ramps.
                // The tempo change halves the interval between the final rows.
                foreach (double time in new[] { 1d, 1.25, 1.5, 1.75, 2d, 3d, 3.5, 4d, 5d, 5.25, 5.375, 5.5, 5.625 })
                {
                    AssertSeamsAt(time, new[] { 1, 2, 3, 4 });
                }
                // The two adjacent widened frets share their middle marker. The outer
                // rail is part of this hold, including when Lefty moves it to the left.
                foreach (double time in new[] { 2.25, 2.5, 2.75 })
                    AssertSeamsAt(time, lefty ? new[] { 0, 1, 2 } : new[] { 3, 4, 5 });
                foreach (double time in new[] { 4.25, 4.5, 4.75 })
                    AssertSeamsAt(time, lefty ? new[] { 0, 1 } : new[] { 4, 5 });
                foreach (double time in new[] { .75, 1.125, 2.125, 5.125, 5.75 })
                    Assert.That(CentersAt(time, time), Is.Empty, $"No dot row at {time}s");
                Assert.That(CentersAt(1, 1), Has.Count.EqualTo(4), "A backward seek restores previous rows.");

                setAlways.Invoke(alwaysOption, new object[] { true });
                AssertSeamsAt(2.5, lefty ? new[] { 0, 1, 2, 3, 4 } : new[] { 1, 2, 3, 4, 5 });
                AssertSeamsAt(3.5, new[] { 1, 2, 3, 4 });

                void AssertSeamsAt(double time, int[] seams)
                {
                    var approaching = CentersAt(time, time - .0625);
                    var arriving = CentersAt(time, time);
                    Assert.That(arriving, Has.Count.EqualTo(seams.Length), $"At {time}s");
                    Assert.That(approaching, Is.EqualTo(arriving).Within(.0001f), $"In flight toward {time}s");
                    for (int i = 0; i < seams.Length; i++)
                    {
                        int seam = seams[i];
                        float expected = seam == 0 ? -1f :
                            columns[seam - 1].localPosition.x + .2f * columns[seam - 1].localScale.x;
                        Assert.That(arriving[i], Is.EqualTo(expected).Within(.0001f), $"Seam {seam} at {time}s");
                    }
                }
            }
            finally
            {
                setAlways.Invoke(alwaysOption, new[] { wasAlwaysOn });
                setSpacing.Invoke(spacing, new[] { previous });
            }

            List<float> CentersAt(double rowTime, double visualTime)
            {
                update.Invoke(player, new object[] { visualTime });
                var vertices = mesh.vertices;
                var centers = new List<float>();
                float z = -2f + (float)(rowTime - visualTime) * noteSpeed;
                for (int i = 0; i < vertices.Length; i += 4)
                {
                    var center = (vertices[i] + vertices[i + 2]) * .5f;
                    if (Mathf.Abs(center.z - z) < .0001f) centers.Add(center.x);
                }
                return centers;
            }
        }

        [UnityTest]
        public IEnumerator HeldCenterDotsUseSavedFretColorsAndPreserveAlwaysOnGrid(
            [ValueSource(nameof(MirroredLayouts))] bool lefty)
        {
            var settings = Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetValue(null);
            var centerProperty = settings.GetType().GetProperty("ColorEliteHeldFretCenters");
            Assert.That(centerProperty, Is.Not.Null, "A setting enables colored center dots.");
            var center = centerProperty.GetValue(settings);
            var always = settings.GetType().GetProperty("AlwaysShowEliteFretBoundaryDots").GetValue(settings);
            var beforeCenter = center.GetType().GetProperty("Value").GetValue(center);
            var beforeAlways = always.GetType().GetProperty("Value").GetValue(always);
            var set = center.GetType().GetMethod("SetValueWithoutNotify");
            var profile = Profile();
            profile.LeftyFlip = lefty;
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Sparse held color slots");
            layout = EliteDrumsLayoutEditor.MoveColumn(layout, 3, 7);
            layout = EliteDrumsLayoutEditor.MoveColumn(layout, 4, 9);
            var occupied = Note(EliteDrumPad.Tom3, 2.75);
            // A converted off-grid head can share the MIDI tick and have a distinct arrival.
            occupied.AddChildNote(new EliteDrumNote(EliteDrumPad.Tom2, DrumNoteType.Neutral,
                EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None,
                NoteFlags.None, EliteDrumsChannelFlag.None, 2.7504, occupied.Tick, false));
            try
            {
                set.Invoke(center, new object[] { false });
                set.Invoke(always, new object[] { false });
                Fixture = new InitializedElitePlayerFixture();
                yield return Fixture.Initialize(CreateBoundaryDotTransitionChart(occupied), profile, layout,
                    noFail: true, enableLanes: false);

                var colors = new ColorProfile("Distinct saved fret tints");
                var orange = new Color32(212, 66, 26, 255);
                var green = new Color32(25, 171, 190, 255);
                colors.EliteDrums.Fret8 = System.Drawing.Color.FromArgb(orange.r, orange.g, orange.b);
                colors.EliteDrums.Fret10 = System.Drawing.Color.FromArgb(green.r, green.g, green.b);
                Fixture.Session.GetType().GetProperty("ColorProfile").SetValue(Fixture.Session, colors);
                var player = Fixture.Player;
                var tom2 = Receptor(player, EliteDrumPad.Tom2).transform;
                var tom3 = Receptor(player, EliteDrumPad.Tom3).transform;

                var defaultDots = AtStrikeLine(2.5);
                Assert.That(defaultDots, Has.Length.EqualTo(3));
                Assert.That(defaultDots.Select(dot => dot.Tint.a), Is.All.EqualTo((byte)0));
                set.Invoke(center, new object[] { true });

                var held = AtStrikeLine(2.5);
                Assert.That(held.Select(dot => dot.Tint), Is.EqualTo(lefty ? new[] { green, orange } : new[] { orange, green }));
                Assert.That(held.Single(dot => dot.Tint.Equals(orange)).X, Is.EqualTo(tom2.localPosition.x).Within(.0001f));
                Assert.That(held.Single(dot => dot.Tint.Equals(green)).X, Is.EqualTo(tom3.localPosition.x).Within(.0001f));
                Assert.That(AtStrikeLine(2.75).Select(dot => dot.Tint), Is.EqualTo(new[] { orange }));
                var transitioning = AtStrikeLine(3.5);
                Assert.That(transitioning, Has.Length.EqualTo(4));
                Assert.That(transitioning.Select(dot => dot.Tint.a), Is.All.EqualTo((byte)0));
                Assert.That(AtStrikeLine(4.5).Select(dot => dot.Tint), Is.EqualTo(new[] { green }));

                set.Invoke(always, new object[] { true });
                var combined = AtStrikeLine(2.5);
                Assert.That(combined.Count(dot => dot.Tint.a == 0), Is.EqualTo(4));
                Assert.That(combined.Where(dot => dot.Tint.a != 0), Is.EqualTo(held));
                Assert.That(AtStrikeLine(2.75).Count(dot => dot.Tint.a == 0), Is.EqualTo(4));
                Assert.That(AtStrikeLine(3.5), Is.EqualTo(transitioning));

                (float X, Color32 Tint)[] AtStrikeLine(double time) => GetBoundaryDotsAtStrikeLine(player, time);
            }
            finally
            {
                set.Invoke(center, new[] { beforeCenter });
                set.Invoke(always, new[] { beforeAlways });
            }
        }

        [UnityTest]
        public IEnumerator AllFretCenterDotsRespectProjectedChordAndFootNoteFootprints(
            [ValueSource(nameof(MirroredLayouts))] bool lefty)
        {
            var settings = Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetValue(null);
            var allProperty = settings.GetType().GetProperty("ColorEliteAllFretCenters");
            Assert.That(allProperty, Is.Not.Null, "A setting enables all-fret center dots.");
            var all = allProperty.GetValue(settings);
            var held = settings.GetType().GetProperty("ColorEliteHeldFretCenters").GetValue(settings);
            var always = settings.GetType().GetProperty("AlwaysShowEliteFretBoundaryDots").GetValue(settings);
            var value = all.GetType().GetProperty("Value");
            var previous = new[] { value.GetValue(all), value.GetValue(held), value.GetValue(always) };
            var set = all.GetType().GetMethod("SetValueWithoutNotify");
            var profile = Profile();
            profile.LeftyFlip = lefty;
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Projected center dots");
            layout = EliteDrumsLayoutEditor.Place(layout, 3, "ride>left-crash");
            var projectedChord = Note(EliteDrumPad.Snare, 1.25);
            projectedChord.AddChildNote(Note(EliteDrumPad.LeftCrash, 1.25));
            projectedChord.AddChildNote(Note(EliteDrumPad.Ride, 1.25));
            var terminator = Note(EliteDrumPad.HatPedal, .5);
            terminator.HatPedalType = EliteDrumsHatPedalType.InvisibleTerminator;
            var chart = CreateBoundaryDotTransitionChart(terminator, Note(EliteDrumPad.HatPedal, .75),
                projectedChord, Note(EliteDrumPad.Kick, 1.5));
            try
            {
                set.Invoke(all, new object[] { true });
                set.Invoke(held, new object[] { false });
                set.Invoke(always, new object[] { false });
                Fixture = new InitializedElitePlayerFixture();
                yield return Fixture.Initialize(chart, profile, layout, noFail: true, enableLanes: false);

                AssertColoredCenters(1.25, EliteDrumPad.Tom1, EliteDrumPad.Tom3);
                Assert.That(Row(1.5), Is.Empty, "A full-width kick owns every center on its subdivision.");
                Assert.That(Row(2.5).Select(dot => dot.Tint.a), Is.EqualTo(new byte[] { 0, 0, 0 }));
                set.Invoke(held, new object[] { true });
                AssertColoredCenters(2.5, EliteDrumPad.Tom2, EliteDrumPad.Tom3);
                set.Invoke(always, new object[] { true });
                AssertColoredCenters(.5, EliteDrumPad.Snare, EliteDrumPad.HiHat, EliteDrumPad.Tom1,
                    EliteDrumPad.Tom2, EliteDrumPad.Tom3);
                AssertColoredCenters(.75, EliteDrumPad.Tom2, EliteDrumPad.Tom3);
                AssertColoredCenters(2.5, EliteDrumPad.Snare, EliteDrumPad.HiHat, EliteDrumPad.Tom1,
                    EliteDrumPad.Tom2, EliteDrumPad.Tom3);
                Assert.That(Row(1.5), Is.Empty, "Always Show must not replace a completely occupied row.");

                (float X, Color32 Tint)[] Row(double time) => GetBoundaryDotsAtStrikeLine(Fixture.Player, time);

                void AssertColoredCenters(double time, params EliteDrumPad[] pads)
                {
                    var row = Row(time);
                    var expected = pads.Select(pad => Receptor(Fixture.Player, pad).transform.localPosition.x)
                        .OrderBy(x => x).ToArray();
                    Assert.That(row.Select(dot => dot.X), Is.EqualTo(expected).Within(.0001f), $"Head footprints at {time}s");
                    Assert.That(row.Select(dot => dot.Tint.a), Is.All.EqualTo((byte)255));
                }
            }
            finally
            {
                set.Invoke(all, new[] { previous[0] });
                set.Invoke(held, new[] { previous[1] });
                set.Invoke(always, new[] { previous[2] });
            }
        }

        private static (float X, Color32 Tint)[] GetBoundaryDotsAtStrikeLine(Component player, double time)
        {
            player.GetType().GetMethod("UpdateRollFrets", INSTANCE).Invoke(player, new object[] { time });
            var mesh = player.transform.Find("Elite fret boundary dots").GetComponent<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            var tints = mesh.colors32;
            return Enumerable.Range(0, vertices.Length / 4)
                .Select(i => (Center: (vertices[4 * i] + vertices[4 * i + 2]) * .5f, Tint: tints[4 * i]))
                .Where(dot => Mathf.Abs(dot.Center.z + 2f) < .0001f)
                .Select(dot => (dot.Center.x, dot.Tint)).ToArray();
        }

        private static SongChart CreateBoundaryDotTransitionChart(params EliteDrumNote[] additional)
        {
            var tom3 = Stream("tom3", EliteDrumPad.Tom3, 3, 5);
            var crash = Stream("right-crash", EliteDrumPad.RightCrash, 3, 5);
            var tom2 = Stream("tom2", EliteDrumPad.Tom2, 3)[0];
            var ride = Stream("ride", EliteDrumPad.Ride, 3)[0];
            for (int i = 0; i < tom3.Length; i++)
                tom3[i].AddChildNote(crash[i]);
            tom3[0].AddChildNote(tom2);
            tom3[0].AddChildNote(ride);
            var chart = PreparedElitePlayerFixture.CreateChart(tom3.Concat(additional).OrderBy(note => note.Time).ToArray());
            chart.SyncTrack.Tempos.Add(new TempoChange(240, 5.25, 5040));
            foreach (double time in new[] { 2d, 3d, 4d, 5d })
                chart.SyncTrack.Beatlines.Add(new Beatline(BeatlineType.Strong, time, (uint)(time * 960)));
            chart.SyncTrack.Beatlines.Add(new Beatline(BeatlineType.Weak, 12, chart.SyncTrack.TimeToTick(12)));
            chart.SyncTrack.FinishLoading(chart.SyncTrack.TimeToTick(12));
            return chart;
        }

        [UnityTest]
        public IEnumerator AlwaysShowBoundaryDotsAddsTheVisibleGridToANonExpandingLayout()
        {
            var settings = Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetValue(null);
            var optionProperty = settings.GetType().GetProperty("AlwaysShowEliteFretBoundaryDots");
            Assert.That(optionProperty, Is.Not.Null);
            var option = optionProperty.GetValue(settings);
            var value = option.GetType().GetProperty("Value");
            var set = option.GetType().GetMethod("SetValueWithoutNotify");
            var previous = value.GetValue(option);
            var chart = PreparedElitePlayerFixture.CreateChart(Stream("snare", EliteDrumPad.Snare, 2, 6));
            var profile = Profile();
            try
            {
                set.Invoke(option, new object[] { false });
                Fixture = new InitializedElitePlayerFixture();
                yield return Fixture.Initialize(chart, profile, EliteDrumsLayoutPreset.Full, noFail: true);
                var player = Fixture.Player;
                var update = player.GetType().GetMethod("UpdateRollFrets", INSTANCE);
                update.Invoke(player, new object[] { 1d });
                Assert.That(player.transform.Find("Elite fret boundary dots"), Is.Null);

                set.Invoke(option, new object[] { true });
                update.Invoke(player, new object[] { 3d });
                var dots = player.transform.Find("Elite fret boundary dots");
                Assert.That(dots, Is.Not.Null);
                var mesh = dots.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(MarkersAtStrikeLine(mesh), Has.Length.EqualTo(7));
                update.Invoke(player, new object[] { 1d });
                Assert.That(MarkersAtStrikeLine(mesh), Is.EqualTo(new[] { -.75f, -.5f, -.25f, 0f, .25f, .5f, .75f })
                    .Within(.0001f));
                profile.NoteSpeed = .005f;
                update.Invoke(player, new object[] { 1d });
                Assert.That(mesh.vertexCount, Is.InRange(28, 512 * 7 * 4), "Dense normal rows stay bounded.");
                set.Invoke(option, new object[] { false });
                update.Invoke(player, new object[] { 1d });
                Assert.That(mesh.vertexCount, Is.Zero);
            }
            finally { set.Invoke(option, new[] { previous }); }

            static float[] MarkersAtStrikeLine(Mesh mesh)
            {
                var vertices = mesh.vertices;
                return Enumerable.Range(0, vertices.Length / 4)
                    .Select(i => (vertices[4 * i] + vertices[4 * i + 2]) * .5f)
                    .Where(center => Mathf.Abs(center.z + 2f) < .0001f).Select(center => center.x).ToArray();
            }
        }
    }
}
