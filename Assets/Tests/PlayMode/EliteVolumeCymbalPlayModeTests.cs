using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteVolumeCymbalPlayModeTests : EliteFlamRuntimeTestBase
    {
        private const string THEME = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";
        private const string HATS = "Assets/Prefabs/Gameplay/Visual/EliteDrums/HiHats/";

        [Test]
        public void HatArticulationsShareBaseAndClosedFacesMatchConeArea()
        {
            var reference = AssetDatabase.LoadAssetAtPath<GameObject>(HATS + "VolumeCymbal.prefab");
            var shell = Part(reference, "Base");
            var baseMetal = shell.GetComponent<MeshRenderer>().sharedMaterial;
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(HATS + "Cymbal.prefab");
            var originalMetal = Part(original, "Base").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(baseMetal.GetTexture("_BaseMap"), Is.Not.SameAs(originalMetal.GetTexture("_BaseMap")));
            Assert.That(baseMetal.shader, Is.SameAs(originalMetal.shader));
            foreach (string suffix in new[] { "Ghost", "", "Accent" })
            {
                var cymbal = AssetDatabase.LoadAssetAtPath<GameObject>(HATS + "VolumeCymbal" + suffix + ".prefab");
                var bow = Part(cymbal, "Top");
                var familyMetal = Part(cymbal, "Base").GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(familyMetal.name, Is.EqualTo(suffix == "Ghost" ? "EliteVolumeCymbalGhostBase" : baseMetal.name));
                foreach (string family in new[] { "ClosedHiHat", "OpenHiHat" })
                {
                    var hat = AssetDatabase.LoadAssetAtPath<GameObject>(HATS + "Volume" + family + suffix + ".prefab");
                    Assert.That(hat, Is.Not.Null, family + suffix);
                    var basis = Part(hat, "Base");
                    Assert.That(basis.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(familyMetal), hat.name);
                    Assert.That(basis.sharedMesh, Is.SameAs(shell.sharedMesh), hat.name);
                    var hatBase = BoundsInNote(hat, basis);
                    var referenceBase = BoundsInNote(reference, shell);
                    Assert.That(Vector3.Distance(hatBase.center, referenceBase.center), Is.LessThan(.0001f), hat.name);
                    Assert.That(Vector3.Distance(hatBase.size, referenceBase.size), Is.LessThan(.0001f), hat.name);
                    var plate = Part(hat, "Top");
                    var faceWidth = BoundsInNote(hat, plate, 1).size.x;
                    var coneWidth = BoundsInNote(cymbal, bow, 1).size.x;
                    float faceScale = family == "ClosedHiHat" ? 1.1262f : 1f;
                    Assert.That(faceWidth, Is.EqualTo(coneWidth * faceScale).Within(.0001f), hat.name);
                    Assert.That(BoundsInNote(hat, plate).size.x, Is.LessThanOrEqualTo(hatBase.size.x + .0001f), hat.name);
                    var visibleMaterials = hat.GetComponentsInChildren<MeshRenderer>()
                        .Where(renderer => renderer.enabled)
                        .SelectMany(renderer => renderer.sharedMaterials)
                        .Select(material => material.name).ToArray();
                    Assert.That(visibleMaterials.Contains("EliteOpenHatGlow"), Is.EqualTo(family == "OpenHiHat"),
                        hat.name + " base glow");
                    if (family == "OpenHiHat")
                    {
                        var glow = Part(hat, "Base Glow");
                        var pulse = Part(hat, "Well pulse");
                        CollectionAssert.AreEqual(glow.sharedMesh.vertices, pulse.sharedMesh.vertices, hat.name);
                        CollectionAssert.AreEqual(glow.sharedMesh.triangles, pulse.sharedMesh.triangles, hat.name);
                        var colors = pulse.sharedMesh.colors32;
                        Assert.That(colors.Length, Is.EqualTo(pulse.sharedMesh.vertexCount));
                        int role = suffix == "Ghost" ? 0 : suffix == "Accent" ? 2 : 1;
                        Assert.That(colors.Min(c => c[role]), Is.Zero, hat.name + " full shadow");
                        Assert.That(colors.Max(c => c[role]), Is.EqualTo(255), hat.name + " lit edge");
                        Assert.That(pulse.transform.localPosition, Is.EqualTo(glow.transform.localPosition), hat.name);
                        var pulseRenderer = pulse.GetComponent<MeshRenderer>();
                        Assert.That(pulseRenderer.sortingOrder, Is.GreaterThan(glow.GetComponent<MeshRenderer>().sortingOrder));
                        float peak = suffix == "Ghost" ? .26f : suffix == "Accent" ? .72f : .47f;
                        Assert.That(pulseRenderer.sharedMaterial.GetFloat("_FloorGain") * peak,
                            Is.EqualTo(.12f).Within(.0001f), hat.name + " shared pulse floor");
                        Assert.That(plate.sharedMesh, Is.SameAs(bow.sharedMesh), hat.name);
                        Assert.That(BoundsInNote(hat, plate).min.y, Is.GreaterThan(BoundsInNote(cymbal, bow).min.y),
                            hat.name + " must expose the gap");
                    }
                    else
                    {
                        var mesh = plate.sharedMesh;
                        var vertices = mesh.vertices;
                        int faceIndex = mesh.GetTriangles(1)[0];
                        var normal = mesh.normals[faceIndex];
                        float Height(int i) => Vector3.Dot(vertices[i] - vertices[faceIndex], normal)
                            * plate.transform.localScale.y;
                        Assert.That(-mesh.GetTriangles(0).Min(Height), Is.EqualTo(.02120f).Within(.00002f),
                            hat.name + " seated body thickness, perpendicular to the face");
                        if (suffix == "Accent")
                        {
                            const float lift = .019674f;
                            var glow = mesh.GetTriangles(4);
                            Assert.That(glow.Min(Height), Is.EqualTo(lift).Within(.00002f), "Only the raised face glows");
                            Assert.That(glow.Max(Height), Is.EqualTo(lift).Within(.00002f), "Ring rise above the face");
                            var layout = mesh.uv2;
                            var outerWall = mesh.GetTriangles(3).Where(i =>
                                Vector2.Distance(layout[i], new Vector2(.5f, .5f)) > .98f * .44f).ToArray();
                            Assert.That(outerWall, Is.Not.Empty);
                            Assert.That(outerWall.Min(Height), Is.EqualTo(0f).Within(.00002f), "Dark outer wall foot");
                            Assert.That(outerWall.Max(Height), Is.EqualTo(lift).Within(.00002f), "Dark outer wall lip");
                        }
                    }
                }
            }
        }

        private static MeshFilter Part(GameObject note, string name) =>
            note.GetComponentsInChildren<MeshFilter>(true).Single(filter => filter.name == name);

        private static Bounds BoundsInNote(GameObject note, MeshFilter filter, int submesh = -1)
        {
            var matrix = note.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var vertices = filter.sharedMesh.vertices;
            var indices = submesh < 0 ? filter.sharedMesh.triangles : filter.sharedMesh.GetTriangles(submesh);
            var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[indices[0]]), Vector3.zero);
            foreach (int i in indices) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
            return bounds;
        }

        [UnityTest]
        public IEnumerator VisibleOpenHatChangesCadenceAtThePlaybackBoundaryAndOnSeek()
        {
            const int changeBeat = 60;
            double changeTime = changeBeat * 60d / 85;
            var notes = Enumerable.Range(0, 100).Select(beat => new EliteDrumNote(
                EliteDrumPad.HiHat, DrumNoteType.Neutral, EliteDrumsHatState.Open, EliteDrumsHatPedalType.Stomp,
                false, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None,
                beat < changeBeat ? beat * 60d / 85 : changeTime + (beat - changeBeat) * 60d / 190,
                (uint)(beat * 480), false)).ToArray();
            var song = PreparedElitePlayerFixture.CreateChart(notes);
            song.SyncTrack.Tempos[0] = new TempoChange(85, 0, 0);
            song.SyncTrack.Tempos.Add(new TempoChange(190, changeTime, changeBeat * 480));
            var profile = Profile();
            profile.UseVolumeCymbals = true;
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(song, profile, EliteDrumsLayoutPreset.Compact);

            var player = Fixture.Player;
            var pool = player.GetType().GetField("NotePool", INSTANCE).GetValue(player);
            var approaching = Playable.Notes[changeBeat + 1];
            player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(player, new object[] { approaching });
            var element = (Component)pool.GetType().GetMethod("GetByKey").Invoke(pool, new object[] { approaching });
            var well = element.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Well pulse");
            var properties = new MaterialPropertyBlock();
            var update = player.GetType().GetMethod("UpdateOpenHatCadence", INSTANCE);
            foreach (var (time, expected) in new[] { (changeTime - 0.01, 2f), (changeTime + 0.01, 1f), (1d, 2f) })
            {
                update.Invoke(player, new object[] { time });
                well.GetPropertyBlock(properties);
                Assert.That(properties.GetFloat("_PulsesPerBeat"), Is.EqualTo(expected));
            }
        }

        [UnityTest]
        public IEnumerator TwoEliteProfilesRenderTheirOwnCymbalsThroughPoolReuseAndStarPower()
        {
            var notes = new List<EliteDrumNote>();
            foreach (var strength in new[] { DrumNoteType.Neutral, DrumNoteType.Ghost, DrumNoteType.Accent })
            foreach (var (pad, state) in new[]
            {
                (EliteDrumPad.HiHat, EliteDrumsHatState.Indifferent),
                (EliteDrumPad.Ride, EliteDrumsHatState.Indifferent),
                (EliteDrumPad.LeftCrash, EliteDrumsHatState.Indifferent),
                (EliteDrumPad.RightCrash, EliteDrumsHatState.Indifferent),
                (EliteDrumPad.HiHat, EliteDrumsHatState.Open),
                (EliteDrumPad.HiHat, EliteDrumsHatState.Closed),
            })
            foreach (var (flam, flat) in new[] { (true, false), (false, true), (false, false) })
            {
                double time = (notes.Count + 1) * .25;
                notes.Add(new EliteDrumNote(pad, strength, state, EliteDrumsHatPedalType.Stomp, flam,
                    DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, (uint)(time * 720),
                    false, isFlatFlam: flat));
            }
            var current = Profile();
            var volume = Profile();
            current.UseVolumeCymbals = false;
            Fixture = new InitializedElitePlayerFixture();
            var song = PreparedElitePlayerFixture.CreateChart(notes.ToArray());
            song.SyncTrack.Tempos[0] = new TempoChange(90, 0, 0);
            yield return Fixture.Initialize(song, current,
                EliteDrumsLayoutPreset.Full, secondProfile: volume);

            var theme = AssetDatabase.LoadAssetAtPath<GameObject>(THEME).GetComponent(Production("YARG.Themes.ThemeComponent"));
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var getModels = theme.GetType().GetMethod("GetNoteModelsForVisualStyle");
            var regular = (IDictionary)getModels.Invoke(theme, new[] { style, (object)false });
            var stars = (IDictionary)getModels.Invoke(theme, new[] { style, (object)true });
            foreach (var player in new[] { Fixture.Player, Fixture.SecondPlayer })
            {
                var pool = player.GetType().GetField("NotePool", INSTANCE).GetValue(player);
                var session = player.GetType().GetProperty("Player").GetValue(player);
                var chart = (InstrumentDifficulty<EliteDrumNote>)session.GetType()
                    .GetProperty("PlayableEliteDrumDifficulty").GetValue(session);
                Assert.That(chart.Notes.Count, Is.EqualTo(notes.Count));
                Component last = null;
                foreach (var note in chart.Notes)
                {
                    volume.OpenHatPulseCadence = note.IsAccent ? DrumOpenHatPulseCadence.Quarter : DrumOpenHatPulseCadence.Auto;
                    volume.OpenHatGlow = note.IsGhost ? DrumOpenHatGlow.Static :
                        note.IsAccent ? DrumOpenHatGlow.CircularSweep : DrumOpenHatGlow.WholeSurface;
                    volume.LeftyFlip = note.IsHandFlam;
                    string family = note.IsOpen ? "OpenHiHat" : note.IsClosed ? "ClosedHiHat" : "Cymbal";
                    string name = family + (note.IsGhost ? "Ghost" : note.IsAccent ? "Accent" : "");
                    object type = Enum.Parse(Production("YARG.Themes.ThemeNoteType"), name);
                    GameObject alternate = null;
                    if (Fixture.SecondPlayer == player)
                    {
                        string variant = note.IsHandFlam ? name.Insert(family.Length, "Flam") : name;
                        alternate = AssetDatabase.LoadAssetAtPath<GameObject>(HATS + "Volume" + variant + ".prefab");
                        Assert.That(alternate, Is.Not.Null, $"Missing experimental {name} model.");
                    }
                    pool.GetType().GetMethod("ReturnAllObjects").Invoke(pool, null);
                    player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(player, new object[] { note });
                    var element = (Component)pool.GetType().GetMethod("GetByKey").Invoke(pool, new object[] { note });
                    if (last != null) Assert.That(element, Is.SameAs(last), "Exercise a reused note element.");
                    last = element;
                    foreach (bool star in new[] { false, true, false })
                    {
                        if (star) note.ActivateFlag(NoteFlags.StarPower);
                        else note.ClearFlag(NoteFlags.StarPower);
                        element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                        var expected = alternate != null ? alternate :
                            (GameObject)(star && stars.Contains(type) ? stars[type] : regular[type]);
                        var visible = element.GetComponentsInChildren(Production("YARG.Themes.ThemeNote"));
                        bool fusedCymbal = alternate != null && note.IsHandFlam;
                        Assert.That(visible, Has.Length.EqualTo(note.IsHandFlam && !fusedCymbal ? 2 : 1));
                        foreach (var model in visible)
                        {
                            Assert.That(model.name, Is.EqualTo(expected.name + "(Clone)"));
                            if (fusedCymbal)
                            {
                                var marker = (GameObject)model.GetType().GetField("_flatFlamMarker", INSTANCE)
                                    .GetValue(model);
                                Assert.That(marker.activeInHierarchy, Is.EqualTo(note.IsFlatFlam), model.name);
                            }
                            if (alternate && note.IsOpen)
                            {
                                var pulse = Part(model.gameObject, "Well pulse").GetComponent<MeshRenderer>();
                                Assert.That(pulse.enabled, Is.True, "Static also applies its adjustable brightness.");
                                var properties = new MaterialPropertyBlock();
                                pulse.GetPropertyBlock(properties);
                                Assert.That(properties.GetFloat("_PulsesPerBeat"), Is.EqualTo(note.IsGhost ? 0f : note.IsAccent ? 1f : 2f));
                                Assert.That(properties.GetFloat("_WholeSurface"),
                                    Is.EqualTo(volume.OpenHatGlow == DrumOpenHatGlow.WholeSurface ? 1f : 0f));
                                Assert.That(properties.GetFloat("_SweepDirection"),
                                    Is.EqualTo(note.IsAccent ? (volume.LeftyFlip ? -1f : 1f) : 0f));
                                Assert.That(properties.GetFloat("_PreviewBeatRate"), Is.Zero);
                                var top = Part(model.gameObject, "Top").GetComponent<MeshRenderer>().sharedMaterials[1];
                                float wellBrightness = note.IsGhost ? .26f : note.IsAccent ? .72f : .47f;
                                foreach (string layer in new[] { "Interior", "Base Glow", "Light" })
                                {
                                    var material = Part(model.gameObject, layer).GetComponent<MeshRenderer>().sharedMaterial;
                                    Assert.That(material.color.maxColorComponent,
                                        Is.EqualTo(top.color.maxColorComponent * wellBrightness).Within(.001f), name + " " + layer);
                                }
                            }
                            if (alternate && !fusedCymbal && note.IsGhost)
                            {
                                var top = Part(model.gameObject, "Top").GetComponent<MeshRenderer>().sharedMaterials;
                                Assert.That(top[0].name, Does.StartWith("CymbalGhostCenter"), name);
                                Assert.That(top[0].color.maxColorComponent, Is.LessThanOrEqualTo(.301f), name);
                                Assert.That(top[0].GetColor("_EmissionColor").maxColorComponent, Is.Zero, name);
                                Assert.That(top[2].name, Does.StartWith("CymbalMetal"), name + " chrome tip");
                            }
                            if (alternate && !fusedCymbal && note.IsAccent)
                            {
                                var peak = Part(model.gameObject, "Top").GetComponent<MeshRenderer>().sharedMaterials[2];
                                if (note.IsClosed)
                                {
                                    Assert.That(peak.name, Does.StartWith("EliteHiHatDark"), name);
                                    Assert.That(peak.GetColor("_EmissionColor").maxColorComponent, Is.Zero, name);
                                }
                                else
                                {
                                    var ring = Part(model.gameObject, "Accent ring").GetComponent<MeshRenderer>();
                                    Assert.That(peak.name, Does.StartWith("EliteAccentHOPOGlow"), name);
                                    Assert.That(peak.GetColor("_EmissionColor"),
                                        Is.EqualTo(ring.sharedMaterial.GetColor("_EmissionColor")), name);
                                }
                            }
                            CollectionAssert.AreEquivalent(expected.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh),
                                model.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh));
                        }
                    }
                }
            }
        }
    }
}
