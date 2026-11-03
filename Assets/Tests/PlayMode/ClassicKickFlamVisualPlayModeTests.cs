// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    public sealed class ClassicKickFlamVisualPlayModeTests : EliteFlamRuntimeTestBase
    {
        private InitializedClassicPlayerFixture _classic;
        private readonly List<GameObject> _previewObjects = new();

        [UnityTearDown]
        public IEnumerator DisposeClassic()
        {
            foreach (var value in _previewObjects) if (value != null) UnityEngine.Object.DestroyImmediate(value);
            _previewObjects.Clear();
            if (_classic != null) yield return _classic.Dispose();
            _classic = null;
        }

        private static YargProfile ClassicProfile(Instrument instrument, bool lefty, DrumsHighwayItem[] order = null)
        {
            var profile = new YargProfile
            {
                GameMode = instrument == Instrument.FiveLaneDrums ? GameMode.FiveLaneDrums : GameMode.FourLaneDrums,
                CurrentInstrument = instrument, PreferredInstrument = instrument, CurrentDifficulty = Difficulty.Hard,
                LeftyFlip = lefty, UseCymbalModels = true
            };
            if (order != null)
            {
                if (instrument == Instrument.ProDrums) profile.ProDrumsHighwayOrdering = order;
                else if (instrument == Instrument.FiveLaneDrums) profile.FiveLaneDrumsHighwayOrdering = order;
                else profile.FourLaneDrumsHighwayOrdering = order;
            }
            return profile;
        }

        private static SongChart Chart(Instrument instrument, params DrumNote[] notes)
        {
            var chart = new SongChart(480);
            chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));
            chart.SyncTrack.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 16, 4));
            var difficulty = new InstrumentDifficulty<DrumNote>(instrument, Difficulty.Hard);
            difficulty.Notes.AddRange(notes);
            chart.GetDrumsTrack(instrument).AddDifficulty(Difficulty.Hard, difficulty);
            return chart;
        }

        private static DrumNote KickChord(double time, params int[] hands)
        {
            var kick = new DrumNote(0, DrumNoteType.Neutral, DrumNoteFlags.Flam, NoteFlags.None, time, (uint)(time * 960));
            foreach (int pad in hands)
                kick.AddChildNote(new DrumNote(pad, DrumNoteType.Accent, DrumNoteFlags.None, NoteFlags.None, time, kick.Tick));
            return kick;
        }

        private object Pool => _classic.Player.GetType().GetField("NotePool", INSTANCE).GetValue(_classic.Player);
        private IList<DrumNote> Notes => (IList<DrumNote>)_classic.Player.GetType().GetProperty("Notes", INSTANCE).GetValue(_classic.Player);
        private Component Spawn(DrumNote note)
        {
            _classic.Player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(_classic.Player, new object[] { note });
            return (Component)Pool.GetType().GetMethod("GetByKey").Invoke(Pool, new object[] { note });
        }
        private static Component VisibleGroup(Component element) => element.GetComponentsInChildren(
            Production("YARG.Gameplay.Visuals.NoteGroup")).Single(group => group.gameObject.activeInHierarchy);
        private static string ModelType(Component element)
        {
            var model = VisibleGroup(element).GetComponentInChildren(Production("YARG.Themes.ThemeNote"));
            return model.GetType().GetProperty("NoteType").GetValue(model).ToString();
        }
        private static float CenterIn(Transform space, MeshFilter mesh) =>
            space.InverseTransformPoint(mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center)).x;
        private static float WidthIn(Transform space, MeshFilter mesh)
        {
            var matrix = space.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
            var b = mesh.sharedMesh.bounds;
            var xs = Enumerable.Range(0, 8).Select(i => matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))).x).ToArray();
            return xs.Max() - xs.Min();
        }
        private static void AssertEndpoints(Component element, int lanes, float left, float right)
        {
            Assert.That(ModelType(element), Is.EqualTo("KickFlam"));
            var meshes = VisibleGroup(element).GetComponentsInChildren<MeshFilter>();
            var caps = meshes.Where(mesh => mesh.name.Contains("inner housing"))
                .OrderBy(mesh => CenterIn(element.transform.parent, mesh)).ToArray();
            Assert.That(caps, Has.Length.EqualTo(2));
            Assert.That(CenterIn(element.transform.parent, caps[0]), Is.EqualTo(left * 2f / lanes - 1f).Within(.0001f));
            Assert.That(CenterIn(element.transform.parent, caps[1]), Is.EqualTo(right * 2f / lanes - 1f).Within(.0001f));
            var outer = meshes.Where(mesh => mesh.name.Contains("housing") && !mesh.name.Contains("inner"))
                .OrderBy(mesh => CenterIn(element.transform.parent, mesh)).ToArray();
            Assert.That(outer, Has.Length.EqualTo(2));
            Assert.That(CenterIn(element.transform.parent, outer[0]) - WidthIn(element.transform.parent, outer[0]) * .5f,
                Is.EqualTo(-1f).Within(.0001f));
            Assert.That(CenterIn(element.transform.parent, outer[1]) + WidthIn(element.transform.parent, outer[1]) * .5f,
                Is.EqualTo(1f).Within(.0001f));
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab");
            var gallery = asset.transform.Find(lanes == 5 ? "Five Lane Notes" : "Four Lane Notes");
            var source = gallery.GetComponentsInChildren(Production("YARG.Themes.ThemeNote"), true).Single(model =>
                model.GetType().GetProperty("NoteType").GetValue(model).ToString() == "KickFlam" &&
                !(bool)model.GetType().GetProperty("StarPowerVariant").GetValue(model));
            var sourceMeshes = source.GetComponentsInChildren<MeshFilter>();
            MeshFilter[] Ordered(IEnumerable<MeshFilter> values, Transform space) =>
                values.OrderBy(mesh => CenterIn(space, mesh)).ToArray();
            var sourceCaps = Ordered(sourceMeshes.Where(mesh => mesh.name.Contains("inner housing")), source.transform.parent);
            var sourceOuter = Ordered(sourceMeshes.Where(mesh => mesh.name.Contains("housing") && !mesh.name.Contains("inner")), source.transform.parent);
            var beams = Ordered(meshes.Where(mesh => mesh.name.Contains("beam")), element.transform.parent);
            var sourceBeams = Ordered(sourceMeshes.Where(mesh => mesh.name.Contains("beam")), source.transform.parent);
            for (int side = 0; side < 2; side++)
            {
                Assert.That(WidthIn(element.transform.parent, caps[side]),
                    Is.EqualTo(WidthIn(source.transform.parent, sourceCaps[side])).Within(.0001f), "Inner housing thickness is authored, not stretched.");
                Assert.That(WidthIn(element.transform.parent, outer[side]),
                    Is.EqualTo(WidthIn(source.transform.parent, sourceOuter[side])).Within(.0001f), "Outer housing thickness is authored, not stretched.");
                float innerSign = side == 0 ? 1f : -1f;
                float actualInnerOverlap = CenterIn(element.transform.parent, beams[side]) + innerSign * WidthIn(element.transform.parent, beams[side]) * .5f
                    - CenterIn(element.transform.parent, caps[side]);
                float authoredInnerOverlap = CenterIn(source.transform.parent, sourceBeams[side]) + innerSign * WidthIn(source.transform.parent, sourceBeams[side]) * .5f
                    - CenterIn(source.transform.parent, sourceCaps[side]);
                Assert.That(actualInnerOverlap, Is.EqualTo(authoredInnerOverlap).Within(.0001f), "Beam end remains seated in the inner housing.");
                float actualOuterOverlap = CenterIn(element.transform.parent, beams[side]) - innerSign * WidthIn(element.transform.parent, beams[side]) * .5f
                    - CenterIn(element.transform.parent, outer[side]);
                float authoredOuterOverlap = CenterIn(source.transform.parent, sourceBeams[side]) - innerSign * WidthIn(source.transform.parent, sourceBeams[side]) * .5f
                    - CenterIn(source.transform.parent, sourceOuter[side]);
                Assert.That(actualOuterOverlap, Is.EqualTo(authoredOuterOverlap).Within(.0001f), "Beam end remains seated in the outer housing.");
            }
        }

        [UnityTest]
        public IEnumerator ResolvedProAndFourLaneChordsKeepBothKickEndsVisibleThroughPoolAndStarPowerReuse()
        {
            foreach (var instrument in new[] { Instrument.FourLaneDrums, Instrument.ProDrums })
            foreach (bool lefty in new[] { false, true })
            {
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(instrument,
                    KickChord(1, 2, 3), KickChord(2, 3, 4), KickChord(3, 2, 4), KickChord(4), KickChord(5, 2),
                    new DrumNote(0, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 6d, 5760)),
                    ClassicProfile(instrument, lefty));
                var expected = new[] { (.5f, 3.5f), (.5f, 1.5f), (.5f, 2.5f), (1.5f, 2.5f), (1.5f, 2.5f) };
                Component reused = null;
                for (int index = 0; index < Notes.Count; index++)
                {
                    Pool.GetType().GetMethod("ReturnAllObjects").Invoke(Pool, null);
                    var note = Notes[index];
                    var element = Spawn(note);
                    if (reused != null) Assert.That(element, Is.SameAs(reused));
                    reused = element;
                    if (!note.IsKickFlam)
                    {
                        Assert.That(ModelType(element), Is.EqualTo("Kick"), "Reusing a split-flam element must restore the ordinary kick.");
                        continue;
                    }
                    var pair = expected[index];
                    if (lefty) pair = (4f - pair.Item2, 4f - pair.Item1);
                    foreach (bool star in new[] { false, true, false })
                    {
                        if (star) note.ActivateFlag(NoteFlags.StarPower); else note.ClearFlag(NoteFlags.StarPower);
                        element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                        AssertEndpoints(element, 4, pair.Item1, pair.Item2);
                    }
                    if (Environment.GetEnvironmentVariable("YARG_CLASSIC_DRUM_CAPTURE_DIRECTORY") != null)
                    {
                        var members = note.ChildNotes.Select(Spawn).ToArray();
                        Capture(element, $"{instrument}-kickflam-chord{index}-lefty{lefty}");
                        foreach (var member in members) Pool.GetType().GetMethod("Return").Invoke(Pool, new object[] { member });
                    }
                }
                yield return _classic.Dispose();
                _classic = null;
            }
        }

        [UnityTest]
        public IEnumerator SplitCustomProOrderUsesActualDrumAndCymbalColumns()
        {
            var order = new[] { DrumsHighwayItem.GreenDrum, DrumsHighwayItem.YellowCymbal, DrumsHighwayItem.Red,
                DrumsHighwayItem.BlueDrum, DrumsHighwayItem.YellowDrum, DrumsHighwayItem.GreenCymbal, DrumsHighwayItem.BlueCymbal };
            foreach (bool lefty in new[] { false, true })
            {
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(Instrument.ProDrums, KickChord(1, 4, 6), KickChord(2, 1, 7)),
                    ClassicProfile(Instrument.ProDrums, lefty, order));
                var expected = new[] { (2.5f, 3.5f), (3.5f, 4.5f) };
                for (int index = 0; index < Notes.Count; index++)
                {
                    Pool.GetType().GetMethod("ReturnAllObjects").Invoke(Pool, null);
                    var element = Spawn(Notes[index]);
                    var pair = expected[index];
                    if (lefty) pair = (7f - pair.Item2, 7f - pair.Item1);
                    AssertEndpoints(element, 7, pair.Item1, pair.Item2);
                    foreach (var hand in Notes[index].ChildNotes) Spawn(hand);
                    Capture(element, $"ProDrums-kickflam-split7-chord{index}-lefty{lefty}");
                }
                yield return _classic.Dispose();
                _classic = null;
            }
        }

        [UnityTest]
        public IEnumerator FiveLaneKeepsFixedSplitBarsAndDedicatedFlamsStayStacked()
        {
            foreach (bool dedicated in new[] { false, true })
            {
                var order = new[] { DrumsHighwayItem.Red, DrumsHighwayItem.Yellow, DrumsHighwayItem.Blue,
                    DrumsHighwayItem.Orange, DrumsHighwayItem.Green };
                if (dedicated) order = order.Concat(new[] { DrumsHighwayItem.Kick }).ToArray();
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(Instrument.FiveLaneDrums, KickChord(1, 2, 3)),
                    ClassicProfile(Instrument.FiveLaneDrums, false, order));
                var element = Spawn(Notes[0]);
                if (dedicated)
                {
                    Assert.That(ModelType(element), Is.EqualTo("DedicatedLaneKickFlam"));
                    AssertStackedBeams(element);
                }
                else AssertEndpoints(element, 5, 1.5f, 3.5f);
                foreach (var hand in Notes[0].ChildNotes) Spawn(hand);
                Capture(element, "FiveLane-kickflam-dedicated" + dedicated);
                yield return _classic.Dispose();
                _classic = null;
            }
        }

        [UnityTest]
        public IEnumerator ClassicPreviewUsesTheSameChordEndpointsAcrossStarPowerAndPoolReuse()
        {
            foreach (var instrument in new[] { Instrument.ProDrums, Instrument.FiveLaneDrums })
            {
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(instrument, KickChord(1)), ClassicProfile(instrument, false));
                var mode = instrument == Instrument.ProDrums ? GameMode.FourLaneDrums : GameMode.FiveLaneDrums;
                var preview = CreatePreview(mode);
                var dataType = Production("YARG.Settings.Preview.FakeNoteData");
                var kinds = Production("YARG.Themes.ThemeNoteType");
                object Data(int fret, bool kick)
                {
                    var data = Activator.CreateInstance(dataType);
                    dataType.GetField("Time").SetValue(data, 1d);
                    dataType.GetField("Fret").SetValue(data, fret);
                    dataType.GetField("CenterNote").SetValue(data, kick);
                    dataType.GetField("NoteType").SetValue(data, Enum.Parse(kinds, kick ? "KickFlam" : "Accent"));
                    return data;
                }
                Component previous = null;
                foreach (var handFrets in new[] { new[] { 2, 3 }, new[] { 3, 4 } })
                {
                    preview.Pool.GetType().GetMethod("ReturnAllObjects").Invoke(preview.Pool, null);
                    var data = Data(0, true);
                    var chord = Array.CreateInstance(dataType, 3);
                    chord.SetValue(data, 0);
                    chord.SetValue(Data(handFrets[0], false), 1);
                    chord.SetValue(Data(handFrets[1], false), 2);
                    preview.Player.GetType().GetMethod("MarkClassicKickFlamColumns", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { chord });
                    preview.Player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(preview.Player, new[] { data });
                    var note = (Component)preview.Pool.GetType().GetMethod("GetByKey").Invoke(preview.Pool, new[] { data });
                    if (previous != null) Assert.That(note, Is.SameAs(previous));
                    previous = note;
                    foreach (bool star in new[] { false, true, false })
                    {
                        preview.Player.GetType().GetProperty("ForceStarPowerNotes").SetValue(preview.Player, star);
                        note.GetType().GetMethod("OnSettingChanged").Invoke(note, null);
                        AssertEndpoints(note, instrument == Instrument.ProDrums ? 4 : 5,
                            instrument == Instrument.ProDrums ? .5f : 1.5f,
                            instrument == Instrument.ProDrums ? handFrets[0] == 2 ? 3.5f : 1.5f : 3.5f);
                    }
                }
                foreach (var value in _previewObjects) if (value != null) UnityEngine.Object.DestroyImmediate(value);
                _previewObjects.Clear();
                yield return _classic.Dispose();
                _classic = null;
            }
        }

        private static ColorProfile BeamPalette()
        {
            var colors = new ColorProfile("Independent classic kick-flam beams");
            var primary = System.Drawing.Color.FromArgb(90, 115, 140);
            var secondary = System.Drawing.Color.FromArgb(160, 70, 45);
            var primaryStar = System.Drawing.Color.FromArgb(40, 170, 80);
            var secondaryStar = System.Drawing.Color.FromArgb(140, 110, 200);
            colors.FourLaneDrums.KickNote = colors.FiveLaneDrums.KickNote = primary;
            colors.FourLaneDrums.DoubleKickNote = colors.FiveLaneDrums.DoubleKickNote = secondary;
            colors.FourLaneDrums.KickStarpower = colors.FiveLaneDrums.KickStarpower = primaryStar;
            colors.FourLaneDrums.DoubleKickStarpower = colors.FiveLaneDrums.DoubleKickStarpower = secondaryStar;
            colors.FourLaneDrums.KickActivationNote = colors.FiveLaneDrums.KickActivationNote = System.Drawing.Color.FromArgb(190, 100, 70);
            colors.FourLaneDrums.DoubleKickActivationNote = colors.FiveLaneDrums.DoubleKickActivationNote = System.Drawing.Color.FromArgb(70, 150, 190);
            colors.FourLaneDrums.Miss = colors.FiveLaneDrums.Miss = System.Drawing.Color.FromArgb(70, 60, 50);
            return colors;
        }

        private static void AssertBeamColors(Component element, System.Drawing.Color primary,
            System.Drawing.Color secondary, bool lefty, bool dedicated)
        {
            var model = VisibleGroup(element).GetComponentInChildren(Production("YARG.Themes.ThemeNote"));
            var expected = new[] { primary, secondary };
            for (int side = 0; side < 2; side++)
            {
                var entries = ((IEnumerable)model.GetType().GetProperty(side == 0 ? "ColoredMaterials" : "ColoredKick2Materials")
                    .GetValue(model)).Cast<object>().ToArray();
                Assert.That(entries, Is.Not.Empty, "Both kick beams must have independently colored materials.");
                var color = expected[lefty && !dedicated ? 1 - side : side];
                foreach (var entry in entries)
                {
                    var type = entry.GetType();
                    var renderer = (Renderer)type.GetField("Mesh").GetValue(entry);
                    var material = renderer.sharedMaterials[(int)type.GetField("MaterialIndex").GetValue(entry)];
                    float addition = (float)type.GetField("EmissionAddition").GetValue(entry);
                    Assert.That(material.color.r, Is.EqualTo(color.R / 255f + addition).Within(.001f), "Kick beam " + side + " red");
                    Assert.That(material.color.g, Is.EqualTo(color.G / 255f + addition).Within(.001f), "Kick beam " + side + " green");
                    Assert.That(material.color.b, Is.EqualTo(color.B / 255f + addition).Within(.001f), "Kick beam " + side + " blue");
                }
            }
        }

        [UnityTest]
        public IEnumerator KickFlamMaterialsUseBothExistingClassicKickPalettes()
        {
            foreach (var instrument in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums })
            foreach (bool dedicated in new[] { false, true })
            foreach (bool lefty in new[] { false, true })
            {
                var profile = ClassicProfile(instrument, lefty);
                if (dedicated)
                {
                    if (instrument == Instrument.FourLaneDrums)
                        profile.FourLaneDrumsHighwayOrdering = profile.FourLaneDrumsHighwayOrdering.Concat(new[] { DrumsHighwayItem.Kick }).ToArray();
                    else if (instrument == Instrument.ProDrums)
                        profile.ProDrumsHighwayOrdering = profile.ProDrumsHighwayOrdering.Concat(new[] { DrumsHighwayItem.Kick }).ToArray();
                    else profile.FiveLaneDrumsHighwayOrdering = profile.FiveLaneDrumsHighwayOrdering.Concat(new[] { DrumsHighwayItem.Kick }).ToArray();
                }
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(instrument, KickChord(1, 2, 3)), profile);
                var colors = BeamPalette();
                _classic.Session.GetType().GetProperty("ColorProfile").GetSetMethod(true).Invoke(_classic.Session, new object[] { colors });
                Pool.GetType().GetMethod("ReturnAllObjects").Invoke(Pool, null);
                var note = Notes[0];
                var element = Spawn(note);
                foreach (bool star in new[] { false, true, false })
                {
                    if (star) note.ActivateFlag(NoteFlags.StarPower); else note.ClearFlag(NoteFlags.StarPower);
                    element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                    AssertBeamColors(element, star ? colors.FourLaneDrums.KickStarpower : colors.FourLaneDrums.KickNote,
                        star ? colors.FourLaneDrums.DoubleKickStarpower : colors.FourLaneDrums.DoubleKickNote, lefty, dedicated);
                }
                foreach (var hand in note.ChildNotes) Spawn(hand);
                Capture(element, $"Classic-kick-palette-{instrument}-dedicated{dedicated}-lefty{lefty}");
                var stats = (YARG.Core.Engine.Drums.DrumsStats)Stats(Engine(_classic.Player));
                stats.StarPowerTickAmount = uint.MaxValue;
                note.ActivateFlag(DrumNoteFlags.StarPowerActivator);
                var handler = _classic.Manager.GetType().GetProperty("BeatEventHandler").GetValue(_classic.Manager);
                var visual = handler.GetType().GetProperty("Visual").GetValue(handler);
                visual.GetType().GetMethod("Update").Invoke(visual, new object[] { .375d, _classic.Chart.SyncTrack });
                var strong = visual.GetType().GetProperty("StrongBeat").GetValue(visual);
                float pulse = (float)(double)strong.GetType().GetProperty("CurrentPercentage").GetValue(strong);
                Assert.That(pulse, Is.InRange(.01f, .99f), "Use a nontrivial activation pulse.");
                System.Drawing.Color Pulsed(System.Drawing.Color color)
                {
                    float factor = 1f - (1f - pulse) * (1f - pulse) * (1f - pulse);
                    return System.Drawing.Color.FromArgb(color.A, (int)(color.R * factor), (int)(color.G * factor), (int)(color.B * factor));
                }
                element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                AssertBeamColors(element, Pulsed(colors.FourLaneDrums.KickActivationNote),
                    Pulsed(colors.FourLaneDrums.DoubleKickActivationNote), lefty, dedicated);
                note.SetMissState(true, false);
                element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                AssertBeamColors(element, colors.FourLaneDrums.Miss, colors.FourLaneDrums.Miss, lefty, dedicated);
                yield return _classic.Dispose();
                _classic = null;
            }
        }

        [UnityTest]
        public IEnumerator KickFlamPreviewMaterialsUseBothExistingClassicKickPalettes()
        {
            foreach (var instrument in new[] { Instrument.ProDrums, Instrument.FiveLaneDrums })
            foreach (bool dedicated in new[] { false, true })
            foreach (bool lefty in new[] { false, true })
            {
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(instrument, KickChord(1)), ClassicProfile(instrument, false));
                var colors = BeamPalette();
                var preview = CreatePreview(instrument == Instrument.ProDrums ? GameMode.FourLaneDrums : GameMode.FiveLaneDrums, colors);
                preview.Player.GetType().GetProperty("LeftyFlip").SetValue(preview.Player, lefty);
                var type = Production("YARG.Settings.Preview.FakeNoteData");
                var data = Activator.CreateInstance(type);
                type.GetField("Time").SetValue(data, 1d);
                type.GetField("CenterNote").SetValue(data, true);
                type.GetField("NoteType").SetValue(data, Enum.Parse(Production("YARG.Themes.ThemeNoteType"), dedicated ? "DedicatedLaneKickFlam" : "KickFlam"));
                preview.Player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(preview.Player, new[] { data });
                var note = (Component)preview.Pool.GetType().GetMethod("GetByKey").Invoke(preview.Pool, new[] { data });
                foreach (bool star in new[] { false, true, false })
                {
                    preview.Player.GetType().GetProperty("ForceStarPowerNotes").SetValue(preview.Player, star);
                    note.GetType().GetMethod("OnSettingChanged").Invoke(note, null);
                    AssertBeamColors(note, star ? colors.FourLaneDrums.KickStarpower : colors.FourLaneDrums.KickNote,
                        star ? colors.FourLaneDrums.DoubleKickStarpower : colors.FourLaneDrums.DoubleKickNote, lefty, dedicated);
                }
                type.GetField("ForceMiss").SetValue(data, true);
                note.GetType().GetMethod("OnSettingChanged").Invoke(note, null);
                AssertBeamColors(note, colors.FourLaneDrums.Miss, colors.FourLaneDrums.Miss, lefty, dedicated);
                foreach (var value in _previewObjects) if (value != null) UnityEngine.Object.DestroyImmediate(value);
                _previewObjects.Clear();
                yield return _classic.Dispose();
                _classic = null;
            }
        }

        private (Component Player, Component Pool) CreatePreview(GameMode mode, ColorProfile colors = null)
        {
            var root = new GameObject("Classic kick-flam preview");
            _previewObjects.Add(root);
            root.SetActive(false);
            var player = root.AddComponent(Production("YARG.Settings.Preview.FakeTrackPlayer"));
            player.GetType().GetProperty("SelectedGameMode").SetValue(player, mode);
            var poolObject = new GameObject("Classic preview pool");
            poolObject.transform.SetParent(root.transform, false);
            poolObject.SetActive(false);
            var pool = poolObject.AddComponent(Production("YARG.Gameplay.KeyedPool"));
            pool.GetType().BaseType.GetField("_prewarmAmount", INSTANCE).SetValue(pool, 0);
            pool.GetType().BaseType.GetField("_objectCap", INSTANCE).SetValue(pool, 16);
            poolObject.SetActive(true);
            var presetType = Production("YARG.Themes.ThemePreset");
            var styleType = Production("YARG.Themes.VisualStyle");
            var factoryType = typeof(Func<,,>).MakeGenericType(presetType, styleType, typeof(GameObject));
            var factory = Delegate.CreateDelegate(factoryType, Production("YARG.Settings.Preview.FakeNote")
                .GetMethod("CreateFakeNoteFromTheme", BindingFlags.Public | BindingFlags.Static));
            var infoType = player.GetType().GetNestedType("Info");
            var initializerType = typeof(Action<,,>).MakeGenericType(infoType, presetType, styleType);
            var initializer = System.Linq.Expressions.Expression.Lambda(initializerType,
                System.Linq.Expressions.Expression.Empty(), initializerType.GetMethod("Invoke").GetParameters()
                    .Select(parameter => System.Linq.Expressions.Expression.Parameter(parameter.ParameterType))).Compile();
            player.GetType().GetMethod("Initialize").Invoke(player, new object[]
            {
                presetType.GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null), pool, factory,
                CameraPreset.Default, colors ?? new ColorProfile("Classic kick-flam preview"), EnginePreset.Default,
                HighwayPreset.Default, false, initializer
            });
            root.SetActive(true);
            return (player, pool);
        }

        private static Bounds BoundsIn(Transform space, MeshFilter mesh)
        {
            var matrix = space.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
            var bounds = mesh.sharedMesh.bounds;
            var result = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
            return result;
        }

        private static void AssertStackedBeams(Component element)
        {
            var beams = VisibleGroup(element).GetComponentsInChildren<MeshFilter>()
                .Where(mesh => mesh.name.Contains("beam")).Select(mesh => BoundsIn(element.transform.parent, mesh))
                .OrderBy(bounds => bounds.center.y).ToArray();
            Assert.That(beams, Has.Length.EqualTo(2));
            Assert.That(beams[1].min.y - beams[0].max.y, Is.GreaterThan(.0001f),
                "Dedicated kick flams require two physically separate vertical beams, not overlapping copies.");
            Assert.That(beams[1].center.x, Is.EqualTo(beams[0].center.x).Within(.0001f));
            Assert.That(beams[1].size.x, Is.EqualTo(beams[0].size.x).Within(.0001f));
            Assert.That(beams[1].size.x, Is.GreaterThan(.1f), "Both beams span the dedicated kick column.");
            Debug.Log($"CLASSIC_KICK_STACK_GEOMETRY lower={beams[0]} upper={beams[1]} gap={beams[1].min.y - beams[0].max.y}");
        }

        private static void Capture(Component element, string stem)
        {
            string directory = Environment.GetEnvironmentVariable("YARG_CLASSIC_DRUM_CAPTURE_DIRECTORY");
            if (string.IsNullOrEmpty(directory)) return;
            System.IO.Directory.CreateDirectory(directory);
            var highway = element.GetComponentInParent(Production("YARG.Gameplay.Player.DrumsPlayer")).transform;
            // A dedicated kick is off-center. Frame the highway axis at the chord's
            // depth, rather than aiming at that kick and clipping the other lanes.
            var localCenter = highway.InverseTransformPoint(element.transform.position);
            localCenter.x = 0f;
            localCenter.y = .02f;
            var center = highway.TransformPoint(localCenter);
            foreach (var view in new[] { (Angle: CameraPreset.Default.Rotation, Suffix: ""), (Angle: 60f, Suffix: "-inspection") })
            {
                var cameraObject = new GameObject("Classic kick-flam chord capture camera");
                var camera = cameraObject.AddComponent<Camera>();
                var target = new RenderTexture(640, 480, 24);
                var pixels = new Texture2D(640, 480, TextureFormat.RGB24, false);
                var previous = RenderTexture.active;
                try
                {
                    // Match the actual default gameplay camera orientation. The
                    // second, steeper view is explicitly an inspection capture.
                    var rotation = highway.rotation * Quaternion.Euler(view.Angle, 0f, 0f);
                    camera.transform.SetPositionAndRotation(center - rotation * Vector3.forward * 3f, rotation);
                    camera.orthographic = true;
                    camera.orthographicSize = .9f;
                    camera.aspect = 4f / 3f;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.targetTexture = target;
                    foreach (var renderer in element.transform.parent.GetComponentsInChildren<Renderer>())
                    {
                        var bounds = renderer.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            var point = camera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents,
                                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
                            Assert.That(point.x, Is.InRange(.01f, .99f), "Full chord must remain inside the capture width.");
                            Assert.That(point.y, Is.InRange(.01f, .99f), "Full chord must remain inside the capture height.");
                        }
                    }
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
                    pixels.Apply();
                    Assert.That(pixels.GetPixels32().Count(pixel => pixel.r > 8 || pixel.g > 8 || pixel.b > 8),
                        Is.GreaterThan(32), "The capture must contain visible native chord geometry.");
                    string path = System.IO.Path.Combine(directory, stem + view.Suffix + ".png");
                    System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                    Debug.Log($"CLASSIC_KICK_CHORD_CAPTURE {path} angle={view.Angle} center={localCenter}");
                }
                finally
                {
                    RenderTexture.active = previous;
                    camera.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(pixels);
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }
            }
        }
    }
}
