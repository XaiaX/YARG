using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Chart;
using YARG.Tests.PlayMode;
using static YARG.Core.Chart.EliteDrumNote;
using Object = UnityEngine.Object;

namespace YARG.Tests.PlayMode
{
    public sealed class ElitePedalPreviewParityPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const float TOLERANCE = 0.0001f;
        private readonly List<Object> _owned = new();
        private Component _themeManager;
        private bool _ownsThemeManager;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _capturedSettings;
        private bool _ownsAudio;
        private InitializedElitePlayerFixture _fixture;

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        private static object Get(object target, string name)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, INSTANCE | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property.GetValue(target);
                var field = type.GetField(name, INSTANCE | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(target);
            }
            throw new MissingMemberException(target.GetType().FullName, name);
        }

        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethods(INSTANCE).Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(target, args);

        [UnitySetUp]
        public IEnumerator SetUpProductionTheme()
        {
            var settingsManager = Production("YARG.Settings.SettingsManager");
            var container = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
            var initialized = container.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            _originalSettings = settingsManager.GetProperty("Settings").GetValue(null);
            _originalSettingsInitialized = (bool)initialized.GetValue(null);
            _capturedSettings = true;

            var audioHandler = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audioHandler.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null)
            {
                Production("YARG.Helpers.PathHelper").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                var bass = Production("YARG.Audio.BASS.BassAudioManager");
                audioHandler.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(bass).Invoke(null, null);
                _ownsAudio = true;
            }
            if (_originalSettings == null)
            {
                var temporary = Activator.CreateInstance(container);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { temporary });
                initialized.SetValue(null, true);
            }

            var managerType = Production("YARG.Themes.ThemeManager");
            var managerProperty = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            _themeManager = managerProperty.GetValue(null) as Component;
            if (_themeManager == null)
            {
                _themeManager = Own(new GameObject("Elite pedal parity ThemeManager").AddComponent(managerType));
                _ownsThemeManager = true;
            }
            yield return null;
            Assert.That(managerProperty.GetValue(null), Is.SameAs(_themeManager));
            var preset = Production("YARG.Themes.ThemePreset").GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            Assert.That(_themeManager.GetType().GetMethod("GetThemeContainer", INSTANCE)
                .Invoke(_themeManager, new[] { preset, style }), Is.Not.Null,
                "The active manager must provide the real default Elite drums theme for FakeTrackPlayer initialization.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_fixture != null)
            {
                yield return _fixture.Dispose();
                _fixture = null;
            }
            for (int i = _owned.Count - 1; i >= 0; i--)
                if (_owned[i] != null) Object.Destroy(_owned[i]);
            _owned.Clear();
            yield return null;
            if (_capturedSettings)
            {
                var settingsManager = Production("YARG.Settings.SettingsManager");
                var container = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { _originalSettings });
                container.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static)
                    .SetValue(null, _originalSettingsInitialized);
                _capturedSettings = false;
            }
            if (_ownsAudio)
            {
                Production("YARG.Core.Audio.GlobalAudioHandler").GetMethod("Close", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                _ownsAudio = false;
            }
            if (_ownsThemeManager)
            {
                _themeManager = null;
                _ownsThemeManager = false;
            }
        }

        [UnityTest]
        public IEnumerator OrdinaryAndGhostSnarePreviewBoundsMatchLiveNativeEliteModels()
        {
            foreach (bool full in new[] { false, true })
            {
                var layout = full ? EliteDrumsLayoutPreset.Full : EliteDrumsLayoutPreset.Compact;
                if (full)
                {
                    layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
                    layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
                }
                var notes = new[] { DrumNoteType.Neutral, DrumNoteType.Ghost }.Select((kind, index) =>
                    new EliteDrumNote(EliteDrumPad.Snare, kind, EliteDrumsHatState.Indifferent,
                        EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
                        EliteDrumsChannelFlag.None, index + 1d, (uint)(480 * (index + 1)), false)).ToArray();
                var chart = PreparedElitePlayerFixture.CreateChart(notes);
                _fixture = new InitializedElitePlayerFixture();
                yield return _fixture.Initialize(chart, Profile(), layout);
                var resolved = (ResolvedDrumPlayback)Get(_fixture.Session, "ResolvedDrumPlayback");
                Assert.That(resolved.SourceFormat, Is.EqualTo(DrumSourceFormat.Elite));
                Assert.That(resolved.RequestedOutput, Is.EqualTo(Instrument.EliteDrums));
                Assert.That(resolved.ScoreCategory, Is.EqualTo(DrumScoreCategory.NativeElite));
                var runtime = _fixture.Player;
                var root = Own(new GameObject("Live geometry comparison preview"));
                root.SetActive(false);
                var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
                var preview = root.AddComponent(playerType);
                playerType.GetProperty("SelectedGameMode", INSTANCE).SetValue(preview, GameMode.EliteDrums);
                playerType.GetProperty("EliteLayoutOverride", INSTANCE).SetValue(preview, layout);
                var poolObject = new GameObject("Geometry preview pool");
                poolObject.transform.SetParent(root.transform, false);
                poolObject.SetActive(false);
                var pool = poolObject.AddComponent(Production("YARG.Gameplay.KeyedPool"));
                pool.GetType().BaseType.GetField("_prewarmAmount", INSTANCE).SetValue(pool, 0);
                pool.GetType().BaseType.GetField("_objectCap", INSTANCE).SetValue(pool, 8);
                poolObject.SetActive(true);
                var presetType = Production("YARG.Themes.ThemePreset");
                var preset = presetType.GetField("Default").GetValue(null);
                var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
                var factory = Delegate.CreateDelegate(typeof(Func<,,>).MakeGenericType(presetType, style.GetType(), typeof(GameObject)),
                    Production("YARG.Settings.Preview.FakeNote").GetMethod("CreateFakeNoteFromTheme"));
                var initializerType = typeof(Action<,,>).MakeGenericType(playerType.GetNestedType("Info"), presetType, style.GetType());
                var initializer = System.Linq.Expressions.Expression.Lambda(initializerType,
                    System.Linq.Expressions.Expression.Empty(), initializerType.GetMethod("Invoke").GetParameters()
                        .Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType))).Compile();
                Call(preview, "Initialize", preset, pool, factory, CameraPreset.Default,
                    new ColorProfile("Live geometry parity"), EnginePreset.Default, HighwayPreset.Default, false, initializer);
                root.SetActive(true);
                yield return null;
                ((Behaviour)preview).enabled = false;
                for (int index = 0; index < notes.Length; index++)
                {
                    Call(runtime, "SpawnNote", notes[index]);
                    var live = runtime.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true)
                        .Single(element => ReferenceEquals(Get(element, "NoteRef"), notes[index]));
                    var generator = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
                    var modelType = Production("YARG.Themes.ThemeNoteType");
                    var descriptor = generator.GetMethod("Describe").Invoke(null, new object[]
                    {
                        EliteDrumsColorRole.Snare, Enum.Parse(modelType, index == 0 ? "Normal" : "Ghost")
                    });
                    Call(pool, "ReturnAllObjects");
                    Call(preview, "SpotlightEliteRole", EliteDrumsColorRole.Snare, false, descriptor);
                    Call(preview, "Advance", 0.21d);
                    var spawned = (IList)Get(pool, "AllSpawned");
                    Assert.That(spawned.Count, Is.GreaterThan(0));
                    var fake = (Component)spawned[spawned.Count - 1];
                    var expected = HorizontalBounds((Component)Get(live, "NoteGroup"), runtime.transform);
                    var actual = HorizontalBounds((Component)Get(fake, "_currentNoteGroup"), preview.transform);
                    // One thousandth of a highway unit allows renderer float rounding, not lane-scale differences.
                    Assert.That(actual.width, Is.EqualTo(expected.width).Within(0.001f), $"{(full ? 10 : 5)} lanes, snare {index}: width");
                    Assert.That(actual.center, Is.EqualTo(expected.center).Within(0.001f), $"{(full ? 10 : 5)} lanes, snare {index}: center");
                }
                root.SetActive(false);
                yield return _fixture.Dispose();
                _fixture = null;
            }
        }

        [UnityTest]
        public IEnumerator PreviewFlamsDedicatedKicksAndWildcardsUseResolvedThemeGeometry()
        {
            var roleType = Production("YARG.Core.Game.EliteDrumsColorRole");
            var modelType = Production("YARG.Themes.ThemeNoteType");
            var descriptorType = Production("YARG.Settings.Preview.ElitePreviewDescriptor");
            var descriptorConstructor = descriptorType.GetConstructor(new[]
            {
                roleType, typeof(int), modelType, typeof(bool), typeof(float), typeof(float), typeof(string)
            });
            var shortDescriptorConstructor = descriptorType.GetConstructor(new[]
            {
                roleType, typeof(int), modelType, typeof(bool), typeof(float), typeof(float)
            });
            Assert.That(descriptorConstructor, Is.Not.Null);
            var kickRole = Enum.Parse(roleType, "Kick");
            var doubleKickRole = Enum.Parse(roleType, "DoubleKick");
            var snareRole = Enum.Parse(roleType, "Snare");
            var wildcardRole = Enum.Parse(roleType, "Wildcard");
            var dedicatedKickType = Enum.Parse(modelType, "DedicatedLaneKick");
            var kickBarType = Enum.Parse(modelType, "Kick");
            var normalType = Enum.Parse(modelType, "Normal");
            float[] flamSeparations = new float[3];

            for (int layoutIndex = 0; layoutIndex < 3; layoutIndex++)
            {
                bool tenLane = layoutIndex == 1;
                bool combined = layoutIndex == 2;
                var layout = combined ? CombinedKickLayout() : tenLane ? TenLaneLayout() : EliteDrumsLayoutPreset.Compact;
                var (preview, pool, root) = CreateProductionPreview(layout);
                float highwayWidth = Convert.ToSingle(Production("YARG.Gameplay.Player.TrackPlayer")
                    .GetField("TRACK_WIDTH").GetRawConstantValue());

                // Two production FakeNotes use the real theme renderer bounds; their parent X positions
                // are read from the rendered models, not from a geometry-helper formula.
                var flamCenters = new List<float>();
                foreach (float offset in new[] { -0.1333333f, 0.1333333f })
                {
                    Call(pool, "ReturnAllObjects");
                    var flamDescriptor = shortDescriptorConstructor.Invoke(new object[]
                        { snareRole, 1, normalType, false, 2f / 3f, offset });
                    Call(preview, "SpotlightEliteRole", snareRole, false, flamDescriptor);
                    Call(preview, "Advance", 0.21d);
                    var fake = (Component)((IList)Get(pool, "AllSpawned")).Cast<object>().Last();
                    Assert.That(Get(fake, "NoteRef"), Is.Not.Null);
                    var flamBounds = HorizontalBounds((Component)Get(fake, "_currentNoteGroup"), preview.transform);
                    flamCenters.Add(flamBounds.center);
                }
                flamSeparations[layoutIndex] = Mathf.Abs(flamCenters[1] - flamCenters[0]);
                Assert.That(flamSeparations[layoutIndex], Is.GreaterThan(0f), "The actual split preview models must be separated.");

                // Both generated kick descriptors must fit split or combined dedicated lanes.
                if (tenLane || combined)
                {
                    var resolvedLayout = Production("YARG.Core.Game.EliteDrumsLayoutResolver").GetMethod("PreviewResolve")
                        .Invoke(null, new object[] { layout });
                    var sourceMap = Get(resolvedLayout, "SourceToLane");
                    var sourceMapIndexer = sourceMap.GetType().GetProperty("Item");
                    float laneWidth = Convert.ToSingle(resolvedLayout.GetType().GetMethod("GetLaneWidth")
                        .Invoke(resolvedLayout, new object[] { highwayWidth }));
                    foreach (var (role, source) in new[] { (kickRole, combined ? "kick" : "kick1"), (doubleKickRole, combined ? "kick" : "kick2") })
                {
                    Call(pool, "ReturnAllObjects");
                    var dedicatedDescriptor = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator")
                        .GetMethod("Describe").Invoke(null, new[] { role, kickBarType });
                    Call(preview, "SpotlightEliteRole", role, false, dedicatedDescriptor);
                    Call(preview, "Advance", 0.21d);
                    var dedicatedFake = (Component)((IList)Get(pool, "AllSpawned")).Cast<object>().Last();
                    var dedicatedGroup = (Component)Get(dedicatedFake, "_currentNoteGroup");
                    Assert.That(dedicatedGroup, Is.SameAs(Call(dedicatedFake, "FindNoteGroup", dedicatedKickType, false)),
                        "The real selected theme group must be the dedicated kick variant.");
                    int kickLane = (int)sourceMapIndexer.GetValue(sourceMap, new object[] { source });
                    float expectedKickCenter = Convert.ToSingle(resolvedLayout.GetType().GetMethod("GetLaneX")
                        .Invoke(resolvedLayout, new object[] { kickLane, highwayWidth, false }));
                    var kickBounds = HorizontalBounds(dedicatedGroup, preview.transform);
                    Assert.That(kickBounds.center, Is.EqualTo(expectedKickCenter).Within(0.01f),
                        $"{(tenLane ? 10 : 5)}-lane {source} must center on its configured lane.");
                    Assert.That(kickBounds.width, Is.LessThanOrEqualTo(laneWidth + 0.01f),
                        $"{(tenLane ? 10 : 5)}-lane {source} must fit within its lane.");
                    foreach (bool starPower in new[] { false, true, false })
                    {
                        var noteRef = Get(dedicatedFake, "NoteRef");
                        noteRef.GetType().GetField("ForceStarPower").SetValue(noteRef, starPower);
                        dedicatedFake.transform.localPosition += Vector3.right * 1.2f;
                        Call(dedicatedFake, "OnSettingChanged");
                        var selected = (Component)Get(dedicatedFake, "_currentNoteGroup");
                        Assert.That(selected, Is.SameAs(Call(dedicatedFake, "FindNoteGroup", dedicatedKickType, starPower)));
                        var refreshed = HorizontalBounds(selected, preview.transform);
                        Assert.That(refreshed.center, Is.EqualTo(expectedKickCenter).Within(0.01f), $"{source} refresh/SP={starPower} center");
                        Assert.That(refreshed.width, Is.EqualTo(laneWidth).Within(0.01f), $"{source} refresh/SP={starPower} width");
                    }
                    Assert.That(Get(dedicatedFake, "_currentNoteGroup"), Is.SameAs(dedicatedGroup),
                        "Settings refresh must retain the dedicated kick model selection.");
                    Call(pool, "ReturnAllObjects");
                    Call(preview, "SpotlightEliteRole", role, false, dedicatedDescriptor);
                    Call(preview, "Advance", 0.21d);
                    var reusedKick = (Component)((IList)Get(pool, "AllSpawned"))[0];
                    Assert.That(reusedKick, Is.SameAs(dedicatedFake), "The production pool must reuse the kick FakeNote.");
                    Assert.That(Get(reusedKick, "_currentNoteGroup"), Is.SameAs(dedicatedGroup));
                    var reusedBounds = HorizontalBounds(dedicatedGroup, preview.transform);
                    Assert.That(reusedBounds.center, Is.EqualTo(expectedKickCenter).Within(0.01f));
                    Assert.That(reusedBounds.width, Is.EqualTo(laneWidth).Within(0.01f));
                    }
                }

                // Explicitly preserve bar mode using the actual Compact layout (no dedicated kick lanes).
                var (barPreview, barPool, barRoot) = CreateProductionPreview(EliteDrumsLayoutPreset.Compact);
                Call(barPool, "ReturnAllObjects");
                var barDescriptor = shortDescriptorConstructor.Invoke(new object[]
                    { kickRole, 0, kickBarType, true, 1f, 0f });
                Call(barPreview, "SpotlightEliteRole", kickRole, false, barDescriptor);
                Call(barPreview, "Advance", 0.21d);
                var barFake = (Component)((IList)Get(barPool, "AllSpawned")).Cast<object>()
                    .Single(note => Equals(Get(Get(note, "NoteRef"), "EliteDescriptor"), barDescriptor));
                var barGroup = (Component)Get(barFake, "_currentNoteGroup");
                Assert.That(barGroup, Is.SameAs(Call(barFake, "FindNoteGroup", kickBarType, false)),
                    "Kick-bar model selection must remain unchanged.");
                var barBounds = HorizontalBounds(barGroup, barPreview.transform);
                Assert.That(barBounds.width, Is.GreaterThan(0f),
                    "Kick-bar geometry must retain the real themed bar renderer.");
                barRoot.SetActive(false);
                Object.Destroy(barRoot);

                // Wildcard geometry is independent of the selected lane count and has no lane assignment.
                Call(pool, "ReturnAllObjects");
                var wildcardDescriptor = shortDescriptorConstructor.Invoke(new object[]
                    { wildcardRole, 0, Enum.Parse(modelType, "Wildcard"), false, 1f, 0f });
                Call(preview, "SpotlightEliteRole", wildcardRole, false, wildcardDescriptor);
                Call(preview, "Advance", 0.21d);
                var wildcardFake = (Component)((IList)Get(pool, "AllSpawned")).Cast<object>().Last();
                var wildcardBounds = HorizontalBounds((Component)Get(wildcardFake, "_currentNoteGroup"), preview.transform);
                Assert.That(wildcardBounds.center, Is.EqualTo(0f).Within(0.01f));
                foreach (bool starPower in new[] { false, true, false })
                {
                    var noteRef = Get(wildcardFake, "NoteRef");
                    noteRef.GetType().GetField("ForceStarPower").SetValue(noteRef, starPower);
                    wildcardFake.transform.localPosition += Vector3.right * 1.2f;
                    Call(wildcardFake, "OnSettingChanged");
                    var selected = (Component)Get(wildcardFake, "_currentNoteGroup");
                    Assert.That(selected, Is.SameAs(Call(wildcardFake, "FindNoteGroup", Enum.Parse(modelType, "Wildcard"), starPower)));
                    var refreshed = HorizontalBounds(selected, preview.transform);
                    Assert.That(refreshed.center, Is.EqualTo(0f).Within(0.01f), $"Wildcard refresh/SP={starPower} center");
                    Assert.That(refreshed.width, Is.EqualTo(highwayWidth).Within(0.02f));
                }
                Call(pool, "ReturnAllObjects");
                Call(preview, "SpotlightEliteRole", wildcardRole, false, wildcardDescriptor);
                Call(preview, "Advance", 0.21d);
                var reusedWildcard = (Component)((IList)Get(pool, "AllSpawned"))[0];
                Assert.That(reusedWildcard, Is.SameAs(wildcardFake));
                var reusedWildcardBounds = HorizontalBounds((Component)Get(reusedWildcard, "_currentNoteGroup"), preview.transform);
                Assert.That(reusedWildcardBounds.center, Is.EqualTo(0f).Within(0.01f));
                Assert.That(reusedWildcardBounds.width, Is.EqualTo(highwayWidth).Within(0.02f));
                Assert.That(wildcardBounds.width, Is.EqualTo(highwayWidth).Within(0.02f),
                    $"Wildcard theme bounds must span the full track at {(tenLane ? 10 : 5)} lanes.");

                root.SetActive(false);
                Object.Destroy(root);
                yield return null;
            }

            Assert.That(flamSeparations[0], Is.EqualTo(flamSeparations[1] * 2f).Within(0.02f),
                "The actual preview flam center separation must scale with five-lane versus ten-lane column width.");
        }

        private (Component preview, Component pool, GameObject root) CreateProductionPreview(EliteDrumsLayoutPreset layout)
        {
            var root = Own(new GameObject("Production Elite geometry preview"));
            root.SetActive(false);
            var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var preview = root.AddComponent(playerType);
            playerType.GetProperty("SelectedGameMode", INSTANCE).SetValue(preview, GameMode.EliteDrums);
            playerType.GetProperty("EliteLayoutOverride", INSTANCE).SetValue(preview, layout);
            var poolObject = new GameObject("Production Elite geometry pool");
            poolObject.transform.SetParent(root.transform, false);
            poolObject.SetActive(false);
            var pool = poolObject.AddComponent(Production("YARG.Gameplay.KeyedPool"));
            pool.GetType().BaseType.GetField("_prewarmAmount", INSTANCE).SetValue(pool, 0);
            pool.GetType().BaseType.GetField("_objectCap", INSTANCE).SetValue(pool, 8);
            poolObject.SetActive(true);
            var presetType = Production("YARG.Themes.ThemePreset");
            var preset = presetType.GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var factory = Delegate.CreateDelegate(typeof(Func<,,>).MakeGenericType(presetType, style.GetType(), typeof(GameObject)),
                Production("YARG.Settings.Preview.FakeNote").GetMethod("CreateFakeNoteFromTheme"));
            var infoType = playerType.GetNestedType("Info");
            var initializerType = typeof(Action<,,>).MakeGenericType(infoType, presetType, style.GetType());
            var initializer = System.Linq.Expressions.Expression.Lambda(initializerType,
                System.Linq.Expressions.Expression.Empty(), initializerType.GetMethod("Invoke").GetParameters()
                    .Select(parameter => System.Linq.Expressions.Expression.Parameter(parameter.ParameterType))).Compile();
            Call(preview, "Initialize", preset, pool, factory, CameraPreset.Default,
                new ColorProfile("Elite geometry regression"), EnginePreset.Default, HighwayPreset.Default, false, initializer);
            root.SetActive(true);
            return (preview, pool, root);
        }

        private static void AssertKickFlamBodyColor(Component group, UnityEngine.Color expected)
        {
            var noteGroupType = Production("YARG.Gameplay.Visuals.NoteGroup");
            var cacheName = "_coloredMaterialCache";
            var cache = (Array)noteGroupType.GetField(cacheName, INSTANCE).GetValue(group);
            Assert.That(cache.Length, Is.GreaterThan(0), "The real theme selected model must expose colored materials.");
            foreach (var info in cache)
            {
                var infoType = info.GetType();
                var material = (Material)infoType.GetField("MaterialCache", INSTANCE).GetValue(info);
                float addition = (float)infoType.GetField("EmissionAddition", INSTANCE).GetValue(info);
                float multiplier = (float)infoType.GetField("EmissionMultiplier", INSTANCE).GetValue(info);
                // NoteGroup preserves each theme material's authored brightness addition on the body.
                var expectedBody = expected + new UnityEngine.Color(addition, addition, addition);
                var actualBody = material.color;
                Assert.That(actualBody.r, Is.EqualTo(expectedBody.r).Within(0.01f));
                Assert.That(actualBody.g, Is.EqualTo(expectedBody.g).Within(0.01f));
                Assert.That(actualBody.b, Is.EqualTo(expectedBody.b).Within(0.01f));
                var paletteColor = actualBody - new UnityEngine.Color(addition, addition, addition);
                Assert.That(paletteColor.r, Is.EqualTo(expected.r).Within(0.01f), "Unadjusted body must retain the KickFlam palette role.");
                Assert.That(paletteColor.g, Is.EqualTo(expected.g).Within(0.01f));
                Assert.That(paletteColor.b, Is.EqualTo(expected.b).Within(0.01f));
                var emission = material.GetColor("_EmissionColor");
                Assert.That(emission.r, Is.EqualTo(expectedBody.r * multiplier).Within(0.01f));
                Assert.That(emission.g, Is.EqualTo(expectedBody.g * multiplier).Within(0.01f));
                Assert.That(emission.b, Is.EqualTo(expectedBody.b * multiplier).Within(0.01f));
            }
        }

        private static (float width, float center) HorizontalBounds(Component group, Transform highway)
        {
            Assert.That(group.gameObject.activeInHierarchy, Is.True, "Measure the selected active model group.");
            var renderers = group.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Assert.That(renderers, Is.Not.Empty);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return (bounds.size.x / Mathf.Abs(highway.lossyScale.x), highway.InverseTransformPoint(bounds.center).x);
        }

        [UnityTest]
        public IEnumerator KickFlamPreviewUsesRealThemeModelLaneBoundsColorsAndPoolRefreshForEachKickLayout()
        {
            var roleType = Production("YARG.Core.Game.EliteDrumsColorRole");
            var modelType = Production("YARG.Themes.ThemeNoteType");
            var descriptorType = Production("YARG.Settings.Preview.ElitePreviewDescriptor");
            var generatorType = Production("YARG.Settings.Preview.EliteDrumsFakeNoteGenerator");
            var describe = generatorType.GetMethod("DescribeForKickLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(describe, Is.Not.Null);
            var role = Enum.Parse(roleType, "KickFlam");
            var normal = Enum.Parse(modelType, "Normal");
            var descriptorProperty = descriptorType.GetProperty("DedicatedLaneSource");
            var scenarios = new[]
            {
                (Layout: TenLaneLayout(), Mode: "Split", Sources: new[] { "kick1", "kick2" }, Model: "DedicatedLaneKick"),
                (Layout: CombinedKickLayout(), Mode: "Combined", Sources: new[] { "kick" }, Model: "Accent"),
                (Layout: EliteDrumsLayoutPreset.Compact, Mode: "Bars", Sources: new string[] { null, null }, Model: "Kick")
            };
            float highwayWidth = Convert.ToSingle(Production("YARG.Gameplay.Player.TrackPlayer")
                .GetField("TRACK_WIDTH").GetRawConstantValue());

            foreach (var scenario in scenarios)
            {
                var (preview, pool, root) = CreateProductionPreview(scenario.Layout);
                var previewProfile = new ColorProfile($"Kick flam {scenario.Mode} preview");
                previewProfile.EliteDrums.KickNote = System.Drawing.Color.Coral;
                previewProfile.EliteDrums.KickFlamNote = System.Drawing.Color.FromArgb(255, 75, 0, 130);
                previewProfile.EliteDrums.KickFlamStarpower = System.Drawing.Color.FromArgb(255, 0, 255, 255);
                preview.GetType().GetProperty("ColorProfileOverride", INSTANCE).SetValue(preview, previewProfile);
                Call(preview, "RefreshColors", previewProfile);
                var resolved = Production("YARG.Core.Game.EliteDrumsLayoutResolver").GetMethod("PreviewResolve")
                    .Invoke(null, new object[] { scenario.Layout });
                var sourceMap = Get(resolved, "SourceToLane");
                var sourceMapIndexer = sourceMap.GetType().GetProperty("Item");
                float laneWidth = Convert.ToSingle(resolved.GetType().GetMethod("GetLaneWidth")
                    .Invoke(resolved, new object[] { highwayWidth }));
                var modeType = Production("YARG.Core.Game.EliteDrumsLayoutKickMode");
                var mode = Enum.Parse(modeType, scenario.Mode);
                var descriptors = new List<object>();
                var primaryDescriptor = describe.Invoke(null, new object[] { role, normal, mode });
                descriptors.Add(primaryDescriptor);
                if (scenario.Sources.Length > 1)
                {
                    var generator = Activator.CreateInstance(generatorType, new[] { mode });
                    var createRoleNote = generatorType.GetMethod("CreateRoleNote", BindingFlags.Public | BindingFlags.Static);
                    var primaryNote = createRoleNote.Invoke(null, new[] { (object)0d, primaryDescriptor });
                    var companion = ((IEnumerable)generatorType.GetMethod("CreateChordNotes")
                        .Invoke(generator, new[] { (object)0d, primaryNote, null })).Cast<object>().Single();
                    object companionDescriptor = companion.GetType().GetField("EliteDescriptor").GetValue(companion);
                    Assert.That(companionDescriptor, Is.Not.Null);
                    Assert.That(companionDescriptor.GetType(), Is.EqualTo(descriptorType),
                        "A populated nullable descriptor boxes as its underlying struct.");
                    descriptors.Add(companionDescriptor);
                }

                string DescribeDescriptor(object value) => value == null ? "<null>" : string.Join(", ",
                    descriptorType.GetProperties().Select(property => $"{property.Name}={property.GetValue(value) ?? "<null>"}"));
                Component SelectMember(object expected, string phase)
                {
                    var spawned = ((IList)Get(pool, "AllSpawned")).Cast<object>().ToArray();
                    var matches = spawned.Where(note => Equals(Get(Get(note, "NoteRef"), "EliteDescriptor"), expected)).ToArray();
                    Assert.That(matches.Length, Is.EqualTo(1),
                        $"{scenario.Mode}/{phase}: expected exactly one [{DescribeDescriptor(expected)}]; actual: " +
                        string.Join("; ", spawned.Select(note => DescribeDescriptor(Get(Get(note, "NoteRef"), "EliteDescriptor")))));
                    return (Component)matches[0];
                }
                void SpawnMembers()
                {
                    // Spotlight regenerates the primary and intentionally suppresses chord extras.
                    Call(preview, "SpotlightEliteRole", role, false, primaryDescriptor);
                    Call(preview, "Advance", 0.21d);
                    if (descriptors.Count > 1)
                    {
                        var primary = SelectMember(primaryDescriptor, "spotlight primary");
                        var primaryData = Get(primary, "NoteRef");
                        var generator = Activator.CreateInstance(generatorType, new[] { mode });
                        var companions = (IEnumerable)generatorType.GetMethod("CreateChordNotes").Invoke(generator,
                            new[] { Get(primaryData, "Time"), primaryData, null });
                        foreach (var companion in companions)
                        {
                            companion.GetType().GetField("ForceStarPower").SetValue(companion, false);
                            Call(preview, "SpawnNote", companion);
                        }
                    }
                }

                for (int index = 0; index < descriptors.Count; index++)
                {
                    Call(pool, "ReturnAllObjects");
                    var descriptor = descriptors[index];
                    SpawnMembers();
                    var fake = SelectMember(descriptor, $"initial member {index}");
                    var selected = (Component)Get(fake, "_currentNoteGroup");
                    object noteRef = Get(fake, "NoteRef");
                    object value = noteRef.GetType().GetField("EliteDescriptor").GetValue(noteRef);
                    Assert.That(value, Is.Not.Null);
                    Assert.That(value.GetType(), Is.EqualTo(descriptorType));
                    string source = (string)descriptorProperty.GetValue(value);
                    if (scenario.Mode == "Bars")
                        Assert.That(Get(value, "Width"), Is.EqualTo(0.5f), "Each Bars-mode kick-flam member keeps its half-width descriptor.");
                    Assert.That(source, Is.EqualTo(scenario.Sources[index]), $"{scenario.Mode} companion source {index}");
                    Assert.That(selected, Is.SameAs(Call(fake, "FindNoteGroup", Enum.Parse(modelType, scenario.Model), false)),
                        $"{scenario.Mode} must select the actual {scenario.Model} group.");
                    AssertKickFlamBodyColor(selected, new UnityEngine.Color(75f / 255f, 0f, 130f / 255f));
                    float expectedCenter = 0f;
                    if (scenario.Mode != "Bars")
                    {
                        int lane = (int)sourceMapIndexer.GetValue(sourceMap, new object[] { source });
                        expectedCenter = Convert.ToSingle(resolved.GetType().GetMethod("GetLaneX")
                            .Invoke(resolved, new object[] { lane, highwayWidth, false }));
                    }
                    var initialBounds = HorizontalBounds(selected, preview.transform);
                    if (scenario.Mode != "Bars")
                        Assert.That(initialBounds.center, Is.EqualTo(expectedCenter).Within(0.01f),
                            $"{scenario.Mode}/{source} actual renderer center");
                    else
                        Assert.That(initialBounds.center, Is.EqualTo(index == 0 ? -highwayWidth / 4f : highwayWidth / 4f).Within(0.02f),
                            "Bars must preserve the two opposing half-highway kick centers.");
                    if (scenario.Mode != "Bars")
                        Assert.That(initialBounds.width, Is.EqualTo(laneWidth).Within(0.01f),
                            $"{scenario.Mode}/{source} actual renderer width");
                    else
                        Assert.That(initialBounds.width, Is.EqualTo(highwayWidth / 2f).Within(0.02f),
                            "Bars must retain two half-highway-width rendered models.");

                    foreach (bool starPower in new[] { false, true, false })
                    {
                        noteRef.GetType().GetField("ForceStarPower").SetValue(noteRef, starPower);
                        fake.transform.localPosition += Vector3.right * 1.2f;
                        Call(fake, "OnSettingChanged");
                        selected = (Component)Get(fake, "_currentNoteGroup");
                        Assert.That(selected, Is.SameAs(Call(fake, "FindNoteGroup", Enum.Parse(modelType, scenario.Model), starPower)));
                        var refreshed = HorizontalBounds(selected, preview.transform);
                        AssertKickFlamBodyColor(selected, starPower ? UnityEngine.Color.cyan
                            : new UnityEngine.Color(75f / 255f, 0f, 130f / 255f));
                        Assert.That(refreshed.center, Is.EqualTo(scenario.Mode == "Bars"
                            ? index == 0 ? -highwayWidth / 4f : highwayWidth / 4f : expectedCenter).Within(0.02f));
                        if (scenario.Mode != "Bars")
                            Assert.That(refreshed.width, Is.EqualTo(laneWidth).Within(0.01f));
                    }
                    var originalMembers = ((IList)Get(pool, "AllSpawned")).Cast<object>().ToArray();
                    Call(pool, "ReturnAllObjects");
                    SpawnMembers();
                    Assert.That(((IList)Get(pool, "AllSpawned")).Cast<object>(), Is.EquivalentTo(originalMembers),
                        $"{scenario.Mode} must reuse every production FakeNote; LIFO return may exchange chord members.");
                    var reused = SelectMember(descriptor, $"reused member {index}");
                    Assert.That(originalMembers, Does.Contain(reused), $"{scenario.Mode} must select a reused production FakeNote.");
                    var reusedSelected = (Component)Get(reused, "_currentNoteGroup");
                    Assert.That(reusedSelected, Is.SameAs(Call(reused, "FindNoteGroup", Enum.Parse(modelType, scenario.Model), false)));
                    AssertKickFlamBodyColor(reusedSelected, new UnityEngine.Color(75f / 255f, 0f, 130f / 255f));
                    Assert.That(HorizontalBounds(reusedSelected, preview.transform).width,
                        Is.EqualTo(scenario.Mode == "Bars" ? highwayWidth / 2f : laneWidth).Within(0.02f));
                    Assert.That(HorizontalBounds(reusedSelected, preview.transform).center,
                        Is.EqualTo(scenario.Mode == "Bars" ? index == 0 ? -highwayWidth / 4f : highwayWidth / 4f : expectedCenter).Within(0.02f));
                }
                root.SetActive(false);
                Object.Destroy(root);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ActualStompAndSplashPreviewModelsMatchRuntimeTickLeadWithoutChangingNoteOrKickTiming()
        {
            const uint tick = 480;
            const double noteTime = 0.5d;
            var stomp = Pedal(EliteDrumsHatPedalType.Stomp, noteTime, tick);
            var stompKick = Kick(noteTime, tick);
            stomp.AddChildNote(stompKick);
            var splash = Pedal(EliteDrumsHatPedalType.Splash, noteTime + 0.5d, tick * 2);
            var splashKick = Kick(noteTime + 0.5d, tick * 2);
            splash.AddChildNote(splashKick);
            _fixture = new InitializedElitePlayerFixture();
            yield return _fixture.Initialize(PreparedElitePlayerFixture.CreateChart(stomp, splash), Profile(), EliteDrumsLayoutPreset.Compact);

            var runtime = _fixture.Player;
            var spawn = runtime.GetType().GetMethod("SpawnNote", INSTANCE);
            foreach (var note in new[] { stomp, stompKick, splash, splashKick })
                spawn.Invoke(runtime, new object[] { note });
            var runtimeElements = runtime.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement"), true);
            var pedalTiming = new Dictionary<EliteDrumNote, (double time, uint tick)>
            {
                [stomp] = (stomp.Time, stomp.Tick), [splash] = (splash.Time, splash.Tick)
            };
            var kickTiming = new Dictionary<EliteDrumNote, (double time, uint tick)>
            {
                [stompKick] = (stompKick.Time, stompKick.Tick), [splashKick] = (splashKick.Time, splashKick.Tick)
            };
            foreach (var pedal in new[] { stomp, splash })
            {
                var element = runtimeElements.Single(component => ReferenceEquals(Get(component, "NoteRef"), pedal));
                var childGroup = (Component)element.GetType().BaseType.GetField("NoteGroup", INSTANCE).GetValue(element);
                float runtimeLead = childGroup.transform.localPosition.z;
                double runtimeTime = pedal.Time;
                uint runtimeTick = pedal.Tick;
                double tickDuration = _fixture.Chart.SyncTrack.TickToTime(pedal.Tick + 1) -
                    _fixture.Chart.SyncTrack.TickToTime(pedal.Tick);
                float runtimeSpeed = Convert.ToSingle(runtime.GetType().GetProperty("NoteSpeed").GetValue(runtime));
                Assert.That(runtimeLead, Is.EqualTo(-(float)(runtimeSpeed * tickDuration)).Within(TOLERANCE));
                Assert.That(pedal.Time, Is.EqualTo(runtimeTime));
                Assert.That(pedal.Tick, Is.EqualTo(runtimeTick));
            }

            var previewRoot = Own(new GameObject("Production Elite pedal preview"));
            previewRoot.SetActive(false);
            var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var preview = previewRoot.AddComponent(playerType);
            playerType.GetProperty("SelectedGameMode", INSTANCE).SetValue(preview, GameMode.EliteDrums);
            playerType.GetProperty("EliteLayoutOverride", INSTANCE).SetValue(preview, EliteDrumsLayoutPreset.Compact);

            var poolObject = new GameObject("Production pedal preview pool");
            poolObject.transform.SetParent(previewRoot.transform, false);
            poolObject.SetActive(false);
            var pool = poolObject.AddComponent(Production("YARG.Gameplay.KeyedPool"));
            var poolBase = pool.GetType().BaseType;
            poolBase.GetField("_prewarmAmount", INSTANCE).SetValue(pool, 0);
            poolBase.GetField("_objectCap", INSTANCE).SetValue(pool, 8);
            poolObject.SetActive(true);

            var presetType = Production("YARG.Themes.ThemePreset");
            var preset = presetType.GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var delegateType = typeof(Func<,,>).MakeGenericType(presetType, style.GetType(), typeof(GameObject));
            var factory = Delegate.CreateDelegate(delegateType,
                Production("YARG.Settings.Preview.FakeNote").GetMethod("CreateFakeNoteFromTheme", BindingFlags.Public | BindingFlags.Static));
            var initType = playerType.GetNestedType("Info");
            var fretInitializerType = typeof(Action<,,>).MakeGenericType(initType, presetType, style.GetType());
            var fretInitializer = System.Linq.Expressions.Expression.Lambda(fretInitializerType,
                System.Linq.Expressions.Expression.Empty(), fretInitializerType.GetMethod("Invoke").GetParameters()
                    .Select(parameter => System.Linq.Expressions.Expression.Parameter(parameter.ParameterType))).Compile();
            playerType.GetMethod("Initialize", INSTANCE).Invoke(preview, new[]
            {
                preset, pool, factory,
                Production("YARG.Core.Game.CameraPreset").GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null),
                new ColorProfile("Elite pedal parity"),
                Production("YARG.Core.Game.EnginePreset").GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null),
                Production("YARG.Core.Game.HighwayPreset").GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null),
                false, fretInitializer
            });
            previewRoot.SetActive(true);
            yield return null;
            ((Behaviour)preview).enabled = false;

            var noteSpeed = Convert.ToSingle(playerType.GetField("NOTE_SPEED", BindingFlags.Public | BindingFlags.Static).GetRawConstantValue());
            var modelType = Production("YARG.Themes.ThemeNoteType");
            var noteType = Production("YARG.Settings.Preview.FakeNote");
            foreach (var (role, runtimeNote) in new[]
            {
                (EliteDrumsColorRole.Stomp, stomp), (EliteDrumsColorRole.Splash, splash)
            })
            foreach (bool starPower in new[] { false, true })
            {
                Call(pool, "ReturnAllObjects");
                Call(preview, "SpotlightEliteRole", role, starPower, null);
                Call(preview, "Advance", 0.21d);
                var spawned = (IList)Get(pool, "AllSpawned");
                Assert.That(spawned.Count, Is.GreaterThan(0), $"{role} { (starPower ? "Star Power" : "regular") } spotlight must spawn a production FakeNote.");
                var fake = (Component)spawned[spawned.Count - 1];
                Assert.That(fake.GetType(), Is.EqualTo(noteType));
                var noteRef = Get(fake, "NoteRef");
                double previewTime = (double)noteRef.GetType().GetField("Time").GetValue(noteRef);
                var noteTypeValue = noteRef.GetType().GetField("NoteType").GetValue(noteRef);
                var forceStarPower = (bool?)noteRef.GetType().GetField("ForceStarPower").GetValue(noteRef);
                var useStarPower = forceStarPower ??
                    (bool)preview.GetType().GetProperty("ForceStarPowerNotes").GetValue(preview);
                var expectedGroup = (Component)fake.GetType()
                    .GetMethod("FindNoteGroup", INSTANCE)
                    .Invoke(fake, new[] { noteTypeValue, (object)useStarPower });
                var model = (Component)noteType.GetField("_currentNoteGroup", INSTANCE).GetValue(fake);
                Assert.That(model, Is.SameAs(expectedGroup),
                    $"{role} {(starPower ? "Star Power" : "regular")} must use the production group resolution, including authored fallbacks.");
                float expected = -noteSpeed / 960f;
                Assert.That(model.transform.localPosition.z, Is.EqualTo(expected).Within(TOLERANCE));
                Call(fake, "OnSettingChanged");
                Assert.That(model.transform.localPosition.z, Is.EqualTo(expected).Within(TOLERANCE),
                    "Reapplying preview settings must preserve the production model child's one-tick lead.");
                Assert.That((double)noteRef.GetType().GetField("Time").GetValue(noteRef), Is.EqualTo(previewTime),
                    "Reapplying preview settings must not change the generated preview note time.");
                Assert.That(runtimeNote.Time, Is.EqualTo(pedalTiming[runtimeNote].time));
                Assert.That(runtimeNote.Tick, Is.EqualTo(pedalTiming[runtimeNote].tick));
                Call(pool, "ReturnAllObjects");
                Call(preview, "SpotlightEliteRole", role, starPower, null);
                Call(preview, "Advance", 0.21d);
                var reused = (Component)((IList)Get(pool, "AllSpawned"))[0];
                Assert.That(reused, Is.SameAs(fake));
                var reusedModel = (Component)noteType.GetField("_currentNoteGroup", INSTANCE).GetValue(reused);
                Assert.That(reusedModel.transform.localPosition.z, Is.EqualTo(expected).Within(TOLERANCE));
            }

            foreach (var kick in new[] { stompKick, splashKick })
            {
                Assert.That(kick.Time, Is.EqualTo(kick.ParentOrSelf.Time));
                Assert.That(kick.Tick, Is.EqualTo(kick.ParentOrSelf.Tick));
                var kickElement = runtimeElements.Single(component => ReferenceEquals(Get(component, "NoteRef"), kick));
                var kickGroup = (Component)kickElement.GetType().BaseType.GetField("NoteGroup", INSTANCE).GetValue(kickElement);
                Vector3 kickPosition = kickGroup.transform.localPosition;
                Vector3 kickScale = kickGroup.transform.localScale;
                float kickWidth = RenderedWidth(kickGroup);
                Assert.That(kickWidth, Is.GreaterThan(0f), "The paired Kick must retain rendered theme geometry.");
                Assert.That(kickPosition.z, Is.Zero.Within(TOLERANCE),
                    "The visual pedal lead must not affect a simultaneously paired Kick model or timing.");
                var update = kickElement.GetType().GetMethod("UpdateElement", INSTANCE);
                update.Invoke(kickElement, null);
                Assert.That(kickGroup.transform.localPosition, Is.EqualTo(kickPosition));
                Assert.That(kickGroup.transform.localScale, Is.EqualTo(kickScale));
                Assert.That(RenderedWidth(kickGroup), Is.EqualTo(kickWidth).Within(TOLERANCE));
                Assert.That(kick.Time, Is.EqualTo(kickTiming[kick].time));
                Assert.That(kick.Tick, Is.EqualTo(kickTiming[kick].tick));
            }
            yield return null;
        }

        private static float RenderedWidth(Component model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            Assert.That(renderers, Is.Not.Empty);
            return renderers.Max(renderer => renderer.bounds.max.x) - renderers.Min(renderer => renderer.bounds.min.x);
        }

        private static EliteDrumsLayoutPreset FiveLaneSplitKickLayout()
        {
            var layout = new EliteDrumsLayoutPreset("Parity preview five lanes");
            layout.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", null);
            layout.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat);
            layout.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1", null);
            layout.Slots[3] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layout.Slots[4] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            return layout;
        }

        private static EliteDrumsLayoutPreset CombinedKickLayout()
        {
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Parity preview combined kick");
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            return layout;
        }

        private static EliteDrumsLayoutPreset TenLaneLayout()
        {
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Parity preview ten lanes");
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            return layout;
        }

        private static EliteDrumNote Pedal(EliteDrumsHatPedalType type, double time, uint tick) => new(
            EliteDrumPad.HatPedal, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent, type,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, tick, false);

        private static EliteDrumNote Kick(double time, uint tick) => new(
            EliteDrumPad.Kick, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, tick, false);

        private static YargProfile Profile() => new()
        {
            GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.EliteDrums,
            PreferredInstrument = Instrument.EliteDrums, CurrentDifficulty = Difficulty.Hard,
            UseCymbalModels = true
        };
    }
}
