using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    /// <summary>Exercises the authored Elite cymbal-accent model in the production preview renderer.</summary>
    public sealed class EliteCymbalAccentRenderPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;

        private GameObject _world;
        private object _builder;
        private Component _player;
        private Component _themeManager;
        private bool _ownsThemeManager;
        private RenderTexture _captureTarget;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _capturedSettings;
        private bool _ownsAudio;

        private static IEnumerator WaitForRenderedFrame()
        {
            bool rendered = false;
            void OnEndFrame(UnityEngine.Rendering.ScriptableRenderContext context, Camera[] cameras) => rendered = true;
            UnityEngine.Rendering.RenderPipelineManager.endFrameRendering += OnEndFrame;
            try
            {
                for (int frame = 0; frame < 120 && !rendered; frame++) yield return null;
            }
            finally
            {
                UnityEngine.Rendering.RenderPipelineManager.endFrameRendering -= OnEndFrame;
            }
            Assert.That(rendered, Is.True, "A rendered frame must arrive before capture.");
        }

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object Get(object target, string name)
        {
            var property = target.GetType().GetProperty(name, INSTANCE);
            return property != null ? property.GetValue(target) : target.GetType().GetField(name, INSTANCE).GetValue(target);
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetProperty(name, INSTANCE).SetValue(target, value);

        private static object Field(object target, string name) => target.GetType().GetField(name, INSTANCE).GetValue(target);

        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethods(INSTANCE).Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(target, args);

        private static object CallStatic(Type type, string name, params object[] args) => type
            .GetMethods(STATIC).Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(null, args);

        [UnitySetUp]
        public IEnumerator SetUpProductionTheme()
        {
            var settingsManager = Production("YARG.Settings.SettingsManager");
            var settingsContainerType = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
            var settingsInitialized = settingsContainerType.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            _originalSettings = settingsManager.GetProperty("Settings").GetValue(null);
            _originalSettingsInitialized = (bool)settingsInitialized.GetValue(null);
            _capturedSettings = true;

            var audioHandler = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audioHandler.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null)
            {
                Production("YARG.Helpers.PathHelper").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                var bassAudioManager = Production("YARG.Audio.BASS.BassAudioManager");
                audioHandler.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(bassAudioManager).Invoke(null, null);
                _ownsAudio = true;
            }

            if (_originalSettings == null)
            {
                var temporarySettings = Activator.CreateInstance(settingsContainerType);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { temporarySettings });
                settingsInitialized.SetValue(null, true);
            }

            var themeManagerType = Production("YARG.Themes.ThemeManager");
            _themeManager = (Component)themeManagerType.GetProperty("Instance", STATIC).GetValue(null);
            if (_themeManager == null)
            {
                _themeManager = new GameObject("Cymbal accent render test ThemeManager").AddComponent(themeManagerType);
                _ownsThemeManager = true;
            }
            yield return null;
            Assert.That(themeManagerType.GetProperty("Instance", STATIC).GetValue(null), Is.SameAs(_themeManager),
                "The production ThemeManager must load the real default note theme.");
            var presetType = Production("YARG.Themes.ThemePreset");
            var visualStyleType = Production("YARG.Themes.VisualStyle");
            var defaultPreset = presetType.GetField("Default", STATIC).GetValue(null);
            var eliteStyle = Enum.Parse(visualStyleType, "EliteDrums");
            Assert.That(Call(_themeManager, "GetThemeContainer", defaultPreset, eliteStyle), Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null && _captureTarget != null)
            {
                // Detach the owned target from the preview cameras (including HighwayCameraRendering)
                // before either side is destroyed, in the production menu's cleanup order.
                CallStatic(Production("YARG.Helpers.CameraPreviewTexture"), "ClearOwnedPreviewTargets",
                    _world.transform, _captureTarget);
            }
            if (_world != null) UnityEngine.Object.Destroy(_world);
            if (_captureTarget != null)
            {
                CallStatic(Production("YARG.Helpers.CameraPreviewTexture"), "ReleaseOwnedPreviewTexture",
                    _captureTarget);
            }
            _captureTarget = null;
            _world = null;
            yield return null;
            if (_ownsThemeManager && _themeManager != null)
            {
                UnityEngine.Object.Destroy(_themeManager.gameObject);
                yield return null;
            }
            _themeManager = null;
            _ownsThemeManager = false;

            if (_capturedSettings)
            {
                var settingsManager = Production("YARG.Settings.SettingsManager");
                var settingsContainerType = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { _originalSettings });
                settingsContainerType.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static)
                    .SetValue(null, _originalSettingsInitialized);
                _capturedSettings = false;
            }

            if (_ownsAudio)
            {
                Production("YARG.Core.Audio.GlobalAudioHandler").GetMethod("Close", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                _ownsAudio = false;
            }
        }

        private IEnumerator BuildElitePreview(bool? experimentalCymbals, int laneCount)
        {
            _world = new GameObject("Elite highway preview");
            var previewBuilderType = Production("YARG.Settings.Metadata.TrackPreviewBuilder");
            Func<string, GameObject> loadAsset = address =>
            {
                Assert.That(address, Is.EqualTo("SettingPreviews/TrackPreview"));
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SettingPreviews/Track/TrackPreview.prefab");
            };
            _builder = Activator.CreateInstance(previewBuilderType, new object[] { false, false, false, loadAsset });
            Set(_builder, "StartingGameMode", GameMode.EliteDrums);
            if (experimentalCymbals.HasValue)
                Set(_builder, "UseVolumeCymbals", experimentalCymbals.Value);
            Set(_builder, "EliteLayoutOverride", laneCount switch
            {
                5 => EliteDrumsLayoutPreset.Compact,
                8 => EliteDrumsLayoutPreset.Full,
                10 => TenLaneLayout(),
                _ => throw new ArgumentOutOfRangeException(nameof(laneCount))
            });
            var buildTask = Call(_builder, "BuildPreviewWorld", _world.transform);
            var awaiter = buildTask.GetType().GetMethod("GetAwaiter").Invoke(buildTask, null);
            awaiter.GetType().GetMethod("GetResult", Type.EmptyTypes).Invoke(awaiter, null);

            yield return null;
            var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            _player = _world.GetComponentInChildren(playerType, true);
            Assert.That(_player, Is.Not.Null, "TrackPreviewBuilder must instantiate its production FakeTrackPlayer.");
            Assert.That(Get(Get(_player, "CurrentGameModeInfo"), "LaneCount"), Is.EqualTo(laneCount),
                "The preview must resolve the requested layout.");
            ((Behaviour)_player).enabled = false;
        }

        private void SpotlightOpenHat(string strength, bool starPower)
        {
            var dynamics = Enum.Parse(Production("YARG.Themes.ThemeNoteType"), strength);
            var descriptor = CallStatic(Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator"),
                "Describe", EliteDrumsColorRole.HatOpen, dynamics);
            Call(_builder, "SpotlightEliteRole", EliteDrumsColorRole.HatOpen, starPower, descriptor);
        }

        private static readonly int[] HatPreviewLanes = { 5, 8 };

        [UnityTest]
        public IEnumerator EliteColorSpotlightShowsPairedDynamicsOnTheSelectedModels()
        {
            yield return BuildElitePreview(null, 5);
            var pool = Field(_player, "_notePool");
            var themeNote = Production("YARG.Themes.ThemeNote");
            foreach (var role in new[] { EliteDrumsColorRole.Snare, EliteDrumsColorRole.HatOpen,
                EliteDrumsColorRole.KickFlam, EliteDrumsColorRole.Splash })
            {
                bool star = role == EliteDrumsColorRole.HatOpen;
                Call(_builder, "SpotlightEliteColor", role, star);
                string[] models = role switch
                {
                    EliteDrumsColorRole.Snare => new[] { "Ghost", "Normal", "Accent", "EliteVFlamGhost",
                        "EliteVFlam", "EliteVFlamAccent", "EliteFlatFlamGhost", "EliteFlatFlam", "EliteFlatFlamAccent" },
                    EliteDrumsColorRole.HatOpen => new[] { "OpenHiHatGhost", "OpenHiHat", "OpenHiHatAccent",
                        "OpenHiHatFlamGhost", "OpenHiHatFlam", "OpenHiHatFlamAccent",
                        "OpenHiHatFlamGhost", "OpenHiHatFlam", "OpenHiHatFlamAccent" },
                    EliteDrumsColorRole.KickFlam => new[] { "KickFlamGhost", "KickFlam", "KickFlamAccent" },
                    _ => new[] { "HatSplash" }
                };
                for (int i = 0; i < models.Length * 2; i++)
                {
                    // Continuous color-picker edits must still let later dynamics and flams appear.
                    Call(_builder, "SpotlightEliteColor", role, star);
                    Call(pool, "ReturnAllObjects");
                    Call(_player, "Advance", .21d);
                    var gem = ((IEnumerable)Get(pool, "AllSpawned")).Cast<Component>().Single();
                    var data = Get(gem, "NoteRef");
                    Assert.That(Get(Get(data, "EliteDescriptor"), "Role"), Is.EqualTo(role));
                    Assert.That(Get(data, "ForceStarPower"), Is.EqualTo(star));
                    Assert.That(Get(data, "EliteFlatFlam"), Is.EqualTo(i >= 12));
                    Assert.That(Get(gem.GetComponentInChildren(themeNote), "NoteType").ToString(),
                        Is.EqualTo(models[i / 2]), $"{role}, sample {i + 1}");
                }
            }
            // Leave the splash color idle. It keeps the usual inspection time, then returns to the kit tour.
            for (int i = 0; i <= 16; i++)
            {
                Call(pool, "ReturnAllObjects");
                Call(_player, "Advance", .21d);
                var gem = ((IEnumerable)Get(pool, "AllSpawned")).Cast<Component>().Single();
                var role = Get(Get(Get(gem, "NoteRef"), "EliteDescriptor"), "Role");
                Assert.That(role, i < 16 ? Is.EqualTo(EliteDrumsColorRole.Splash) : Is.Not.EqualTo(EliteDrumsColorRole.Splash));
            }
        }

        [UnityTest]
        public IEnumerator KitTourFourNoteCollisionIsVisibleOnTheHighway([ValueSource(nameof(HatPreviewLanes))] int lanes)
        {
            yield return BuildElitePreview(true, lanes);
            var pool = Field(_player, "_notePool");
            var generator = Get(Get(_player, "CurrentGameModeInfo"), "Generator");
            int cycle = (int)generator.GetType().GetProperty("CycleLength").GetValue(null);
            var chord = Array.Empty<Component>();
            for (int i = 0; i < cycle; i++)
            {
                Call(pool, "ReturnAllObjects");
                Call(_player, "Advance", .21d);
                chord = ((IEnumerable)Get(pool, "AllSpawned")).Cast<Component>().ToArray();
                if (chord.Length == 4 && chord.Any(n =>
                    (EliteDrumsColorRole)Get(Get(Get(n, "NoteRef"), "EliteDescriptor"), "Role") == EliteDrumsColorRole.Tom3))
                    break;
            }

            Assert.That(chord.Select(n => Get(Get(Get(n, "NoteRef"), "EliteDescriptor"), "Role")),
                Is.EquivalentTo(new[] { EliteDrumsColorRole.RightCrash, EliteDrumsColorRole.Tom3,
                    EliteDrumsColorRole.Kick, EliteDrumsColorRole.Splash }));
            Assert.That(chord.Count(n => (bool)Get(Get(n, "NoteRef"), "OffsetElitePair")), Is.EqualTo(lanes == 5 ? 2 : 0));
            foreach (var gem in chord)
            {
                var data = Get(gem, "NoteRef");
                data.GetType().GetField("Time").SetValue(data, (double)Get(_player, "PreviewTime") + .12d);
                Call(gem, "RefreshPosition");
            }
            var targets = Production("YARG.Helpers.CameraPreviewTexture");
            _captureTarget = (RenderTexture)CallStatic(targets, "CreateOwnedPreviewTexture", 1280, 720, "Elite collision");
            CallStatic(targets, "SetOwnedPreviewTargets", _world.transform, _captureTarget);
            var camera = _world.GetComponentInChildren(Production("YARG.Gameplay.Visuals.HighwayCameraRendering")).GetComponent<Camera>();
            yield return WaitForRenderedFrame();
            camera.Render();
            var all = ReadFrame(_captureTarget);
            foreach (var gem in chord)
            {
                var renderers = gem.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                foreach (var renderer in renderers) renderer.enabled = false;
                camera.Render();
                var omitted = ReadFrame(_captureTarget);
                foreach (var renderer in renderers) renderer.enabled = true;
                Assert.That(Enumerable.Range(0, all.Length).Count(i => Math.Abs(all[i].r - omitted[i].r) > 8
                    || Math.Abs(all[i].g - omitted[i].g) > 8 || Math.Abs(all[i].b - omitted[i].b) > 8),
                    Is.GreaterThan(20), $"{Get(Get(Get(gem, "NoteRef"), "EliteDescriptor"), "Role")} visible at {lanes} lanes");
            }
        }

        [UnityTest]
        public IEnumerator VolumeCymbalFlamPreviewReusesOneJoinedModelForBothForms()
        {
            yield return BuildElitePreview(true, 5);
            SpotlightOpenHat("Accent", false);
            Call(_player, "Advance", .35d);
            var spawned = (IList)Get(Field(_player, "_notePool"), "AllSpawned");
            var note = (Component)spawned[spawned.Count - 1];
            var data = Get(note, "NoteRef");
            var themeNote = Production("YARG.Themes.ThemeNote");
            data.GetType().GetField("EliteHandFlam").SetValue(data, true);
            foreach (bool lefty in new[] { false, true })
            foreach (var layout in new[] { DrumFlamLayout.V, DrumFlamLayout.Bucket, DrumFlamLayout.Parallel })
            foreach (bool flat in new[] { true, false })
            {
                Set(_player, "LeftyFlip", lefty);
                Set(_player, "FlamLayout", layout);
                data.GetType().GetField("EliteFlatFlam").SetValue(data, flat);
                Call(note, "OnSettingChanged");
                var models = note.GetComponentsInChildren(themeNote);
                Assert.That(models, Has.Length.EqualTo(1));
                Assert.That(Get(models[0], "NoteType").ToString(), Is.EqualTo("OpenHiHatFlamAccent"));
                var marker = (GameObject)Field(models[0], "_flatFlamMarker");
                Assert.That(marker.activeInHierarchy, Is.EqualTo(flat));
            }
            data.GetType().GetField("EliteHandFlam").SetValue(data, false);
            Call(note, "OnSettingChanged");
            Assert.That(Get(note.GetComponentInChildren(themeNote), "NoteType").ToString(), Is.EqualTo("OpenHiHatAccent"));
        }

        [UnityTest]
        public IEnumerator ExperimentalGlowRowsPreviewTheirNoteFamilies()
        {
            var manager = Production("YARG.Settings.SettingsManager");
            var tab = ((IEnumerable)manager.GetField("AllSettingsTabs").GetValue(null)).Cast<object>()
                .Single(t => (string)Get(t, "Name") == "Experimental");
            var fakePlayer = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var themeNote = Production("YARG.Themes.ThemeNote");
            _world = new GameObject("Experimental glow settings preview");
            foreach (var row in new[] { "EliteOpenHatShadowPeak", "EliteCymbalFlamGlowMaximum" })
            {
                bool open = row == "EliteOpenHatShadowPeak";
                Call(tab, "OnSettingSelected", row);
                var task = Call(tab, "BuildPreviewWorld", _world.transform);
                var awaiter = task.GetType().GetMethod("GetAwaiter").Invoke(task, null);
                awaiter.GetType().GetMethod("GetResult").Invoke(awaiter, null);
                yield return null;
                _player = _world.GetComponentInChildren(fakePlayer);
                Assert.That(_player, Is.Not.Null, row);
                ((Behaviour)_player).enabled = false;
                var pool = Field(_player, "_notePool");
                var seen = new System.Collections.Generic.HashSet<string>();
                int cycle = open ? 9 : 36;
                for (int i = 0; i < cycle; i++)
                {
                    Call(_player, "Advance", .35d);
                    var live = ((IEnumerable)Get(pool, "AllSpawned")).Cast<Component>()
                        .OrderByDescending(n => (double)Get(Get(n, "NoteRef"), "Time")).First();
                    var data = Get(live, "NoteRef");
                    var role = ((EliteDrumsColorRole)Get(Get(data, "EliteDescriptor"), "Role")).ToString();
                    string type = Get(live.GetComponentInChildren(themeNote), "NoteType").ToString();
                    if (open) Assert.That(type, Does.StartWith("OpenHiHat"));
                    else Assert.That(type, Does.Contain("Flam"));
                    seen.Add(role + type + Get(data, "EliteFlatFlam"));
                }
                Assert.That(seen.Count, Is.EqualTo(cycle), row);
                // Switching between endpoints must keep the current preview and cycle alive.
                var builder = Get(tab, "PreviewBuilder");
                Call(tab, "OnSettingSelected", open ? "EliteOpenHatGhostGlow" : "EliteCymbalFlamGlowMinimum");
                Assert.That(Get(tab, "PreviewBuilder"), Is.SameAs(builder));
                foreach (Transform child in _world.transform.Cast<Transform>().ToArray())
                    UnityEngine.Object.Destroy(child.gameObject);
                yield return null;
            }
            Call(tab, "OnTabExit");
        }

        [UnityTest]
        public IEnumerator CymbalFlamPulseSeparatesTheBellsOnTheHighway([ValueSource(nameof(HatPreviewLanes))] int lanes)
        {
            yield return BuildElitePreview(true, lanes);
            var normal = Enum.Parse(Production("YARG.Themes.ThemeNoteType"), "Normal");
            var descriptor = CallStatic(Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator"),
                "Describe", EliteDrumsColorRole.HatClosed, normal);
            Call(_builder, "SpotlightEliteRole", EliteDrumsColorRole.HatClosed, false, descriptor);
            Call(_player, "Advance", .35d);
            var spawned = (IList)Get(Field(_player, "_notePool"), "AllSpawned");
            var note = (Component)spawned[spawned.Count - 1];
            foreach (Component other in spawned)
                if (other != note) other.gameObject.SetActive(false);
            var data = Get(note, "NoteRef");
            data.GetType().GetField("EliteHandFlam").SetValue(data, true);
            data.GetType().GetField("Time").SetValue(data, (double)Get(_player, "PreviewTime") + .12d);
            var targets = Production("YARG.Helpers.CameraPreviewTexture");
            _captureTarget = (RenderTexture)CallStatic(targets, "CreateOwnedPreviewTexture", 1280, 720, "Cymbal flam pulse");
            CallStatic(targets, "SetOwnedPreviewTargets", _world.transform, _captureTarget);
            var camera = _world.GetComponentInChildren(Production("YARG.Gameplay.Visuals.HighwayCameraRendering")).GetComponent<Camera>();
            yield return WaitForRenderedFrame();

            const string gains = "_YargCymbalFlamGlowAdjustments";
            const string clock = "_YargFlamGlowPhase";
            var oldGains = Shader.GetGlobalVector(gains);
            var controls = Activator.CreateInstance(Production("YARG.Settings.SettingsManager").GetNestedType("SettingContainer"));
            var maximum = Get(controls, "EliteCymbalFlamGlowMaximum");
            var oldClock = Shader.GetGlobalFloat(clock);
            Color32[] Frame(float beat)
            {
                Shader.SetGlobalFloat(clock, beat);
                camera.Render();
                return ReadFrame(_captureTarget);
            }

            try
            {
                Shader.SetGlobalVector(gains, Vector4.zero);
                float previousLeft = 0f;
                foreach (bool flat in new[] { true, false })
                foreach (bool lefty in new[] { false, true })
                {
                    Set(_player, "LeftyFlip", lefty);
                    data.GetType().GetField("EliteFlatFlam").SetValue(data, flat);
                    Call(note, "OnSettingChanged");
                    Call(note, "RefreshPosition");
                    var group = note.GetComponentInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"));
                    var top = group.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Top");
                    Assert.That(top.sharedMaterials[1].GetVector("_CymbalFlamPulse").z, Is.EqualTo(2f), "Preview clock");
                    foreach (float rate in new[] { .25f, .5f, 1f, 2f, 4f })
                    {
                        Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.Static, rate, true, lefty);
                        Assert.That(top.sharedMaterials[1].GetVector("_CymbalFlamPulse").x,
                            Is.EqualTo(flat ? rate : rate * .5f), $"Bell cadence at {rate} pulses per quarter");
                    }
                    // Hold both timing samples on the same rendered frame so shine and highway scroll cancel.
                    Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.Static, 1f, false, lefty);
                    var beat = Frame(1f);
                    var opposite = Frame(flat ? 1.5f : 2f);
                    var mask = Enumerable.Range(0, beat.Length).Where(i => Math.Abs(beat[i].g - opposite[i].g) > 8).ToArray();
                    Assert.That(mask.Length, Is.GreaterThan(24), $"Visible bell pixels ({lanes} lanes)");
                    int middle = (mask.Min(i => i % _captureTarget.width) + mask.Max(i => i % _captureTarget.width)) / 2;
                    int left = mask.Where(i => i % _captureTarget.width < middle).Sum(i => beat[i].g - opposite[i].g);
                    int right = mask.Where(i => i % _captureTarget.width > middle).Sum(i => beat[i].g - opposite[i].g);
                    if (flat)
                    {
                        Assert.That(left, Is.Positive);
                        Assert.That(right, Is.Positive);
                        Set(maximum, "Value", 150f);
                        var boosted = Frame(1f);
                        Assert.That(mask.Count(i => boosted[i].g > beat[i].g + 3), Is.GreaterThan(12), "150% exceeds authored peak");
                        Set(maximum, "Value", 100f);
                    }
                    else
                    {
                        Assert.That(Math.Sign(left) * Math.Sign(right), Is.EqualTo(-1), "Opposite bell phases");
                        if (lefty) Assert.That(Math.Sign(left), Is.EqualTo(-Math.Sign(previousLeft)), "Mirrored lead");
                        previousLeft = left;
                    }
                }
            }
            finally
            {
                Shader.SetGlobalVector(gains, oldGains);
                Shader.SetGlobalFloat(clock, oldClock);
            }
        }

        [UnityTest]
        public IEnumerator NeutralOpenHatPulseReachesTheProjectedWell([ValueSource(nameof(HatPreviewLanes))] int laneCount) =>
            CheckOpenHatGlow("OpenHiHat", "EliteOpenHatNeutralGlow", laneCount);

        [UnityTest]
        public IEnumerator GhostOpenHatPulseReachesTheProjectedWell([ValueSource(nameof(HatPreviewLanes))] int laneCount) =>
            CheckOpenHatGlow("OpenHiHatGhost", "EliteOpenHatGhostGlow", laneCount);

        [UnityTest]
        public IEnumerator AccentOpenHatPulseReachesTheProjectedWell([ValueSource(nameof(HatPreviewLanes))] int laneCount) =>
            CheckOpenHatGlow("OpenHiHatAccent", "EliteOpenHatAccentGlow", laneCount);

        private IEnumerator CheckOpenHatGlow(string noteType, string peakSetting, int laneCount)
        {
            yield return BuildElitePreview(true, laneCount);
            string strength = noteType == "OpenHiHatGhost" ? "Ghost" : noteType == "OpenHiHatAccent" ? "Accent" : "Normal";
            SpotlightOpenHat(strength, false);
            Call(_player, "Advance", .35d);
            var pool = Field(_player, "_notePool");
            var spawned = (IList)Get(pool, "AllSpawned");
            var note = (Component)spawned[spawned.Count - 1];
            var noteRef = Get(note, "NoteRef");
            noteRef.GetType().GetField("Time").SetValue(noteRef, (double)Get(_player, "PreviewTime") + .2d);
            Call(note, "RefreshPosition");
            var group = note.GetComponentInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"));
            Assert.That(group.GetComponentsInChildren<MeshRenderer>().Any(r => r.name == "Well pulse"), Is.True);

            var targets = Production("YARG.Helpers.CameraPreviewTexture");
            _captureTarget = (RenderTexture)CallStatic(targets, "CreateOwnedPreviewTexture", 1152, 960, "Open hat highway pulse");
            CallStatic(targets, "SetOwnedPreviewTargets", _world.transform, _captureTarget);
            var highway = _world.GetComponentInChildren(Production("YARG.Gameplay.Visuals.HighwayCameraRendering"));
            // A projected offset separates the player's well from its ordinary Unity camera position.
            Call(highway, "SetHorizontalOffsetPx", 96f);
            var camera = highway.GetComponent<Camera>();
            yield return WaitForRenderedFrame();

            Color32[] Capture(float phase = 1f)
            {
                Shader.SetGlobalFloat("_YargFlamGlowPhase", phase);
                camera.Render();
                return ReadFrame(_captureTarget);
            }

            int phaseId = Shader.PropertyToID("_YargFlamGlowPhase");
            float savedPhase = Shader.GetGlobalFloat(phaseId);
            const string GAINS = "_YargOpenHatGlowAdjustments";
            const string SHADOW_GAINS = "_YargOpenHatShadowAdjustments";
            var savedGains = Shader.GetGlobalVector(GAINS);
            var savedShadowGains = Shader.GetGlobalVector(SHADOW_GAINS);
            var settings = Activator.CreateInstance(Production("YARG.Settings.SettingsManager")
                .GetNestedType("SettingContainer", BindingFlags.Public));
            void Tune(string name, float value)
            {
                var slider = Get(settings, name);
                Call(slider, "SetValueWithoutNotify", value);
                Call(slider, "ForceInvokeCallback");
            }
            try
            {
                Call(Get(settings, peakSetting), "ForceInvokeCallback");
                Call(Get(settings, "EliteOpenHatGlowMinimum"), "ForceInvokeCallback");
                Call(Get(settings, "EliteOpenHatShadowMinimum"), "ForceInvokeCallback");
                Call(Get(settings, "EliteOpenHatShadowPeak"), "ForceInvokeCallback");
                float defaultPeak = (float)Get(Get(settings, peakSetting), "Value");
                group.gameObject.SetActive(false);
                var background = Capture();
                group.gameObject.SetActive(true);
                Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.Static, 1f, false, false);
                var bright = Capture();
                // Locate the whole well even where the authored Ghost is barely brighter than the highway.
                Tune(peakSetting, 150f);
                var lit = Capture();
                Tune(peakSetting, defaultPeak);
                // Restrict comparisons to the projected note so a misplaced overlay cannot pass.
                var notePixels = Enumerable.Range(0, bright.Length)
                    .Where(i => lit[i].g - background[i].g > 30).ToArray();
                Assert.That(notePixels.Length, Is.GreaterThan(20), noteType);
                int Dimmed(Color32[] peak, Color32[] low) =>
                    notePixels.Count(i => peak[i].g - low[i].g > 15);

                if (noteType == "OpenHiHatGhost")
                {
                    Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.Radial, 1f, false, false);
                    int BrightnessSwing()
                    {
                        var peak = Capture(1f);
                        var low = Capture(1.5f);
                        return notePixels.Sum(i => Math.Max(0, peak[i].g - low[i].g));
                    }
                    int originalSwing = BrightnessSwing();
                    Tune(peakSetting, 50f);
                    int strongerSwing = BrightnessSwing();
                    Assert.That(strongerSwing, Is.GreaterThan(originalSwing));
                    Tune(peakSetting, defaultPeak);
                }
                foreach (var mode in new[] { DrumOpenHatGlow.Radial, DrumOpenHatGlow.WholeSurface, DrumOpenHatGlow.PulsingShadow })
                {
                    foreach (float rate in new[] { 0.25f, 0.5f, 1f, 2f, 4f })
                    {
                        Call(group, "ConfigureOpenHatGlow", mode, rate, false, false);
                        var peak = Capture(1f);
                        var low = Capture(1f + 0.5f / rate);
                        var nextPeak = Capture(1f + 1f / rate);
                        int dimmed = Dimmed(peak, low);
                        Debug.Log($"{noteType} {mode} rate={rate}: {dimmed} note pixels dimmed.");
                        Assert.That(dimmed, Is.GreaterThan(8), $"{noteType} {mode} rate={rate}");
                        Assert.That(Dimmed(peak, nextPeak), Is.Zero, "The next peak must recover.");
                    }
                }

                // The two sides trade brightness. Lefty traverses the same highway pixels in reverse.
                Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.CircularSweep, 1f, false, false);
                var clockwise = Capture(1.25f);
                var opposite = Capture(1.75f);
                Assert.That(Dimmed(clockwise, opposite), Is.GreaterThan(3), noteType + " first bright lobe");
                Assert.That(Dimmed(opposite, clockwise), Is.GreaterThan(3), noteType + " opposite bright lobe");
                Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.CircularSweep, 1f, false, true);
                var lefty = Capture(1.25f);
                Assert.That(Dimmed(opposite, lefty) + Dimmed(lefty, opposite), Is.Zero, noteType + " lefty sweep");
                Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.CircularSweep, 2f, false, false);
                var eighth = Capture(1.125f);
                Assert.That(Dimmed(clockwise, eighth) + Dimmed(eighth, clockwise), Is.Zero, noteType + " eighth-note sweep");

                Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.StaticShadow, 1f, false, false);
                var shadow = Capture(1f);
                var steady = Capture(1.5f);
                Assert.That(Dimmed(bright, shadow), Is.GreaterThan(3), noteType + " shaded basin");
                Assert.That(Dimmed(shadow, steady) + Dimmed(steady, shadow), Is.Zero);
                Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.PulsingShadow, 1f, false, false);
                var shadowPeak = Capture(1f);
                var shadowLow = Capture(1.5f);
                // The Ghost shadow changes from 8% (static) to 4% (pulsing). Measure the
                // combined change over the well, including pixels below the 15-level pulse cutoff.
                Assert.That(notePixels.Sum(i => shadow[i].g - shadowPeak[i].g), Is.GreaterThan(notePixels.Length),
                    noteType + " deeper shadow at the default pulse peak");
                Assert.That(Dimmed(shadowPeak, shadowLow), Is.GreaterThan(8), noteType + " shadow pulse");

                Tune("EliteOpenHatShadowPeak", 100f);
                var fullyLit = Capture(1f);
                Assert.That(Dimmed(bright, fullyLit) + Dimmed(fullyLit, bright), Is.Zero,
                    "At 100% the shaded peak matches the exposed peak.");
                Tune("EliteOpenHatShadowMinimum", 5f);
                var raisedTrough = Capture(1.5f);
                Tune("EliteOpenHatShadowMinimum", 0f);
                var darkTrough = Capture(1.5f);
                Assert.That(Dimmed(raisedTrough, darkTrough), Is.GreaterThan(3), noteType + " live shadow minimum");
                Assert.That(Dimmed(fullyLit, Capture(1f)), Is.Zero, "The minimum must not reduce the peak.");
                Tune("EliteOpenHatShadowPeak", 20f);
                var staticDepthPeak = Capture(1f);
                Assert.That(Dimmed(fullyLit, staticDepthPeak), Is.GreaterThan(3), noteType + " live shadow peak");
                Assert.That(Dimmed(staticDepthPeak, shadow) + Dimmed(shadow, staticDepthPeak), Is.Zero,
                    "Matching the static shadow's depth must reproduce its mask and exposed crescent.");
                var sameTrough = Capture(1.5f);
                Assert.That(Dimmed(darkTrough, sameTrough) + Dimmed(sameTrough, darkTrough), Is.Zero,
                    "The shadow peak must not move a lower shadow minimum.");

                // Move the sliders on an already visible note, including in Static mode.
                Tune(peakSetting, defaultPeak);
                Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.Static, 1f, false, false);
                Assert.That(Dimmed(bright, Capture(1.5f)), Is.Zero);
                Tune(peakSetting, 0f);
                var off = Capture();
                Tune(peakSetting, 100f);
                var raised = Capture();
                Assert.That(Dimmed(bright, off), Is.GreaterThan(8), "Zero peak must dim the exposed well.");
                Assert.That(Dimmed(raised, bright), Is.GreaterThan(8), "Raising this role's peak must brighten it.");

                Call(group, "ConfigureOpenHatGlow", DrumOpenHatGlow.WholeSurface, 1f, false, false);
                Tune("EliteOpenHatGlowMinimum", 0f);
                Assert.That(Dimmed(raised, Capture(1.5f)), Is.GreaterThan(8));
                Tune("EliteOpenHatGlowMinimum", 200f);
                var capped = Capture(1.5f);
                Assert.That(Dimmed(raised, capped) + Dimmed(capped, raised), Is.Zero,
                    "A minimum above this role's peak must hold it steady.");
            }
            finally
            {
                Shader.SetGlobalFloat(phaseId, savedPhase);
                Shader.SetGlobalVector(GAINS, savedGains);
                Shader.SetGlobalVector(SHADOW_GAINS, savedShadowGains);
            }
        }

        [UnityTest]
        public IEnumerator ActualEliteAccentModelsRenderAndCaptureAtTenLanePreviewScale()
        {
            yield return BuildElitePreview(false, 10);
            var themeNoteType = Production("YARG.Themes.ThemeNoteType");
            var noteGroupType = Production("YARG.Gameplay.Visuals.NoteGroup");
            var fakeNoteType = Production("YARG.Settings.Preview.FakeNote");
            var cases = new[]
            {
                (Name: "normal-regular", Type: "Normal", StarPower: false),
                (Name: "normal-starpower", Type: "Normal", StarPower: true),
                (Name: "accent-regular", Type: "CymbalAccent", StarPower: false),
                (Name: "accent-starpower", Type: "CymbalAccent", StarPower: true),
                (Name: "ghost-regular", Type: "CymbalGhost", StarPower: false),
                (Name: "ghost-starpower", Type: "CymbalGhost", StarPower: true),
                (Name: "ClosedHiHatGhost-regular", Type: "ClosedHiHatGhost", StarPower: false),
                (Name: "ClosedHiHatGhost-starpower", Type: "ClosedHiHatGhost", StarPower: true),
                (Name: "ClosedHiHat-regular", Type: "ClosedHiHat", StarPower: false),
                (Name: "ClosedHiHat-starpower", Type: "ClosedHiHat", StarPower: true),
                (Name: "ClosedHiHatAccent-regular", Type: "ClosedHiHatAccent", StarPower: false),
                (Name: "ClosedHiHatAccent-starpower", Type: "ClosedHiHatAccent", StarPower: true),
                (Name: "OpenHiHatGhost-regular", Type: "OpenHiHatGhost", StarPower: false),
                (Name: "OpenHiHatGhost-starpower", Type: "OpenHiHatGhost", StarPower: true),
                (Name: "OpenHiHat-regular", Type: "OpenHiHat", StarPower: false),
                (Name: "OpenHiHat-starpower", Type: "OpenHiHat", StarPower: true),
                (Name: "OpenHiHatAccent-regular", Type: "OpenHiHatAccent", StarPower: false),
                (Name: "OpenHiHatAccent-starpower", Type: "OpenHiHatAccent", StarPower: true),
            };
            foreach (var testCase in cases)
            {
                var noteType = Enum.Parse(themeNoteType, testCase.Type);
                var pool = Field(_player, "_notePool");
                Call(pool, "ReturnAllObjects");
                Call(_builder, "SpotlightNoteType", noteType, (object)testCase.StarPower);
                Call(_player, "Advance", 0.35d);

                var spawned = (IList)pool.GetType().GetProperty("AllSpawned").GetValue(pool);
                Assert.That(spawned.Count, Is.GreaterThan(0), $"{testCase.Name}: Advance must spawn a real preview note.");
                var note = (Component)spawned[spawned.Count - 1];
                Assert.That(fakeNoteType.IsInstanceOfType(note), Is.True);
                Assert.That(note.gameObject.activeInHierarchy, Is.True);
                var noteRef = Get(note, "NoteRef");
                noteRef.GetType().GetField("Time").SetValue(noteRef,
                    Convert.ToDouble(Get(_player, "PreviewTime")) + 0.2d);
                Call(note, "RefreshPosition");
                Assert.That(Get(noteRef, "NoteType"), Is.EqualTo(noteType),
                    $"{testCase.Name}: the spotlight request must reach the spawned FakeNote.");
                var activeRenderers = note.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
                Assert.That(activeRenderers, Is.Not.Empty, $"{testCase.Name}: selected authored group must render.");

                var groups = note.GetComponentsInChildren(noteGroupType, true).Cast<Component>().ToArray();
                var activeGroups = groups.Where(group => group.gameObject.activeInHierarchy).ToArray();
                Assert.That(activeGroups.Length, Is.EqualTo(1), $"{testCase.Name}: no legacy fallback group may remain active.");
                var activeGroup = activeGroups.Single();
                var themeNote = (Component)Field(activeGroup, "_themeNote");
                Assert.That(themeNote, Is.Not.Null, $"{testCase.Name}: active group must retain its authored ThemeNote bindings.");
                if (testCase.Type == "CymbalAccent")
                {
                    var forceStarPower = (bool?)Get(noteRef, "ForceStarPower");
                    var useStarPower = forceStarPower ?? (bool)Get(_player, "ForceStarPowerNotes");
                    var expectedGroup = (Component)note.GetType()
                        .GetMethod("FindNoteGroup", INSTANCE)
                        .Invoke(note, new[] { noteType, (object)useStarPower });
                    Assert.That(activeGroup, Is.SameAs(expectedGroup),
                        $"{testCase.Name}: the active group must match production FindNoteGroup resolution, including authored star-power fallback.");
                    Assert.That(Get(themeNote, "NoteType").ToString(), Is.EqualTo("CymbalAccent"),
                        $"{testCase.Name}: the spotlight must select authored CymbalAccent, not legacy Cymbal fallback.");
                    var noteGroups = (System.Collections.IEnumerable)Field(note, "_noteGroups");
                    object activePair = null;
                    foreach (var pair in noteGroups)
                    {
                        if (ReferenceEquals(pair.GetType().GetField("Group", INSTANCE).GetValue(pair), activeGroup))
                        {
                            activePair = pair;
                            break;
                        }
                    }
                    Assert.That(activePair, Is.Not.Null,
                        $"{testCase.Name}: the active group must come from the production group table.");
                    Assert.That((bool)Get(themeNote, "StarPowerVariant"),
                        Is.EqualTo((bool)activePair.GetType().GetField("StarPower", INSTANCE).GetValue(activePair)),
                        $"{testCase.Name}: authored variant metadata must match the selected group entry (fallback to regular is expected when no SP variant is authored).");
                    Assert.That(((IEnumerable)Get(themeNote, "ColoredMaterials")).Cast<object>().Any(), Is.True,
                        "Accent color bindings must exist at runtime.");
                    Assert.That(((IEnumerable)Get(themeNote, "ColoredMetalMaterials")).Cast<object>().Any(), Is.True,
                        "Accent metal-color bindings must exist at runtime.");
                }

                var bounds = activeRenderers.Select(renderer => renderer.bounds).Aggregate(BoundsUnion);
                Assert.That(bounds.size.x, Is.GreaterThan(0.01f), $"{testCase.Name}: model bounds must be non-empty.");
                Assert.That(bounds.size.y, Is.GreaterThan(0.01f), $"{testCase.Name}: model bounds must be non-empty.");
                var camera = _world.GetComponentInChildren<Camera>(true);
                Assert.That(camera, Is.Not.Null, "The actual preview prefab must provide its known camera.");
                if (_captureTarget == null)
                {
                    _captureTarget = (RenderTexture)CallStatic(Production("YARG.Helpers.CameraPreviewTexture"),
                        "CreateOwnedPreviewTexture", 1024, 1024, "Elite cymbal accent runtime capture");
                }
                // Rebind before every case: the owned target must reach HighwayCameraRendering through its
                // preview-target API. Assigning camera.targetTexture directly leaves the highway renderer's
                // render-graph resources screen-sized, so the CalcFadeAlphaMask pass aborts the camera render
                // with a fragment-dimension mismatch and the capture stays fully transparent.
                CallStatic(Production("YARG.Helpers.CameraPreviewTexture"), "SetOwnedPreviewTargets",
                    _world.transform, _captureTarget);
                Assert.That(_captureTarget.IsCreated(), Is.True,
                    "The owned HDR preview texture must be allocated before capture.");
                Assert.That(camera.targetTexture, Is.SameAs(_captureTarget),
                    $"{testCase.Name}: preview camera must render into the owned HDR texture.");
                camera.aspect = 1f;
                camera.pixelRect = new Rect(0, 0, _captureTarget.width, _captureTarget.height);
                var viewport = camera.WorldToViewportPoint(bounds.center);
                Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane), $"{testCase.Name}: model center is in front of preview camera.");
                foreach (float x in new[] { bounds.min.x, bounds.max.x })
                foreach (float y in new[] { bounds.min.y, bounds.max.y })
                foreach (float z in new[] { bounds.min.z, bounds.max.z })
                {
                    var corner = camera.WorldToViewportPoint(new Vector3(x, y, z));
                    Assert.That(corner.z, Is.InRange(camera.nearClipPlane, camera.farClipPlane),
                        $"{testCase.Name}: whole model bounds must fit camera depth.");
                    Assert.That(corner.x, Is.InRange(0f, 1f), $"{testCase.Name}: whole model bounds must fit camera width.");
                    Assert.That(corner.y, Is.InRange(0f, 1f), $"{testCase.Name}: whole model bounds must fit camera height.");
                }

                foreach (var renderer in activeRenderers)
                foreach (var material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null, $"{testCase.Name}: {renderer.name} has a null runtime material slot.");
                    Assert.That(material.shader == null || !material.shader.name.Contains("LegacyLit"), Is.True,
                        $"{testCase.Name}: {renderer.name} slot {material.name} unexpectedly uses LegacyLit.");
                }

                yield return WaitForRenderedFrame();
                var pixels = ReadFrame(_captureTarget);
                AssertNonEmptyCapture(pixels, testCase.Name);
                var path = $"/tmp/yarg-elite-cymbal-accent-{testCase.Name}.png";
                WriteFramePng(pixels, _captureTarget.width, _captureTarget.height, path);
                Debug.Log($"[Elite cymbal accent runtime capture] case={testCase.Name}; image={path}; " +
                    $"camera={camera.name}; viewportCenter={viewport}; worldBoundsCenter={bounds.center}; " +
                    $"worldBoundsSize={bounds.size}; activeGroup={activeGroup.name}; themeNote={Get(themeNote, "NoteType")}; " +
                    $"starPower={Get(themeNote, "StarPowerVariant")}; colorBindings={DescribeBindings(Get(themeNote, "ColoredMaterials"))}; " +
                    $"metalBindings={DescribeBindings(Get(themeNote, "ColoredMetalMaterials"))}; materials={DescribeRuntimeMaterials(activeRenderers)}");
            }
            yield return null;
        }

        private static string DescribeBindings(object bindings)
        {
            return string.Join(" | ", ((IEnumerable)bindings).Cast<object>().Select(binding =>
            {
                var renderer = (Renderer)Field(binding, "Mesh");
                int index = (int)Field(binding, "MaterialIndex");
                var material = renderer.materials[index];
                return $"{renderer.name}[{index}]={material.name}:{material.shader?.name}:color={material.color}:" +
                    $"emissionMultiplier={Field(binding, "EmissionMultiplier")}:emissionAddition={Field(binding, "EmissionAddition")}";
            }));
        }

        private static string DescribeRuntimeMaterials(System.Collections.Generic.IEnumerable<Renderer> renderers)
        {
            return string.Join(" | ", renderers.SelectMany(renderer => renderer.materials).Select(material =>
                $"{material.name}:{material.shader?.name}:color={material.color}:metallic=" +
                (material.HasProperty("_Metallic") ? material.GetFloat("_Metallic").ToString("R") : "<not-supported>")));
        }

        private static Bounds BoundsUnion(Bounds left, Bounds right)
        {
            left.Encapsulate(right);
            return left;
        }

        private static Color32[] ReadFrame(RenderTexture source)
        {
            var previous = RenderTexture.active;
            var displayTarget = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var staging = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                // Sample the HDR source through a normal GPU blit into an sRGB display target.
                // This follows the same linear-to-display transfer used by the RawImage shader,
                // instead of quantizing half-float linear values directly into 8-bit channels.
                Graphics.Blit(source, displayTarget);
                RenderTexture.active = displayTarget;
                staging.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                staging.Apply(false, false);
                return staging.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(displayTarget);
                UnityEngine.Object.Destroy(staging);
            }
        }

        private static void AssertNonEmptyCapture(Color32[] pixels, string label)
        {
            int opaque = pixels.Count(pixel => pixel.a > 8);
            Assert.That(opaque, Is.GreaterThan(0),
                $"{label}: capture must contain non-transparent pixels; a fully transparent image means " +
                "the camera render never reached the owned preview texture.");
            int rendered = pixels.Count(pixel => pixel.a > 8 && (pixel.r > 10 || pixel.g > 10 || pixel.b > 10));
            Assert.That(rendered, Is.GreaterThan(60),
                $"{label}: preview image must contain rendered pixels (count={rendered}).");
        }

        private static void WriteFramePng(Color32[] pixels, int width, int height, string path)
        {
            var capture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                capture.SetPixels32(pixels);
                capture.Apply(false, false);
                File.WriteAllBytes(path, capture.EncodeToPNG());
                Debug.Log($"[Elite cymbal accent runtime capture] wrote {path} ({width}x{height}, sRGB display values)");
            }
            finally
            {
                UnityEngine.Object.Destroy(capture);
            }
        }

        private static EliteDrumsLayoutPreset TenLaneLayout()
        {
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Cymbal accent render ten lane");
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            return layout;
        }
    }
}
