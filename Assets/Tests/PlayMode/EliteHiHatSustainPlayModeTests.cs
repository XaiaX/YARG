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
            Assert.That(head.x, Is.EqualTo((float)(column - first) / (last - first + 1)).Within(.001));
            Assert.That(head.y, Is.EqualTo((float)(column - first + 1) / (last - first + 1)).Within(.001));
            if (!testCase.lefty)
                Capture(new[] { visual, Receptor(Fixture.Player, EliteDrumPad.Snare),
                    Receptor(Fixture.Player, EliteDrumPad.HiHat), Receptor(Fixture.Player, EliteDrumPad.Tom1),
                    Receptor(Fixture.Player, EliteDrumPad.Tom2), Receptor(Fixture.Player, EliteDrumPad.Tom3) }, $"width-{testCase.column}-{(testCase.narrow ? "one-fret" : "pedal")}");
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

        [UnityTest]
        public IEnumerator StartSpreadLastsOneThirtySecondAndDoesNotRestartWhenClipped()
        {
            yield return Initialize();
            var visual = Visual();
            Render(visual, 1.03125); // half of a 1/32 at 120 BPM
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.uv3.Length, Is.EqualTo(mesh.vertexCount), "Mesh needs the authored onset envelope.");
            Assert.That(mesh.uv3.Min(uv => uv.x), Is.EqualTo(.5f).Within(.001));
            Assert.That(mesh.uv3.Max(uv => uv.x), Is.GreaterThan(1), "Clamp per pixel, not before interpolation.");
            Render(visual, 1.125);
            Assert.That(mesh.uv3.Min(uv => uv.x), Is.EqualTo(2).Within(.001));
        }

        private static readonly (double bpm, bool change, double halfAttack)[] TempoCases = {
            (60, false, .0625), (120, false, .03125), (240, false, .015625), (120, true, .046875) };

        [UnityTest]
        public IEnumerator SpreadUsesThirtySecondChartTimeIncludingTempoChanges(
            [ValueSource(nameof(TempoCases))] (double bpm, bool change, double halfAttack) testCase)
        {
            yield return Initialize(timing: sync =>
            {
                sync.Tempos.Clear(); sync.Tempos.Add(new TempoChange(testCase.bpm, 0, 0));
                if (testCase.change) sync.Tempos.Add(new TempoChange(60, 1.03125, 990));
            });
            var visual = Visual(); Render(visual, 1 + testCase.halfAttack);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.uv3.Min(uv => uv.x), Is.EqualTo(.5f).Within(.001));
            Render(visual, 1 + testCase.halfAttack * 2);
            Assert.That(mesh.uv3.Min(uv => uv.x), Is.EqualTo(1).Within(.001));
        }

        [UnityTest]
        public IEnumerator EarlyHitAndRewindKeepTheAuthoredOnset()
        {
            yield return Initialize();
            var visual = Visual();
            Press(Fixture.Player, EliteDrumsAction.EliteOpenHiHat, .95);
            Render(visual, .95);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.uv3.Min(uv => uv.x), Is.EqualTo(-.8f).Within(.001));
            Render(visual, 1.03125);
            Assert.That(mesh.uv3.Min(uv => uv.x), Is.EqualTo(.5f).Within(.001));
            Render(visual, .95);
            Assert.That(mesh.uv3.Min(uv => uv.x), Is.EqualTo(-.8f).Within(.001));
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
            Capture(new[] { visual, hat }, "hihat-held");
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
            Assert.That(mesh.uv2.Max(uv => uv.x), Is.EqualTo(1).Within(.001),
                "Natural decay must span the whole ringing interval, rather than only its final eighth.");
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
