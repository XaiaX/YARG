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
using YARG.Core.Input;

namespace YARG.Tests.PlayMode
{
    // pattern: Imperative Shell
    public sealed class ClassicDrumFlamPlayModeTests : EliteFlamRuntimeTestBase
    {
        private InitializedClassicPlayerFixture _classic;

        [UnityTearDown]
        public IEnumerator DisposeClassic()
        {
            if (_classic != null) yield return _classic.Dispose();
            _classic = null;
        }

        private static YargProfile ClassicProfile(Instrument instrument, bool lefty, bool cymbalModels, bool dedicated)
        {
            var profile = new YargProfile
            {
                GameMode = instrument == Instrument.FiveLaneDrums ? GameMode.FiveLaneDrums : GameMode.FourLaneDrums,
                CurrentInstrument = instrument, PreferredInstrument = instrument, CurrentDifficulty = Difficulty.Hard,
                LeftyFlip = lefty, UseCymbalModels = cymbalModels
            };
            if (dedicated)
            {
                var ordering = (instrument == Instrument.FiveLaneDrums ? profile.FiveLaneDrumsHighwayOrdering
                    : instrument == Instrument.ProDrums ? profile.ProDrumsHighwayOrdering
                    : profile.FourLaneDrumsHighwayOrdering).Concat(new[] { DrumsHighwayItem.Kick }).ToArray();
                if (instrument == Instrument.FiveLaneDrums) profile.FiveLaneDrumsHighwayOrdering = ordering;
                else if (instrument == Instrument.ProDrums) profile.ProDrumsHighwayOrdering = ordering;
                else profile.FourLaneDrumsHighwayOrdering = ordering;
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

        private object Pool => _classic.Player.GetType().GetField("NotePool", INSTANCE).GetValue(_classic.Player);
        private IList<DrumNote> Notes => (IList<DrumNote>)_classic.Player.GetType().GetProperty("Notes", INSTANCE).GetValue(_classic.Player);

        private Component Spawn(DrumNote note)
        {
            Pool.GetType().GetMethod("ReturnAllObjects").Invoke(Pool, null);
            _classic.Player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(_classic.Player, new object[] { note });
            return (Component)Pool.GetType().GetMethod("GetByKey").Invoke(Pool, new object[] { note });
        }

        private static Component[] VisibleGroups(Component element) => element
            .GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
            .Where(group => group.gameObject.activeInHierarchy).ToArray();

        [UnityTest]
        public IEnumerator NativeProTomsAndCymbalsRemainDistinctThroughBotPoolReuse()
        {
            var pads = new[] { FourLaneDrumPad.YellowDrum, FourLaneDrumPad.YellowCymbal,
                FourLaneDrumPad.BlueDrum, FourLaneDrumPad.BlueCymbal,
                FourLaneDrumPad.GreenDrum, FourLaneDrumPad.GreenCymbal, FourLaneDrumPad.YellowDrum };
            var notes = pads.Select((pad, index) => new DrumNote(pad, DrumNoteType.Neutral,
                DrumNoteFlags.None, NoteFlags.None, index + 1d, (uint)((index + 1) * 960))).ToArray();
            var profile = ClassicProfile(Instrument.ProDrums, false, true, false);
            profile.IsBot = true;
            _classic = new InitializedClassicPlayerFixture();
            yield return _classic.Initialize(Chart(Instrument.ProDrums, notes), profile);
            Component previous = null;
            foreach (var note in Notes)
            {
                var element = Spawn(note);
                if (previous != null) Assert.That(element, Is.SameAs(previous), "Exercise the same pooled element.");
                previous = element;
                foreach (bool star in new[] { false, true, false })
                {
                    if (star) note.ActivateFlag(NoteFlags.StarPower); else note.ClearFlag(NoteFlags.StarPower);
                    element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                    var groups = VisibleGroups(element);
                    Assert.That(groups, Has.Length.EqualTo(1));
                    Assert.That(ModelType(groups[0]), Is.EqualTo(note.Pad >= (int)FourLaneDrumPad.YellowCymbal
                        ? "Cymbal" : "Normal"), ((FourLaneDrumPad)note.Pad).ToString());
                }
            }
        }

        private static string ModelType(Component group)
        {
            var theme = group.GetComponentInChildren(Production("YARG.Themes.ThemeNote"));
            return theme.GetType().GetProperty("NoteType").GetValue(theme).ToString();
        }

        [UnityTest]
        public IEnumerator ClassicKickDynamicsAreNeutralAcrossDedicatedLanesAndPoolReuse()
        {
            foreach (var instrument in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums })
            foreach (bool dedicated in new[] { false, true })
            {
                var profile = ClassicProfile(instrument, false, true, dedicated);
                var notes = new[] { DrumNoteType.Ghost, DrumNoteType.Neutral, DrumNoteType.Accent }
                    .Select((strength, index) => new DrumNote(0, strength, DrumNoteFlags.None, NoteFlags.None,
                        index + 1, (uint)((index + 1) * 960))).ToArray();
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(instrument, notes), profile);
                Assert.That(notes.Select(note => note.Type), Is.EqualTo(new[]
                {
                    DrumNoteType.Ghost, DrumNoteType.Neutral, DrumNoteType.Accent
                }), "Player preparation must preserve the authored source for historical replay reconstruction.");
                Component previous = null;
                var widths = new List<float>();
                foreach (var note in Notes)
                {
                    Assert.That(note.Type, Is.EqualTo(DrumNoteType.Neutral),
                        "Classic output must be fully playable with a switch-only kick, including converted charts.");
                    var element = Spawn(note);
                    Assert.That(element, Is.Not.Null);
                    if (previous != null) Assert.That(element, Is.SameAs(previous));
                    previous = element;
                    foreach (bool star in new[] { false, true, false })
                    {
                        if (star) note.ActivateFlag(NoteFlags.StarPower); else note.ClearFlag(NoteFlags.StarPower);
                        element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                        var groups = VisibleGroups(element);
                        Assert.That(groups, Has.Length.EqualTo(1));
                        string expected = dedicated ? "DedicatedLaneKick" : "Kick";
                        Assert.That(ModelType(groups[0]), Is.EqualTo(expected), instrument.ToString());
                        Assert.That(groups[0].GetComponentsInChildren<Renderer>().Length, Is.GreaterThan(0));
                        widths.Add(Width(element));
                        if (!star) CaptureIfRequested(element, $"{instrument}-kick-{(dedicated ? "dedicated" : "wide")}-{note.Type}");
                    }
                }
                foreach (var width in widths)
                    Assert.That(width, Is.EqualTo(widths[0]).Within(.002f),
                        "All classic kicks retain the neutral housing's authored width.");
                yield return _classic.Dispose();
                _classic = null;
            }
        }

        [UnityTest]
        public IEnumerator HandFlamPairsKeepDynamicsStarPowerAndAuthoredGeometryAcrossPoolReuse()
        {
            foreach (var instrument in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums })
            foreach (bool cymbalModels in new[] { false, true })
            foreach (bool lefty in new[] { false, true })
            foreach (int pad in instrument == Instrument.ProDrums ? new[] { 2, 5 } : new[] { 2 })
            {
                var notes = new[] { DrumNoteType.Ghost, DrumNoteType.Accent, DrumNoteType.Neutral }
                    .Select((strength, index) => new DrumNote(pad, strength, DrumNoteFlags.None, NoteFlags.None,
                        index + 1, (uint)((index + 1) * 960))).ToArray();
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(instrument, notes), ClassicProfile(instrument, lefty, cymbalModels, false));
                Component previous = null;
                foreach (var note in Notes)
                {
                    float ordinaryWidth = Width(Spawn(note));
                    foreach (bool flat in new[] { false, true })
                    {
                        var flag = typeof(DrumNote).GetProperty(flat ? "IsFlatFlam" : "IsFlam");
                        Assert.That(flag, Is.Not.Null, "Classic hand flam metadata must survive preparation.");
                        typeof(DrumNote).GetProperty("IsFlam").SetValue(note, !flat);
                        typeof(DrumNote).GetProperty("IsFlatFlam").SetValue(note, flat);
                        var element = Spawn(note);
                        if (previous != null) Assert.That(element, Is.SameAs(previous));
                        previous = element;
                        Vector3[] firstScales = null;
                        foreach (bool star in new[] { false, true, false })
                        {
                            if (star) note.ActivateFlag(NoteFlags.StarPower); else note.ClearFlag(NoteFlags.StarPower);
                            element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                            var groups = VisibleGroups(element);
                            Assert.That(groups, Has.Length.EqualTo(2), "One chart flam renders two real gems.");
                            AssertMatchingColors(groups[0], groups[1]);
                            if (flat)
                            {
                                int lanes = (int)_classic.Player.GetType().GetProperty("LaneCount").GetValue(_classic.Player);
                                Assert.That(Width(element), Is.EqualTo(ordinaryWidth * 2f * (lanes - 1f) / lanes).Within(.002f),
                                    "Pair width scales the actual classic model, without assuming a five-lane authored width.");
                            }
                            string expected = note.IsGhost ? "Ghost" : note.IsAccent ? "Accent" : "Normal";
                            if (instrument == Instrument.FiveLaneDrums && cymbalModels || instrument == Instrument.ProDrums && pad == 5)
                                expected = note.IsGhost ? "CymbalGhost" : note.IsAccent ? "CymbalAccent" : "Cymbal";
                            foreach (var group in groups)
                            {
                                Assert.That(ModelType(group), Is.EqualTo(expected));
                                Assert.That(group.GetComponentsInChildren<Renderer>().Length, Is.GreaterThan(0));
                                if (flat) Assert.That(Quaternion.Angle(group.transform.localRotation, Quaternion.identity),
                                    Is.LessThan(.001f), "Flat flams have no V rotation.");
                            }
                            var scales = groups.Select(group => group.transform.localScale).ToArray();
                            if (firstScales == null) firstScales = scales;
                            else if (!star) Assert.That(scales, Is.EqualTo(firstScales), "SP must not accumulate pair scaling.");
                            if (!lefty && !star && (cymbalModels || instrument == Instrument.FiveLaneDrums) ||
                                lefty && star && note.IsAccent && !flat && cymbalModels)
                                CaptureIfRequested(element, $"{instrument}-pad{pad}-cymbalModels{cymbalModels}-{note.Type}-{(flat ? "flat" : "regular")}-SP{star}-lefty{lefty}");
                        }
                        note.SetMissState(true, false);
                        element.GetType().GetMethod("OnStarPowerUpdated").Invoke(element, null);
                        var missedGroups = VisibleGroups(element);
                        Assert.That(missedGroups, Has.Length.EqualTo(2));
                        AssertMatchingColors(missedGroups[0], missedGroups[1]);
                        note.SetMissState(false, false);
                    }
                    typeof(DrumNote).GetProperty("IsFlam").SetValue(note, false);
                    typeof(DrumNote).GetProperty("IsFlatFlam").SetValue(note, false);
                    Assert.That(VisibleGroups(Spawn(note)), Has.Length.EqualTo(1), "Ordinary reuse hides its old companion.");
                }
                yield return _classic.Dispose();
                _classic = null;
            }
        }

        private static float Width(Component element)
        {
            var renderers = element.GetComponentsInChildren<Renderer>();
            return renderers.Max(renderer => renderer.bounds.max.x) - renderers.Min(renderer => renderer.bounds.min.x);
        }

        private static void AssertMatchingColors(Component left, Component right)
        {
            Color[] Colors(Component group, string role)
            {
                var theme = group.GetComponentInChildren(Production("YARG.Themes.ThemeNote"));
                return ((IEnumerable)theme.GetType().GetProperty(role).GetValue(theme)).Cast<object>().Select(entry =>
                {
                    var renderer = (Renderer)entry.GetType().GetField("Mesh").GetValue(entry);
                    int index = (int)entry.GetType().GetField("MaterialIndex").GetValue(entry);
                    return renderer.sharedMaterials[index].color;
                }).ToArray();
            }
            foreach (string role in new[] { "ColoredMaterials", "ColoredMetalMaterials" })
                Assert.That(Colors(right, role), Is.EqualTo(Colors(left, role)), "Both flam gems receive the same palette and emission updates.");
        }

        [UnityTest]
        public IEnumerator FollowUpRestartsReceptorWithoutRepeatingSourceOrDynamicsLifecycle()
        {
            foreach (var strength in new[] { DrumNoteType.Ghost, DrumNoteType.Accent })
            {
                var source = new DrumNote(FourLaneDrumPad.RedDrum, strength, DrumNoteFlags.None, NoteFlags.None, 1d, 960);
                typeof(DrumNote).GetProperty("IsFlam").SetValue(source, true);
                var future = new DrumNote(FourLaneDrumPad.YellowDrum, DrumNoteType.Neutral,
                    DrumNoteFlags.None, NoteFlags.None, 4d, 3840);
                _classic = new InitializedClassicPlayerFixture();
                yield return _classic.Initialize(Chart(Instrument.ProDrums, source, future),
                    ClassicProfile(Instrument.ProDrums, false, true, false));
                var player = _classic.Player;
                var engine = Engine(player);
                var sourceNote = Notes[0];
                Spawn(sourceNote);
                int primaryHits = 0;
                Action<int, DrumNote> observeHit = (_, __) => primaryHits++;
                var primaryField = engine.GetType().GetField("OnNoteHit");
                primaryField.SetValue(engine, Delegate.Combine((Delegate)primaryField.GetValue(engine),
                    Delegate.CreateDelegate(primaryField.FieldType, observeHit.Target, observeHit.Method)));
                var receptor = ClassicReceptor(player, (int)FourLaneDrumPad.RedDrum);
                var otherReceptor = ClassicReceptor(player, (int)FourLaneDrumPad.YellowDrum);
                StopEffect(receptor);
                StopEffect(otherReceptor);
                PressClassic(player, 1d, strength == DrumNoteType.Ghost ? .1f : .95f);
                Assert.That(sourceNote.WasHit, Is.True);
                Assert.That(Effect(receptor).GetComponentsInChildren<ParticleSystem>(true).Any(particle => particle.isPlaying), Is.True,
                    "The primary strike must animate the classic red receptor before follow-up feedback is isolated.");
                Assert.That(Pool.GetType().GetMethod("GetByKey").Invoke(Pool, new object[] { sourceNote }), Is.Null);
                Assert.That(primaryHits, Is.EqualTo(1));
                var combo = Stat(engine, "Combo");
                var ghosts = Stat(engine, "GhostsHit");
                var accents = Stat(engine, "AccentsHit");
                var nextElement = Spawn(Notes[1]);
                StopEffect(receptor);
                StopEffect(otherReceptor);
                var timers = (IDictionary)player.GetType().GetField("_animTypeToPadToLastPressedDelta", INSTANCE).GetValue(player);
                foreach (var value in timers.Values)
                {
                    var padTimers = (IDictionary)value;
                    foreach (var pad in padTimers.Keys.Cast<object>().ToArray()) padTimers[pad] = 1f;
                }
                PressClassic(player, 1.02d, .7f);
                Assert.That(Effect(receptor).GetComponentsInChildren<ParticleSystem>(true).Any(particle => particle.isPlaying), Is.True,
                    "A follow-up is another physical strike and must restart its receptor particles.");
                Assert.That(Effect(otherReceptor).GetComponentsInChildren<ParticleSystem>(true).Any(particle => particle.isPlaying), Is.False);
                foreach (DictionaryEntry entry in timers)
                {
                    float expected = entry.Key.ToString() == "CorrectNormal" ? 0f : 1f;
                    Assert.That(((IDictionary)entry.Value)[(int)FourLaneDrumPad.RedDrum], Is.EqualTo(expected),
                        "Follow-up feedback is normal; it does not repeat primary ghost/accent adjudication.");
                }
                Assert.That(primaryHits, Is.EqualTo(1));
                Assert.That(Stat(engine, "NotesHit"), Is.EqualTo(1));
                Assert.That(Stat(engine, "Combo"), Is.EqualTo(combo));
                Assert.That(Stat(engine, "GhostsHit"), Is.EqualTo(ghosts));
                Assert.That(Stat(engine, "AccentsHit"), Is.EqualTo(accents));
                Assert.That(Pool.GetType().GetMethod("GetByKey").Invoke(Pool, new object[] { Notes[1] }), Is.SameAs(nextElement),
                    "The follow-up must not return or re-hit a reused pooled note element.");
                yield return _classic.Dispose();
                _classic = null;
                var handlers = (Delegate)engine.GetType().GetField("OnFlamFollowUp").GetValue(engine);
                Assert.That(handlers == null || !handlers.GetInvocationList().Any(handler => ReferenceEquals(handler.Target, player)), Is.True);
            }
        }

        private static void CaptureIfRequested(Component element, string stem)
        {
            string directory = Environment.GetEnvironmentVariable("YARG_CLASSIC_DRUM_CAPTURE_DIRECTORY");
            if (string.IsNullOrEmpty(directory)) return;
            System.IO.Directory.CreateDirectory(directory);
            var cameraObject = new GameObject("Fixed classic drum capture camera");
            var camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(320, 240, 24);
            var pixels = new Texture2D(320, 240, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var highway = element.GetComponentInParent(Production("YARG.Gameplay.Player.DrumsPlayer")).transform;
                var center = element.transform.position + highway.up * .02f;
                camera.transform.position = center + highway.TransformDirection(new Vector3(0f, 1.5f, -1.8f));
                camera.transform.LookAt(center, highway.up);
                camera.orthographic = true;
                camera.orthographicSize = .9f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 320, 240), 0, 0);
                pixels.Apply();
                Assert.That(pixels.GetPixels32().Count(pixel => pixel.r > 8 || pixel.g > 8 || pixel.b > 8),
                    Is.GreaterThan(8), "The native capture must contain visible gameplay geometry.");
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, stem + ".png"), pixels.EncodeToPNG());
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

        private static Component ClassicReceptor(Component player, int pad)
        {
            var array = player.GetComponentInChildren(Production("YARG.Gameplay.Visuals.FretArray"), true);
            var frets = (IDictionary)array.GetType().GetField("_frets", INSTANCE).GetValue(array);
            return (Component)frets[pad];
        }

        private static void PressClassic(Component player, double time, float velocity)
        {
            var input = GameInput.Create(time, DrumsAction.RedDrum, velocity);
            var type = Production("YARG.Gameplay.Player.BasePlayer");
            type.GetMethod("OnGameInput", INSTANCE).Invoke(player, new object[] { input });
            type.GetMethod("UpdateInputs", INSTANCE).Invoke(player, new object[] { time });
        }
    }
}
