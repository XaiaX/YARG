// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteHiHatSustainPlayModeTests : EliteFlamRuntimeTestBase
    {
        [UnitySetUp]
        public IEnumerator FreshTuningSettingsForThisSuite()
        {
            var manager = Production("YARG.Settings.SettingsManager");
            manager.GetProperty("Settings").GetSetMethod(true).Invoke(null,
                new[] { Activator.CreateInstance(manager.GetNestedType("SettingContainer")) });
            yield return null;
        }

        private static EliteDrumNote Open(double time, uint length) => new(
            EliteDrumPad.HiHat, DrumNoteType.Neutral, EliteDrumsHatState.Open,
            EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumsChannelFlag.None, time, (uint)(time * 960), false,
            new EliteDrumSourceDefinition($"open:{time}", (int)(time * 960), (int)EliteDrumPad.HiHat,
                (uint)(time * 960), length, time, length / 960d));

        private IEnumerator Initialize(bool lefty = false, EliteDrumNote[] notes = null, Action<SyncTrack> timing = null)
        {
            var profile = Profile();
            profile.LeftyFlip = lefty;
            Fixture = new InitializedElitePlayerFixture();
            var chart = PreparedElitePlayerFixture.CreateChart(notes ?? new[] {
                Open(1, 1920), Note(EliteDrumPad.Snare, 5) });
            timing?.Invoke(chart.SyncTrack);
            yield return Fixture.Initialize(chart, profile, YARG.Core.Game.EliteDrumsLayoutPreset.Full, noFail: true);
        }

        private Component Visual()
        {
            var root = Fixture.Player.transform.Find("Hi-hat sustains");
            Assert.That(root, Is.Not.Null, "Native Elite needs an independently owned sustain visual that survives gem pooling.");
            return root.GetComponent(Production("YARG.Gameplay.Visuals.HiHatSustainVisual"));
        }

        private void Render(Component visual, double time)
        {
            float speed = (float)Fixture.Player.GetType().GetProperty("NoteSpeed").GetValue(Fixture.Player);
            visual.GetType().GetMethod("Render").Invoke(visual, new object[] { time, speed, 15f });
        }

        private static EliteDrumNote[] ConnectedOpens() => new[] { Open(1, 0), Open(1.125, 0),
            Open(1.25, 0), Open(1.375, 0), Open(1.5, 0), Note(EliteDrumPad.Snare, 5) };

        private static Vector2[] UpcomingWidths(Component visual)
        {
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            var data = new List<Vector2>(); mesh.GetUVs(4, data);
            // Exclude the first interval's endpoint at the shared boundary. After
            // release it is no longer held, but retains its own historical width.
            return data.Where((value, i) => mesh.uv2[i].y == 0 && mesh.uv[i].y > 1.125f).ToArray();
        }

        private static readonly bool[] ConnectedPedalStarts = { false, true };

        private static readonly double[] FlareDurations = { .25, 1 };

        [UnityTest]
        public IEnumerator WaitingShapeUsesAuthoredLengthThroughClippingAndActivation([ValueSource(nameof(FlareDurations))] double duration)
        {
            var closed = Open(1 + duration, 0); closed.HatState = EliteDrumsHatState.Closed;
            yield return Initialize(notes: new[] { Open(1, (uint)(duration * 960)), closed, Note(EliteDrumPad.Snare, 5) });
            var engine = (EliteDrumsEngine)Engine(Fixture.Player);
            var sustain = engine.HiHatSustains.Single();
            Assert.That(sustain.EndTime - sustain.StartTime, Is.EqualTo(duration).Within(.0001));
            var visual = Visual();
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            float speed = (float)Fixture.Player.GetType().GetProperty("NoteSpeed").GetValue(Fixture.Player);
            void Check(double time, float farZ, float expectedLast)
            {
                visual.GetType().GetMethod("Render").Invoke(visual, new object[] { time, speed, farZ });
                var envelope = new List<Vector3>(); mesh.GetUVs(4, envelope);
                Assert.That(envelope.Count, Is.GreaterThan(0));
                for (int i = 0; i < envelope.Count; i++)
                    Assert.That(envelope[i].z, Is.EqualTo((mesh.uv[i].y - sustain.StartTime) / duration).Within(.0001),
                        "The taper must stay anchored to authored endpoints, rather than the visible or held front.");
                Assert.That(envelope.Max(value => value.z), Is.EqualTo(expectedLast).Within(.0001));
            }
            Check(.9, 100, 1);
            Capture(new[] { visual, Receptor(Fixture.Player, EliteDrumPad.HiHat) }, $"flare-waiting-{duration}", 28);
            float clippedFar = -2 + (float)(1 + duration * .4 - .9) * speed;
            Check(.9, clippedFar, .4f);
            Check(.8, -2 + (float)(1 + duration * .4 - .8) * speed, .4f);
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            engine.Update(1.02);
            Check(1.02, 100, 1);
            Capture(new[] { visual, Receptor(Fixture.Player, EliteDrumPad.HiHat) }, $"flare-held-{duration}", 28);
        }

        [UnityTest]
        public IEnumerator ConnectedRunInheritsWidthWithoutGlowOrActualActivation([ValueSource(nameof(ConnectedPedalStarts))] bool pedal)
        {
            var notes = ConnectedOpens();
            if (pedal) notes[0] = Splash(1);
            yield return Initialize(notes: notes);
            var visual = Visual(); Render(visual, .9);
            if (!pedal) Capture(new[] { visual, Receptor(Fixture.Player, EliteDrumPad.HiHat) }, "connected-waiting", 28);
            Press(Fixture.Player, pedal ? EliteDrumsAction.EliteSplash : EliteDrumsAction.EliteOpenHiHat, 1);
            ((EliteDrumsEngine)Engine(Fixture.Player)).Update(1.1);
            Render(visual, 1.1);
            var future = UpcomingWidths(visual);
            Assert.That(future.Length, Is.GreaterThan(0));
            Assert.That(future.All(value => Math.Abs(value.x - (pedal ? 1 : .896f)) < .001), Is.True,
                "All snapped successors must follow the same widening origin.");
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.uv2.Any(value => value.y == 1), Is.True);
            for (int i = 0; i < mesh.vertexCount; i++)
                Assert.That(mesh.colors[i].a, Is.EqualTo(mesh.uv2[i].y == 1 ? .33f : .28f).Within(.001),
                    "Inherited width must not activate the future glow.");
            if (!pedal) Capture(new[] { visual, Receptor(Fixture.Player, EliteDrumPad.HiHat) }, "connected-expanded-dim-future", 28);
        }

        [UnityTest]
        public IEnumerator EarlyRenewalDoesNotRestartConnectedWidening()
        {
            yield return Initialize(notes: ConnectedOpens());
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1.1);
            var visual = Visual(); Render(visual, 1.1);
            Assert.That(UpcomingWidths(visual).All(value => Math.Abs(value.x - .896f) < .001), Is.True);
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1.25);
            Render(visual, 1.25);
            Assert.That(UpcomingWidths(visual).All(value => value.x == 1), Is.True,
                "A connected renewal must keep the already expanded width.");
        }

        [UnityTest]
        public IEnumerator CorrectEarlyHatStartsFreshAfterItSkipsAMissedPad()
        {
            var notes = ConnectedOpens().Concat(new[] { Note(EliteDrumPad.Snare, 1.04) }).OrderBy(note => note.Time).ToArray();
            yield return Initialize(notes: notes);
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1.1);
            var engine = (EliteDrumsEngine)Engine(Fixture.Player);
            var visual = Visual(); Render(visual, 1.1);
            Assert.That(UpcomingWidths(visual).All(value => value.x == 0), Is.True, "The skipped miss resets the old chain.");
            engine.Update(1.125); Render(visual, 1.125);
            Assert.That(UpcomingWidths(visual).All(value => Math.Abs(value.x - .104f) < .001), Is.True,
                "The successful hat after that miss must seed a new chain rather than leave future tails unseeded.");
        }

        [UnityTest]
        public IEnumerator OverhitClearsFutureWidthWithoutResurrectingItFromTheHeldInterval()
        {
            yield return Initialize(notes: ConnectedOpens());
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            var engine = (EliteDrumsEngine)Engine(Fixture.Player); engine.Update(1.1);
            var visual = Visual(); Render(visual, 1.1);
            Assert.That(UpcomingWidths(visual).All(value => value.x > .8f), Is.True);
            engine.Overhit(); Render(visual, 1.1);
            Assert.That(engine.ActiveHiHatSustain, Is.Not.Null);
            Assert.That(UpcomingWidths(visual).All(value => value.x == 0), Is.True);
            Render(visual, 1.11);
            Assert.That(UpcomingWidths(visual).All(value => value.x == 0), Is.True,
                "The still-held interval must not rebuild inheritance after a fault.");
        }

        [UnityTest]
        public IEnumerator MissedGeneratorClearsTheRestOfTheConnectedRun()
        {
            yield return Initialize(notes: ConnectedOpens());
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            var engine = (EliteDrumsEngine)Engine(Fixture.Player); engine.Update(1.1);
            var visual = Visual(); Render(visual, 1.1);
            Assert.That(UpcomingWidths(visual).All(value => value.x > .8f), Is.True);
            engine.Update(1.23); Render(visual, 1.23);
            Assert.That(engine.HiHatSustains[1].Generators.Any(note => note.WasMissed), Is.True);
            Assert.That(UpcomingWidths(visual).All(value => value.x == 0), Is.True);
        }

        private static readonly double[] ReleaseTimes = { 1.04, 1.1 };

        [UnityTest]
        public IEnumerator EarlyAndForgivenPedalReleaseBothResetInheritedWidth([ValueSource(nameof(ReleaseTimes))] double release)
        {
            yield return Initialize(notes: ConnectedOpens());
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            var visual = Visual(); Render(visual, 1);
            ((EliteDrumsEngine)Engine(Fixture.Player)).Update(1.02); Render(visual, 1.02);
            Assert.That(UpcomingWidths(visual).All(value => value.x > 0), Is.True);
            var engine = (EliteDrumsEngine)Engine(Fixture.Player);
            bool released = false;
            engine.OnPadHit += (_, _, _, _, _, _) => released |= engine.IsHiHatSustainRelease;
            Press(Fixture.Player, EliteDrumsAction.EliteStomp, release);
            Assert.That(released, Is.True, "The physical pedal input must reach the sustain-release callback.");
            Assert.That(engine.ActiveHiHatSustain, Is.Null);
            Render(visual, release);
            Assert.That(UpcomingWidths(visual).All(value => value.x == 0), Is.True);
            Assert.That(((EliteDrumsEngine)Engine(Fixture.Player)).EngineStats.Overhits, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ADisconnectedRunDoesNotInheritThePreviousRunWidth()
        {
            yield return Initialize(notes: new[] { Open(1, 0), Open(2, 0), Open(2.125, 0), Note(EliteDrumPad.Snare, 5) });
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            ((EliteDrumsEngine)Engine(Fixture.Player)).Update(1.1);
            var visual = Visual(); Render(visual, 1.1);
            Assert.That(UpcomingWidths(visual).Length, Is.GreaterThan(0));
            Assert.That(UpcomingWidths(visual).All(value => value.x == 0), Is.True);
        }

        [UnityTest]
        public IEnumerator TuningChangesRefreshTheExistingMeshWithoutGameplayUpdates()
        {
            yield return Initialize();
            var visual = Visual(); Render(visual, .9);
            var settings = Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetValue(null);
            string[] names = { "EliteHiHatWaitingOpacity", "EliteHiHatHeldOpacity", "EliteHiHatWaitingGlow", "EliteHiHatHeldGlow" };
            float[] values = { 33, 66, 75, 175 };
            for (int i = 0; i < names.Length; i++)
            {
                var property = settings.GetType().GetProperty(names[i]); Assert.That(property, Is.Not.Null);
                var slider = property.GetValue(settings); slider.GetType().GetProperty("Value").SetValue(slider, values[i]);
            }
            yield return null; // The component updates even while the gameplay loop is stopped/paused.
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.colors[0].a, Is.EqualTo(.33f).Within(.001));
            var glow = visual.GetComponent<MeshRenderer>().sharedMaterial.GetVector("_GlowLevels");
            Assert.That(glow.x, Is.EqualTo(.75f).Within(.001)); Assert.That(glow.y, Is.EqualTo(1.75f).Within(.001));
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1); Render(visual, 1);
            Assert.That(mesh.colors[0].a, Is.EqualTo(.66f).Within(.001));
        }

        private static readonly (int column, bool lefty, bool narrow)[] WidthCases = {
            (0, false, false), (0, true, false), (1, false, false), (1, true, false),
            (4, false, false), (4, true, false), (0, false, true), (1, true, true) };

        [UnityTest]
        public IEnumerator TailMatchesPedalFootprintOrSingleFret(
            [ValueSource(nameof(WidthCases))] (int column, bool lefty, bool narrow) testCase)
        {
            var profile = Profile(); profile.LeftyFlip = testCase.lefty;
            var field = profile.GetType().GetField("OneFretHiHatSustains");
            Assert.That(field, Is.Not.Null, "Each profile needs its own width choice.");
            field.SetValue(profile, testCase.narrow);
            var layout = (YARG.Core.Game.EliteDrumsLayoutPreset)YARG.Core.Game.EliteDrumsLayoutPreset.Compact.CopyWithNewName("Sustain width");
            (layout.Slots[1], layout.Slots[testCase.column]) = (layout.Slots[testCase.column], layout.Slots[1]);
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(PreparedElitePlayerFixture.CreateChart(Open(1, 240)), profile, layout, noFail: true);
            var visual = Visual(); Render(visual, .9);
            var bounds = visual.GetComponent<MeshFilter>().sharedMesh.bounds;
            int column = testCase.lefty ? 4 - testCase.column : testCase.column;
            int first = testCase.narrow ? column : Math.Max(0, column - 1);
            int last = testCase.narrow ? column : Math.Min(4, column + 1);
            Assert.That(bounds.size.x, Is.EqualTo((last - first + 1) * .4f).Within(.001));
            Assert.That(visual.transform.localPosition.x + bounds.center.x,
                Is.EqualTo((first + last + 1) * .2f - 1).Within(.001));
            Assert.That(visual.transform.localPosition.x + bounds.min.x, Is.GreaterThanOrEqualTo(-1.001f));
            Assert.That(visual.transform.localPosition.x + bounds.max.x, Is.LessThanOrEqualTo(1.001f));
            var material = visual.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(material.HasProperty("_HeadRange"), Is.True);
            var head = material.GetVector("_HeadRange");
            float coreInset = testCase.narrow ? 0 : .375f;
            Assert.That(head.x, Is.EqualTo((column - first + coreInset) / (last - first + 1)).Within(.001));
            Assert.That(head.y, Is.EqualTo((column - first + 1 - coreInset) / (last - first + 1)).Within(.001));
            if (!testCase.lefty)
                Capture(new[] { visual, Receptor(Fixture.Player, EliteDrumPad.Snare),
                    Receptor(Fixture.Player, EliteDrumPad.HiHat), Receptor(Fixture.Player, EliteDrumPad.Tom1),
                    Receptor(Fixture.Player, EliteDrumPad.Tom2), Receptor(Fixture.Player, EliteDrumPad.Tom3) }, $"width-{testCase.column}-{(testCase.narrow ? "one-fret" : "pedal")}");
        }

        private static readonly double[] VisualClockOffsets = { -.1, .1 };

        [UnityTest]
        public IEnumerator ExpansionUsesElapsedInputTimeDespiteADifferentVisualClock(
            [ValueSource(nameof(VisualClockOffsets))] double visualOffset)
        {
            yield return Initialize();
            var visual = Visual();
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Render(visual, 1 + visualOffset);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            var envelopes = new List<Vector2>(); mesh.GetUVs(4, envelopes);
            Assert.That(envelopes.All(value => Math.Abs(value.x) < .001), Is.True,
                "Video/input calibration must not skip or delay the first expansion frame.");
            ((EliteDrumsEngine)Engine(Fixture.Player)).Update(1.03125);
            Render(visual, 1.03125 + visualOffset); mesh.GetUVs(4, envelopes);
            Assert.That(envelopes.All(value => Math.Abs(value.x - .15625f) < .001), Is.True);
        }

        [UnityTest]
        public IEnumerator ReplayInputReconstructionSurvivesTheFollowingVisualReset()
        {
            yield return Initialize(notes: ConnectedOpens());
            var engine = (EliteDrumsEngine)Engine(Fixture.Player);
            engine.ProcessUpToTime(1.07, new[] { GameInput.Create(1.06, EliteDrumsAction.EliteOpenHiHat, .8f) });
            // BasePlayer.SetReplayTime uses this ordering. Supply the real reset's
            // otherwise-unrelated BRE UI dependency without running the whole HUD.
            var hud = new GameObject("Replay reset BRE fixture"); hud.SetActive(false);
            var bre = hud.AddComponent(Production("YARG.Gameplay.HUD.BREBox"));
            var imageObject = new GameObject("Box", typeof(RectTransform)); imageObject.transform.SetParent(hud.transform, false);
            var textObject = new GameObject("Text", typeof(RectTransform)); textObject.transform.SetParent(hud.transform, false);
            var image = imageObject.AddComponent(Production("UnityEngine.UI.Image"));
            var text = textObject.AddComponent(Production("TMPro.TextMeshProUGUI"));
            bre.GetType().GetField("_breBox", INSTANCE).SetValue(bre, image);
            bre.GetType().GetField("_breFullText", INSTANCE).SetValue(bre, text);
            bre.GetType().GetField("_breBoxCanvasGroup", INSTANCE).SetValue(bre, hud.AddComponent<CanvasGroup>());
            var field = Fixture.Manager.GetType().GetField("_breBox", INSTANCE);
            object saved = field.GetValue(Fixture.Manager);
            try
            {
                field.SetValue(Fixture.Manager, bre);
                Fixture.Player.GetType().GetMethod("ResetVisuals", INSTANCE).Invoke(Fixture.Player, null);
            }
            finally
            {
                field.SetValue(Fixture.Manager, saved);
                UnityEngine.Object.DestroyImmediate(hud);
            }
            var visual = Visual(); Render(visual, 1.07);
            var envelopes = new List<Vector2>(); visual.GetComponent<MeshFilter>().sharedMesh.GetUVs(4, envelopes);
            Assert.That(envelopes.All(value => Math.Abs(value.x - .018176f) < .001), Is.True,
                "Preserve the late hit's activation time after replay inputs have been reconstructed.");
        }

        [UnityTest]
        public IEnumerator IsolatedOpenWidthStartsAtOneFretAndGrowsOverASixteenthAfterTheHit()
        {
            yield return Initialize();
            var visual = Visual(); Render(visual, .9);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            var envelopes = new List<Vector2>(); mesh.GetUVs(4, envelopes);
            Assert.That(envelopes.Count, Is.EqualTo(mesh.vertexCount), "Supply the display width separately from the fixed mesh footprint.");
            Assert.That(envelopes.All(value => value.x == 0), Is.True);
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Render(visual, 1); mesh.GetUVs(4, envelopes);
            Assert.That(envelopes.All(value => value.x == 0), Is.True, "Expansion starts at the actual hit.");
            ((EliteDrumsEngine)Engine(Fixture.Player)).Update(1.0625);
            Render(visual, 1.0625); mesh.GetUVs(4, envelopes);
            Assert.That(envelopes.All(value => Math.Abs(value.x - .5f) < .001), Is.True);
            ((EliteDrumsEngine)Engine(Fixture.Player)).Update(1.125);
            Render(visual, 1.125); mesh.GetUVs(4, envelopes);
            Assert.That(envelopes.All(value => value.x == 1), Is.True);
            Assert.That(visual.GetComponent<MeshRenderer>().sharedMaterial.GetVector("_HeadRange").w, Is.GreaterThan(0));
        }

        private static EliteDrumNote Splash(double time) => new(EliteDrumPad.HatPedal, DrumNoteType.Neutral,
            EliteDrumsHatState.Open, EliteDrumsHatPedalType.Splash, false, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumsChannelFlag.None, time, (uint)(time * 960), false,
            new EliteDrumSourceDefinition("splash", (int)(time * 960), (int)EliteDrumPad.HatPedal,
                (uint)(time * 960), 1920, time, 2));

        private static readonly int[] PedalOriginCases = { 0, 1, 2, 3 };

        [UnityTest]
        public IEnumerator PedalOriginTailsAreAlreadyWideBeforeAndAtActivation([ValueSource(nameof(PedalOriginCases))] int origin)
        {
            var generator = origin == 0 ? Splash(1) : Open(1, 1920);
            if (origin == 1) generator.AddChildNote(Splash(1));
            if (origin == 2) generator.AddChildNote(Note(EliteDrumPad.HatPedal, 1));
            if (origin == 3) generator.AddChildNote(new EliteDrumNote(EliteDrumPad.HatPedal, DrumNoteType.Neutral,
                EliteDrumsHatState.Closed, EliteDrumsHatPedalType.InvisibleTerminator, false, DrumNoteFlags.None,
                NoteFlags.None, EliteDrumsChannelFlag.None, 1, 960, false));
            var closed = Open(1.5, 0); closed.HatState = EliteDrumsHatState.Closed;
            yield return Initialize(notes: new[] { generator, closed, Note(EliteDrumPad.Snare, 5) });
            var visual = Visual(); Render(visual, .9);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            var envelopes = new List<Vector2>(); mesh.GetUVs(4, envelopes);
            Assert.That(envelopes.Count, Is.EqualTo(mesh.vertexCount));
            Assert.That(envelopes.All(value => value.x == (origin == 3 ? 0 : 1)), Is.True,
                "Only visible pedal gems make a sustain full width; hidden pedal markers do not.");
            if (origin == 0) Capture(new[] { visual, Receptor(Fixture.Player, EliteDrumPad.HiHat) }, "pedal-waiting-hard-end", 28);
            Press(Fixture.Player, origin == 0 ? EliteDrumsAction.EliteSplash : EliteDrumsAction.EliteOpenHiHat, 1);
            Render(visual, 1); mesh.GetUVs(4, envelopes);
            Assert.That(envelopes.All(value => value.x == (origin == 3 ? 0 : 1)), Is.True);
            if (origin == 0) Capture(new[] { visual, Receptor(Fixture.Player, EliteDrumPad.HiHat) }, "pedal-held-hard-end", 28);
        }

        private static readonly int[] EndingCases = { 0, 1, 2 };

        [UnityTest]
        public IEnumerator DisplayFadeRespectsNaturalVersusCleanAndPedalEndings([ValueSource(nameof(EndingCases))] int ending)
        {
            var generator = ending == 2 ? Splash(1) : Open(1, 0);
            var closed = Open(1.5, 0); closed.HatState = EliteDrumsHatState.Closed;
            yield return Initialize(notes: ending == 0 ? new[] { generator, Note(EliteDrumPad.Snare, 5) }
                : new[] { generator, closed, Note(EliteDrumPad.Snare, 5) });
            var visual = Visual(); Render(visual, .9);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            var envelopes = new List<Vector2>(); mesh.GetUVs(4, envelopes);
            Assert.That(envelopes.Count, Is.EqualTo(mesh.vertexCount));
            Assert.That(envelopes.All(value => value.y == ending), Is.True);
            var sustain = ((EliteDrumsEngine)Engine(Fixture.Player)).HiHatSustains[0];
            float speed = (float)Fixture.Player.GetType().GetProperty("NoteSpeed").GetValue(Fixture.Player);
            double duration = ending == 0 ? sustain.EndTime - sustain.FadeStartTime : 1d / 32;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                double time = (mesh.vertices[i].z + 2) / speed + .9;
                double expected = ending == 2 ? 1 : (sustain.EndTime - time) / duration;
                Assert.That(mesh.uv2[i].x, Is.EqualTo(expected).Within(.0001), "Clean ends must not inherit the full-lifetime decay.");
            }
        }

        [UnityTest]
        public IEnumerator SlowWaveClockCrossesQuarterBoundariesWithoutJumpingAndReconstructsAfterRewind()
        {
            yield return Initialize();
            var visual = Visual();
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Render(visual, 1.49);
            Assert.That(mesh.uv4[0].y, Is.EqualTo(.98).Within(.00001));
            Render(visual, 1.51);
            var paused = mesh.uv4;
            Assert.That(paused[0].y, Is.EqualTo(.02).Within(.00001));
            Render(visual, 1.51);
            Assert.That(mesh.uv4, Is.EqualTo(paused), "Repeated visual time must freeze the wave clock.");
            var coordinates = new List<Vector4>(); mesh.GetUVs(3, coordinates);
            Assert.That(coordinates.Min(value => value.w), Is.EqualTo(0).Within(.00001), "Held rounding stays at the fret.");
            Render(visual, 1.49);
            Assert.That(mesh.uv4[0].y, Is.EqualTo(.98).Within(.00001));
        }

        [UnityTest]
        public IEnumerator RoundFadeUsesTheTrueEndpointInsteadOfTheClippedMeshEnd()
        {
            yield return Initialize();
            var visual = Visual();
            float speed = (float)Fixture.Player.GetType().GetProperty("NoteSpeed").GetValue(Fixture.Player);
            visual.GetType().GetMethod("Render").Invoke(visual, new object[] { .9d, speed, 0f });
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            var coordinates = new List<Vector4>(); mesh.GetUVs(3, coordinates);
            Assert.That(coordinates.Count, Is.EqualTo(mesh.vertexCount));
            Assert.That(coordinates.Min(value => value.z), Is.GreaterThan(1), "The horizon is not the authored sustain end.");
            var engine = (EliteDrumsEngine)Engine(Fixture.Player);
            for (int i = 0; i < coordinates.Count; i++)
            {
                double time = (mesh.vertices[i].z + 2) / speed + .9;
                Assert.That(coordinates[i].z, Is.EqualTo((engine.HiHatSustains[0].EndTime - time) * speed).Within(.0001));
            }
        }

        [UnityTest]
        public IEnumerator WaitingGlowUsesTheRequestedTwentyPercentDefault()
        {
            yield return Initialize();
            var visual = Visual(); Render(visual, .9);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.colors[0].a, Is.EqualTo(.28f).Within(.001), "Use the selected 28% waiting opacity by default.");
            var glow = visual.GetComponent<MeshRenderer>().sharedMaterial.GetVector("_GlowLevels");
            Assert.That(glow.x, Is.EqualTo(.35f).Within(.001));
            Assert.That(glow.y, Is.EqualTo(.7f).Within(.001));
        }

        [UnityTest]
        public IEnumerator HiddenVisualKeepsScoringAndComboWhileSuppressingTailAndFretEffects()
        {
            var profile = Profile();
            var field = profile.GetType().GetField("HideHiHatSustains");
            Assert.That(field, Is.Not.Null, "Each profile needs its own visual-off choice.");
            field.SetValue(profile, true);
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(PreparedElitePlayerFixture.CreateChart(Open(1, 1920)), profile,
                YARG.Core.Game.EliteDrumsLayoutPreset.Full, noFail: true);
            var visual = Visual();
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Fixture.Player.GetType().GetMethod("UpdateHiHatSustains", INSTANCE).Invoke(Fixture.Player, new object[] { 2d });
            Assert.That(visual.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.Zero);
            var engine = (EliteDrumsEngine)Engine(Fixture.Player);
            Assert.That(engine.ActiveHiHatSustain, Is.Not.Null);
            engine.Update(3.1);
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(50));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
            var hat = Receptor(Fixture.Player, EliteDrumPad.HiHat);
            var bind = hat.GetType().GetProperty("ThemeBind").GetValue(hat);
            Assert.That(((Animator)bind.GetType().GetProperty("Animator").GetValue(bind)).GetBool("Sustain"), Is.False);
        }

        private static readonly (double bpm, bool change, double halfAttack, double fullAttack)[] TempoCases = {
            (60, false, .125, .25), (120, false, .0625, .125), (240, false, .03125, .0625), (120, true, .09375, .21875) };

        [UnityTest]
        public IEnumerator WidthExpansionUsesASixteenthAcrossTempoChanges(
            [ValueSource(nameof(TempoCases))] (double bpm, bool change, double halfAttack, double fullAttack) testCase)
        {
            yield return Initialize(timing: sync =>
            {
                sync.Tempos.Clear(); sync.Tempos.Add(new TempoChange(testCase.bpm, 0, 0));
                if (testCase.change) sync.Tempos.Add(new TempoChange(60, 1.03125, 990));
            });
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            var engine = (EliteDrumsEngine)Engine(Fixture.Player);
            var visual = Visual(); var values = new List<Vector2>();
            engine.Update(1 + testCase.halfAttack); Render(visual, 1 + testCase.halfAttack);
            visual.GetComponent<MeshFilter>().sharedMesh.GetUVs(4, values);
            Assert.That(values.All(value => Math.Abs(value.x - .5f) < .001), Is.True);
            engine.Update(1 + testCase.fullAttack); Render(visual, 1 + testCase.fullAttack);
            visual.GetComponent<MeshFilter>().sharedMesh.GetUVs(4, values);
            Assert.That(values.All(value => value.x == 1), Is.True);
        }

        private static readonly (double first, double second, double boundary, double visibleBoundary, double beatDistance)[] WaveTempoCases = {
            (120, 60, 1.25, 1.25, .3), (60, 120, 2.5, 1.5, .4), (240, 120, .625, 1.125, .1) };

        [UnityTest]
        public IEnumerator WaveSpacingTracksTempoChangesAndTheHeldFretWithoutTickRounding(
            [ValueSource(nameof(WaveTempoCases))] (double first, double second, double boundary, double visibleBoundary, double beatDistance) testCase)
        {
            yield return Initialize(timing: sync =>
            {
                sync.Tempos.Clear(); sync.Tempos.Add(new TempoChange(testCase.first, 0, 0));
                sync.Tempos.Add(new TempoChange(testCase.second, testCase.boundary, 1200));
            });
            var visual = Visual();
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Render(visual, 1.1);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.uv4.Length, Is.EqualTo(mesh.vertexCount), "Wave coordinates must be supplied in musical time.");
            float speed = (float)Fixture.Player.GetType().GetProperty("NoteSpeed").GetValue(Fixture.Player);
            var rows = mesh.vertices.Select((vertex, i) => (time: (vertex.z + 2) / speed + 1.1, wave: mesh.uv4[i]))
                .GroupBy(value => Math.Round(value.time, 5)).Select(group => group.First()).OrderBy(value => value.time).ToArray();
            Assert.That(rows.Length, Is.EqualTo(3), "A tempo change needs its own row; endpoint interpolation changes wave spacing.");
            Assert.That(rows[0].wave.x, Is.EqualTo(0).Within(.00001));
            Assert.That(mesh.uv4.All(value => Math.Abs(value.y - testCase.first / 600) < .00001), Is.True,
                "All vertices share a smooth source-offset song clock, independent of the pulse clock.");
            Assert.That(rows[1].time, Is.EqualTo(testCase.visibleBoundary).Within(.00001));
            Assert.That(rows[1].wave.x, Is.EqualTo(testCase.beatDistance).Within(.00001));
            Assert.That(rows[2].wave.x - rows[1].wave.x, Is.EqualTo((rows[2].time - rows[1].time) * testCase.second / 60).Within(.00001));
            Render(visual, 1.1001); // deliberately smaller than one MIDI tick
            Assert.That(mesh.uv4.Min(uv => uv.x), Is.EqualTo(0).Within(.00001));
            Render(visual, 1.05); // visual rewind reconstructs the same fret-relative origin
            Assert.That(mesh.uv4.Min(uv => uv.x), Is.EqualTo(0).Within(.00001));
        }

        [UnityTest]
        public IEnumerator VisualClockRollbackDoesNotLoseTheReconstructedExpansionState()
        {
            yield return Initialize(notes: ConnectedOpens());
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, .95);
            ((EliteDrumsEngine)Engine(Fixture.Player)).Update(1.02);
            var visual = Visual(); Render(visual, 1.02);
            var before = UpcomingWidths(visual);
            Assert.That(before.All(value => value.x > .5f), Is.True);
            Render(visual, .95);
            Assert.That(UpcomingWidths(visual).Select(value => value.x), Is.EqualTo(before.Select(value => value.x)),
                "Geometry rollback must not discard input-clock history rebuilt by replay seeking.");
        }

        private static readonly bool[] SplashStarts = { false, true };

        [UnityTest]
        public IEnumerator SplashAndSharedOpenSplashBeginWithFullWidth(
            [ValueSource(nameof(SplashStarts))] bool shared)
        {
            var splash = new EliteDrumNote(EliteDrumPad.HatPedal, DrumNoteType.Neutral, EliteDrumsHatState.Open,
                EliteDrumsHatPedalType.Splash, false, DrumNoteFlags.None, NoteFlags.None,
                EliteDrumsChannelFlag.None, 1, 960, false,
                new EliteDrumSourceDefinition("splash", 960, (int)EliteDrumPad.HatPedal, 960, 1920, 1, 2));
            if (shared) splash.AddChildNote(Open(1, 1920));
            yield return Initialize(notes: new[] { splash, Note(EliteDrumPad.Snare, 5) });
            var visual = Visual(); Render(visual, 0);
            Assert.That(visual.GetComponent<MeshFilter>().sharedMesh.uv3.All(uv => uv.y == 1), Is.True);
        }

        [UnityTest]
        public IEnumerator GameplayAnglePreviewUsesRealGemsAndChartedCutoff()
        {
            var first = Open(1, 1920); var next = Open(1.5, 1920);
            next.HatState = EliteDrumsHatState.Closed;
            Fixture = new InitializedElitePlayerFixture();
            yield return Fixture.Initialize(PreparedElitePlayerFixture.CreateChart(first, next, Note(EliteDrumPad.Snare, 5)),
                Profile(), YARG.Core.Game.EliteDrumsLayoutPreset.Compact, noFail: true);
            var visual = Visual();
            float speed = (float)Fixture.Player.GetType().GetProperty("NoteSpeed").GetValue(Fixture.Player);
            visual.GetType().GetMethod("Render").Invoke(visual, new object[] { .9d, speed, 15f });
            var subjects = new List<Component> { visual, Receptor(Fixture.Player, EliteDrumPad.HiHat),
                Receptor(Fixture.Player, EliteDrumPad.Snare), Receptor(Fixture.Player, EliteDrumPad.LeftCrash),
                Receptor(Fixture.Player, EliteDrumPad.Tom1), Receptor(Fixture.Player, EliteDrumPad.Tom2),
                Receptor(Fixture.Player, EliteDrumPad.Tom3), Receptor(Fixture.Player, EliteDrumPad.Ride),
                Receptor(Fixture.Player, EliteDrumPad.RightCrash) };
            foreach (var note in new[] { first, next })
            {
                Fixture.Player.GetType().GetMethod("SpawnNote", INSTANCE).Invoke(Fixture.Player, new object[] { note });
                var element = Fixture.Player.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                    .Single(item => ReferenceEquals(item.GetType().GetProperty("NoteRef").GetValue(item), note));
                var position = element.transform.localPosition;
                position.z = (float)(note.Time - .9) * speed - 2;
                element.transform.localPosition = position;
                subjects.Add(element);
            }
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.uv2.Min(uv => uv.x), Is.Zero);
            Capture(subjects, "open-onset-and-charted-cut-end", 28);
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            ((EliteDrumsEngine)Engine(Fixture.Player)).Update(1.05);
            Render(visual, 1.05);
            Assert.That(mesh.colors[0].a, Is.EqualTo(.33f).Within(.001));
            Capture(subjects, "held-round-fade", 28);
        }

        [UnityTest]
        public IEnumerator RenewedOpenHatEndsFadeBeforeTheNextGem()
        {
            yield return Initialize(notes: new[] { Open(1, 1920), Open(2, 1920), Note(EliteDrumPad.Snare, 5) });
            var visual = Visual(); Render(visual, 0);
            var engine = (EliteDrumsEngine)Engine(Fixture.Player);
            Assert.That(engine.HiHatSustains[0].HasFade, Is.False, "Reproduce the chart-timed clean-cut case.");
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            float speed = (float)Fixture.Player.GetType().GetProperty("NoteSpeed").GetValue(Fixture.Player);
            var ends = mesh.vertices.Select((vertex, i) => (vertex, i)).Where(item => Mathf.Abs(item.vertex.z - (2 * speed - 2)) < .001);
            Assert.That(ends.Min(item => mesh.uv2[item.i].x), Is.Zero,
                "The previous tail must visually fade even when its endpoint is the next open gem.");
        }

        [UnityTest]
        public IEnumerator WideTailContainsInteriorVerticesToFollowHighwayCurvature()
        {
            yield return Initialize();
            var visual = Visual(); Render(visual, 0);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            var across = mesh.vertices.Select(vertex => vertex.x).Distinct().OrderBy(x => x).ToArray();
            Assert.That(across.Length, Is.GreaterThan(2), "A wide single quad cuts below the curved highway.");
            for (int i = 1; i < across.Length; i++)
                Assert.That(across[i] - across[i - 1], Is.LessThanOrEqualTo(.065f),
                    "Keep curved-surface chord error below the tail's highway clearance.");
        }

        [UnityTest]
        public IEnumerator UninitializedPlayerCanReleaseSustainEffectsDuringDestruction()
        {
            var root = new GameObject("Uninitialized Elite sustain cleanup");
            var player = root.AddComponent(Production("YARG.Gameplay.Player.EliteDrumsPlayer"));
            try
            {
                Assert.DoesNotThrow(() => player.GetType().GetMethod("SetHiHatSustained", INSTANCE)
                    .Invoke(player, new object[] { false }));
            }
            finally { UnityEngine.Object.Destroy(root); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator TailSurvivesHitUsesCommonLaneAndDrivesGuitarStyleFretSustain()
        {
            yield return Initialize();
            var visual = Visual();
            Render(visual, 0);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            var hat = Receptor(Fixture.Player, EliteDrumPad.HiHat);
            Assert.That(Receptor(Fixture.Player, EliteDrumPad.HatPedal), Is.SameAs(hat));
            float width = 2f / (int)Fixture.Player.GetType().GetProperty("LaneCount").GetValue(Fixture.Player);
            Assert.That(mesh.bounds.size.x, Is.EqualTo(width * 3).Within(.001));
            Assert.That(visual.transform.localPosition.x + mesh.bounds.center.x,
                Is.EqualTo(Fixture.Player.transform.InverseTransformPoint(hat.transform.position).x).Within(.001));
            float waiting = mesh.colors[0].a;
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Render(visual, 1.1);
            Assert.That(mesh.vertexCount, Is.GreaterThan(0), "Returning the hit gem must not return its ringing tail.");
            Assert.That(mesh.colors[0].a, Is.GreaterThan(waiting));
            var bind = hat.GetType().GetProperty("ThemeBind").GetValue(hat);
            var animator = (Animator)bind.GetType().GetProperty("Animator").GetValue(bind);
            Assert.That(animator.GetBool("Sustain"), Is.True);
            var sustainEffect = (Component)bind.GetType().GetProperty("SustainEffect").GetValue(bind);
            Assert.That(sustainEffect.GetComponentsInChildren<ParticleSystem>(true).Any(p => p.isPlaying), Is.True);
            if (animator.runtimeAnimatorController is AnimatorOverrideController controller)
            {
                var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                controller.GetOverrides(clips);
                Assert.That(clips.Where(pair => pair.Key.name == "FretSustained")
                    .All(pair => pair.Value == pair.Key), Is.True,
                    "The native hat must retain its theme's actual guitar sustain motion.");
            }
            Capture(new[] { visual, hat }, "hihat-held", 28);
            Press(Fixture.Player, EliteDrumsAction.EliteStomp, 1.4);
            Render(visual, 1.4);
            Assert.That(animator.GetBool("Sustain"), Is.False);
            Assert.That(mesh.colors[0].a, Is.LessThan(waiting));
            Assert.That(((EliteDrumsEngine)Engine(Fixture.Player)).EngineStats.Overhits, Is.Zero);
            Capture(new[] { visual, hat }, "hihat-broken");
        }

        [UnityTest]
        public IEnumerator LeftyNaturalFadeResetAndOwnershipAreConsistent()
        {
            yield return Initialize(true);
            var visual = Visual();
            Render(visual, .5);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.uv2.Max(uv => uv.x), Is.GreaterThan(1),
                "Keep the natural body opaque until Core's authored fade boundary, then clamp per pixel.");
            Assert.That(mesh.uv2.Min(uv => uv.x), Is.EqualTo(0).Within(.001), "Natural tails must reach zero alpha at the authored endpoint.");
            var material = visual.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(material.shader.isSupported, Is.True);
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Render(visual, 1.2);
            ((EliteDrumsEngine)Engine(Fixture.Player)).Update(3.1);
            Render(visual, 3.1);
            Assert.That(mesh.vertexCount, Is.Zero);
            var hat = Receptor(Fixture.Player, EliteDrumPad.HiHat);
            var bind = hat.GetType().GetProperty("ThemeBind").GetValue(hat);
            Assert.That(((Animator)bind.GetType().GetProperty("Animator").GetValue(bind)).GetBool("Sustain"), Is.False);
            ((EliteDrumsEngine)Engine(Fixture.Player)).Reset();
            Render(visual, 0);
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Capture(new[] { visual, hat }, "hihat-waiting-lefty");
            yield return Fixture.Dispose();
            Fixture = null;
            Assert.That(mesh == null, Is.True, "Destroying the player must release its owned mesh.");
            Assert.That(material == null, Is.True, "Destroying the player must release its owned material.");
        }

        [UnityTest]
        public IEnumerator DarkColorsRemainVisibleThroughTheSustainVisualUpdate()
        {
            yield return Initialize();
            var visual = Visual();
            visual.GetType().GetMethod("Configure").Invoke(visual, new object[] {
                Engine(Fixture.Player), 0f, .25f, Color.black });
            Fixture.Player.GetType().GetMethod("UpdateHiHatSustains", INSTANCE).Invoke(Fixture.Player, new object[] { 0d });
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(mesh.colors[0].r + mesh.colors[0].g + mesh.colors[0].b, Is.GreaterThan(.8f),
                "A dark profile color must retain the same minimum sustain luminance as guitar/keys.");
        }

        [UnityTest]
        public IEnumerator EarlyRenewalsTouchTheFretWithoutOverlappingThePreviousTail()
        {
            yield return Initialize(notes: new[] { Open(1, 0), Open(1.5, 1920), Note(EliteDrumPad.Snare, 5) });
            var visual = Visual();
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1.48);
            Fixture.Player.GetType().GetMethod("UpdateHiHatSustains", INSTANCE).Invoke(Fixture.Player, new object[] { 1.48d });
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertices.Select(vertex => vertex.z).Distinct().Count(), Is.EqualTo(2),
                "An early renewal must replace its preceding strip without overlapping faces.");
            Assert.That(mesh.bounds.min.z, Is.EqualTo(-2f).Within(.001), "A held tail must reach its fret even on an early hit.");
        }

        [UnityTest]
        public IEnumerator TailUsesTheProfilesOpenHatColorsForNormalAndStarPower()
        {
            yield return Initialize();
            var property = Fixture.Session.GetType().GetProperty("ColorProfile");
            var profile = (YARG.Core.Game.ColorProfile)((YARG.Core.Game.ColorProfile)property.GetValue(Fixture.Session))
                .CopyWithNewName("Sustain color coverage");
            profile.EliteDrums.HatOpenNote = System.Drawing.Color.Magenta;
            profile.EliteDrums.HatOpenStarpower = System.Drawing.Color.Cyan;
            property.SetValue(Fixture.Session, profile);
            Fixture.Player.GetType().GetMethod("ConfigureHiHatSustainVisual", INSTANCE)
                .Invoke(Fixture.Player, new[] { Engine(Fixture.Player) });
            var visual = Visual();
            Render(visual, 0);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.colors[0].r, Is.EqualTo(1));
            Assert.That(mesh.colors[0].g, Is.Zero);
            var generator = ((EliteDrumsEngine)Engine(Fixture.Player)).HiHatSustains.Single().Generators[0];
            generator.ActivateFlag(NoteFlags.StarPower);
            Render(visual, 0);
            Assert.That(mesh.colors[0].r, Is.Zero);
            Assert.That(mesh.colors[0].g, Is.EqualTo(1));
            Assert.That(mesh.colors[0].b, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ChartedClosedHatAlsoFadesToZeroAndStopsSustainedFretEffects()
        {
            var closed = Open(2, 0);
            closed.HatState = EliteDrumsHatState.Closed;
            yield return Initialize(notes: new[] { Open(1, 1920), closed, Note(EliteDrumPad.Snare, 5) });
            var visual = Visual();
            Render(visual, 0);
            Assert.That(visual.GetComponent<MeshFilter>().sharedMesh.uv2.Min(uv => uv.x), Is.EqualTo(0),
                "A charted cutoff must not bypass the opacity fade.");
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, 1);
            Press(Fixture.Player, EliteDrumsAction.EliteClosedHiHat, 1.98);
            var engine = (EliteDrumsEngine)Engine(Fixture.Player);
            Assert.That(engine.ActiveHiHatSustain, Is.Null);
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            var bind = Receptor(Fixture.Player, EliteDrumPad.HiHat).GetType().GetProperty("ThemeBind")
                .GetValue(Receptor(Fixture.Player, EliteDrumPad.HiHat));
            Assert.That(((Animator)bind.GetType().GetProperty("Animator").GetValue(bind)).GetBool("Sustain"), Is.False);
        }

        [UnityTest]
        public IEnumerator PulseUsesTheSharedVisualQuarterNoteClock()
        {
            yield return Initialize();
            var beat = Fixture.Manager.GetType().GetProperty("BeatEventHandler").GetValue(Fixture.Manager);
            beat.GetType().GetMethod("Update").Invoke(beat, new object[] { .75d, .75d });
            Fixture.Player.GetType().GetMethod("UpdateHiHatSustains", INSTANCE)
                .Invoke(Fixture.Player, new object[] { .75d });
            var material = Visual().GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(material.HasProperty("_BeatPhase"), Is.True);
            Assert.That(material.GetFloat("_BeatPhase"), Is.EqualTo(.5f).Within(.001));
        }

        private static void Capture(IEnumerable<Component> subjects, string name, float elevation = 45)
        {
            string directory = Environment.GetEnvironmentVariable("YARG_HIHAT_SUSTAIN_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            var renderers = subjects.SelectMany(item => item.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var root = new GameObject("Hi-hat sustain inspection camera");
            var camera = root.AddComponent<Camera>();
            camera.enabled = false;
            var render = new RenderTexture(640, 640, 24);
            var pixels = new Texture2D(640, 640, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var layers = renderers.Select(renderer => renderer.gameObject).Distinct().ToDictionary(item => item, item => item.layer);
            try
            {
                foreach (var item in layers.Keys) item.layer = 31;
                camera.cullingMask = 1 << 31;
                camera.transform.position = bounds.center + new Vector3(0, 8, -8 / Mathf.Tan(elevation * Mathf.Deg2Rad));
                camera.transform.LookAt(bounds.center, Vector3.up);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(.8f, bounds.extents.x + .1f,
                    bounds.extents.z * Mathf.Sin(elevation * Mathf.Deg2Rad) + bounds.extents.y + .3f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.025f, .03f, .045f);
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
                pixels.Apply();
                Assert.That(pixels.GetPixels32().Any(pixel => pixel.r > 50 || pixel.g > 50 || pixel.b > 50), Is.True);
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
                string motionDirectory = Environment.GetEnvironmentVariable("YARG_HIHAT_NATIVE_MOTION_CAPTURE_DIR");
                if ((name == "open-onset-and-charted-cut-end" || name == "hihat-held" || name == "held-round-fade" || name == "pedal-held-hard-end") && !string.IsNullOrEmpty(motionDirectory))
                {
                    var sustain = subjects.First(item => item.GetType().Name == "HiHatSustainVisual");
                    var material = sustain.GetComponent<MeshRenderer>().sharedMaterial;
                    float savedTime = material.GetFloat("_VisualTime"), savedBeat = material.GetFloat("_BeatPhase");
                    var waveMesh = sustain.GetComponent<MeshFilter>().sharedMesh;
                    var savedCoordinates = new List<Vector4>(); waveMesh.GetUVs(3, savedCoordinates);
                    var animatedCoordinates = new List<Vector4>(savedCoordinates);
                    var savedEnvelopes = new List<Vector3>(); waveMesh.GetUVs(4, savedEnvelopes);
                    var animatedEnvelopes = new List<Vector3>(savedEnvelopes);
                    bool held = waveMesh.uv2[0].y > .5f;
                    bool pedal = waveMesh.uv3[0].y > .5f;
                    System.IO.Directory.CreateDirectory(motionDirectory);
                    try
                    {
                        // Freeze note geometry to inspect only the glow's transient detail.
                        for (int frame = 0; frame < 180; frame++)
                        {
                            float time = savedTime + frame / 60f;
                            material.SetFloat("_VisualTime", time);
                            material.SetFloat("_BeatPhase", time * 2 % 1);
                            for (int i = 0; i < animatedCoordinates.Count; i++)
                            {
                                var value = savedCoordinates[i]; value.y = time * 2 - Mathf.Floor(time * 2);
                                animatedCoordinates[i] = value;
                            }
                            waveMesh.SetUVs(3, animatedCoordinates);
                            for (int i = 0; i < animatedEnvelopes.Count; i++)
                                animatedEnvelopes[i] = new Vector3(pedal ? 1 : held ? Mathf.SmoothStep(0, 1, (time - 1) * 8) : 0,
                                    savedEnvelopes[i].y, savedEnvelopes[i].z);
                            waveMesh.SetUVs(4, animatedEnvelopes);
                            camera.Render();
                            RenderTexture.active = render;
                            pixels.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); pixels.Apply();
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(motionDirectory, $"{name}-{frame:D3}.png"), pixels.EncodeToPNG());
                        }
                    }
                    finally
                    {
                        material.SetFloat("_VisualTime", savedTime); material.SetFloat("_BeatPhase", savedBeat);
                        waveMesh.SetUVs(3, savedCoordinates);
                        waveMesh.SetUVs(4, savedEnvelopes);
                    }
                }
            }
            finally
            {
                foreach (var pair in layers) if (pair.Key != null) pair.Key.layer = pair.Value;
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(pixels);
                render.Release();
                UnityEngine.Object.DestroyImmediate(render);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
