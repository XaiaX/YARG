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
using Object = UnityEngine.Object;

namespace YARG.Tests.PlayMode
{
    public sealed class ElitePreviewGeometryPlayModeTests
    {
        private const float GEOMETRY_TOLERANCE = 0.01f;
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<Object> _owned = new();
        private Component _themeManager;
        private bool _ownsThemeManager;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _capturedSettings;
        private bool _ownsAudioManager;

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private T Own<T>(T value) where T : Object
        {
            _owned.Add(value);
            return value;
        }

        private static object Get(object target, string name) => target.GetType()
            .GetProperty(name, INSTANCE).GetValue(target);

        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethods(INSTANCE).Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(target, args);

        [UnitySetUp]
        public IEnumerator SetUpProductionTheme()
        {
            _capturedSettings = false;
            _ownsAudioManager = false;
            var settingsManager = Production("YARG.Settings.SettingsManager");
            var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
            var initialized = settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            _originalSettings = settingsManager.GetProperty("Settings").GetValue(null);
            _originalSettingsInitialized = (bool)initialized.GetValue(null);
            _capturedSettings = true;

            var audioHandler = Production("YARG.Core.Audio.GlobalAudioHandler");
            var audioInstance = audioHandler.GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (audioInstance.GetValue(null) == null)
            {
                Production("YARG.Helpers.PathHelper").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
                var bassAudioManager = Production("YARG.Audio.BASS.BassAudioManager");
                audioHandler.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(bassAudioManager).Invoke(null, null);
                _ownsAudioManager = true;
            }

            if (_originalSettings == null)
            {
                var temporarySettings = Activator.CreateInstance(settingContainer);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null,
                    new[] { temporarySettings });
                initialized.SetValue(null, true);
            }

            var managerType = Production("YARG.Themes.ThemeManager");
            var managerProperty = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            _themeManager = managerProperty.GetValue(null) as Component;
            if (_themeManager == null)
            {
                _themeManager = Own(new GameObject("Fake preview geometry ThemeManager").AddComponent(managerType));
                _ownsThemeManager = true;
            }
            // Let the player loop call Start once, including when an existing scene manager
            // has not yet reached Start. Never invoke it manually.
            yield return null;
            Assert.That(managerProperty.GetValue(null), Is.SameAs(_themeManager),
                "The active production ThemeManager must be available after its player-loop initialization.");
            var defaultPreset = Production("YARG.Themes.ThemePreset").GetField("Default",
                BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            Assert.That(_themeManager.GetType().GetMethod("GetThemeContainer", INSTANCE)
                .Invoke(_themeManager, new[] { defaultPreset, style }), Is.Not.Null,
                "The existing manager must already contain the real default theme.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                var item = _owned[i];
                if (item != null) Object.Destroy(item);
            }
            _owned.Clear();
            yield return null;
            if (_ownsThemeManager)
            {
                _themeManager = null;
                _ownsThemeManager = false;
            }
            if (_capturedSettings)
            {
                var settingsManager = Production("YARG.Settings.SettingsManager");
                var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
                settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null,
                    new[] { _originalSettings });
                settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static)
                    .SetValue(null, _originalSettingsInitialized);
                _capturedSettings = false;
            }
            if (_ownsAudioManager)
            {
                Production("YARG.Core.Audio.GlobalAudioHandler")
                    .GetMethod("Close", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                _ownsAudioManager = false;
            }
        }

        [UnityTest]
        public IEnumerator ElitePairPreviewGameplayParity()
        {
            var noteType = Production("YARG.Themes.ThemeNoteType");
            foreach (int lanes in new[] { 5, 7, 10 })
            {
                var layout = lanes == 5 ? EliteDrumsLayoutPreset.Compact : TenLaneLayout();
                if (lanes == 7)
                {
                    layout.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1", "left-crash");
                    layout.Slots[3] = EliteDrumsLayoutSlot.Ordinary("tom2", null);
                    layout.Slots[4] = EliteDrumsLayoutSlot.Ordinary("tom3", null);
                    layout.Slots[5] = EliteDrumsLayoutSlot.Ordinary(null, "ride");
                    layout.Slots[6] = EliteDrumsLayoutSlot.Ordinary(null, "right-crash");
                    layout.Slots[7] = new EliteDrumsLayoutSlot();
                    layout.Slots[8] = new EliteDrumsLayoutSlot();
                    layout.Slots[9] = new EliteDrumsLayoutSlot();
                }
                var (player, pool) = CreatePreparedPlayer(layout);
                foreach (bool lefty in new[] { false, true })
                foreach (bool star in new[] { false, true })
                foreach (bool flat in new[] { false, true })
                {
                    Call(pool, "ReturnAllObjects");
                    player.GetType().GetProperty("LeftyFlip").SetValue(player, lefty);
                    Call(player, "SpotlightNoteType", Enum.Parse(noteType, "Normal"), star);
                    Call(player, "Advance", 0.21d);
                    var note = (Component)((IList)Get(pool, "AllSpawned"))[0];
                    var data = Get(note, "NoteRef");
                    float normalWidth = RenderedWidth(note);
                    data.GetType().GetField("EliteHandFlam").SetValue(data, true);
                    data.GetType().GetField("EliteFlatFlam").SetValue(data, flat);
                    Call(note, "OnSettingChanged");
                    var groups = note.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
                        .Where(g => g.gameObject.activeInHierarchy).ToArray();
                    Assert.That(groups, Has.Length.EqualTo(2), "One source FakeNote displays exactly two physical gems.");
                    float width = RenderedWidth(note);
                    Assert.That(width, Is.GreaterThan(0f));
                    float firstY = groups[0].transform.localScale.y;
                    float firstZ = groups[0].transform.localScale.z;
                    Call(note, "OnSettingChanged");
                    Assert.That(RenderedWidth(note), Is.EqualTo(width).Within(GEOMETRY_TOLERANCE));
                    Assert.That(groups[0].transform.localScale.y, Is.EqualTo(firstY));
                    Assert.That(groups[0].transform.localScale.z, Is.EqualTo(firstZ));
                    var info = Get(player, "CurrentGameModeInfo");
                    int resolvedColumns = ((EliteDrumsResolvedLayout)info.GetType().GetField("EliteLayout").GetValue(info)).Lanes.Count;
                    if (flat) Assert.That(width, Is.EqualTo(normalWidth * 2f * (resolvedColumns - 1f) / resolvedColumns)
                        .Within(GEOMETRY_TOLERANCE), "Actual flat preview mesh width matches the gameplay pair scaling contract.");
                    data.GetType().GetField("EliteHandFlam").SetValue(data, false);
                    Call(note, "OnSettingChanged");
                    Assert.That(note.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.NoteGroup"))
                        .Count(g => g.gameObject.activeInHierarchy), Is.EqualTo(1), "Companion cannot leak into ordinary reuse.");
                    Call(pool, "ReturnAllObjects");
                    Call(player, "SpotlightNoteType", Enum.Parse(noteType, "Normal"), star);
                    Call(player, "Advance", 0.21d);
                    Assert.That(((IList)Get(pool, "AllSpawned"))[0], Is.SameAs(note));
                }
                Object.Destroy(((Component)player).gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ActualThemeGhostAndNormalModelsKeepAuthoredRatiosAcrossLaneCountsAndPoolReuse()
        {
            var noteType = Production("YARG.Themes.ThemeNoteType");
            var measurements = new Dictionary<(int lanes, string type, bool starPower), float>();
            foreach (int laneCount in new[] { 5, 8, 10 })
            {
                var layout = laneCount switch
                {
                    5 => EliteDrumsLayoutPreset.Compact,
                    8 => EliteDrumsLayoutPreset.Full,
                    _ => TenLaneLayout()
                };
                var (player, pool) = CreatePreparedPlayer(layout);
                var productionRatios = ProductionGhostNormalRatios(player, noteType);
                foreach (var model in new[] { "Ghost", "Normal" })
                foreach (bool starPower in new[] { false, true })
                {
                    Call(pool, "ReturnAllObjects");
                    Call(player, "SpotlightNoteType", Enum.Parse(noteType, model), starPower);
                    Call(player, "Advance", 0.21d);
                    var spawned = (IList)Get(pool, "AllSpawned");
                    Assert.That(spawned.Count, Is.GreaterThan(0), "Advance must spawn a real pooled FakeNote.");
                    var note = (Component)spawned[spawned.Count - 1];
                    Assert.That(note.GetType().FullName, Is.EqualTo("YARG.Settings.Preview.FakeNote"));
                    Assert.That(note.gameObject.activeInHierarchy, Is.True);
                    float firstWidth = RenderedWidth(note);
                    Assert.That(firstWidth, Is.GreaterThan(0f), "The selected authored theme model must render geometry.");

                    Call(note, "OnSettingChanged");
                    Assert.That(RenderedWidth(note), Is.EqualTo(firstWidth).Within(GEOMETRY_TOLERANCE),
                        "OnSettingChanged must not compound authored group scaling.");
                    measurements[(laneCount, model, starPower)] = firstWidth;

                    Call(pool, "ReturnAllObjects");
                    Assert.That(((IList)Get(pool, "AllSpawned")).Count, Is.Zero);
                    Call(player, "SpotlightNoteType", Enum.Parse(noteType, model), starPower);
                    Call(player, "Advance", 0.21d);
                    var reused = (Component)((IList)Get(pool, "AllSpawned"))[0];
                    Assert.That(reused, Is.SameAs(note), "The production keyed pool must reuse the same FakeNote.");
                    Assert.That(RenderedWidth(reused), Is.EqualTo(firstWidth).Within(GEOMETRY_TOLERANCE),
                        $"Actual {model} {(starPower ? "Star Power" : "regular")} theme geometry must remain stable at {laneCount} lanes.");
                }

                foreach (bool starPower in new[] { false, true })
                {
                    float ghost = measurements[(laneCount, "Ghost", starPower)];
                    float normal = measurements[(laneCount, "Normal", starPower)];
                    float ratio = ghost / normal;
                    float expectedProductionRatio = productionRatios[starPower];
                    Assert.That(ratio, Is.EqualTo(expectedProductionRatio).Within(GEOMETRY_TOLERANCE),
                        $"The {(starPower ? "Star Power" : "regular")} Ghost/Normal ratio must match the actual model groups selected by production theme fallback at {laneCount} lanes.");
                    float fiveLaneRatio = measurements[(5, "Ghost", starPower)] /
                        measurements[(5, "Normal", starPower)];
                    Assert.That(ratio, Is.EqualTo(fiveLaneRatio).Within(GEOMETRY_TOLERANCE),
                        $"The actual {(starPower ? "Star Power" : "regular")} Ghost/Normal model ratio must remain stable at {laneCount} lanes.");
                }
                Object.Destroy(((Component)player).gameObject);
            }
            // Compare absolute rendered widths only after all layouts have been measured.
            foreach (bool starPower in new[] { false, true })
            foreach (var model in new[] { "Ghost", "Normal" })
            {
                float fiveLaneWidth = measurements[(5, model, starPower)];
                float tenLaneWidth = measurements[(10, model, starPower)];
                Assert.That(fiveLaneWidth, Is.EqualTo(tenLaneWidth * 2f).Within(GEOMETRY_TOLERANCE),
                    $"Absolute {model} renderer width should halve from five active lanes to ten; widths={fiveLaneWidth}/{tenLaneWidth}.");
                Assert.That(measurements[(8, model, starPower)],
                    Is.EqualTo(fiveLaneWidth * 5f / 8f).Within(GEOMETRY_TOLERANCE),
                    "Eight-lane geometry must also follow the absolute lane-width scale.");
            }
            yield return null;
        }

        private static Dictionary<bool, float> ProductionGhostNormalRatios(Component player, Type noteType)
        {
            var prefab = (GameObject)player.GetType().GetField("_notePrefab", INSTANCE).GetValue(player);
            Assert.That(prefab, Is.Not.Null, "FakeTrackPlayer must expose its production-created themed FakeNote prefab.");
            var noteGroups = (IList)Production("YARG.Settings.Preview.FakeNote")
                .GetField("_noteGroups", INSTANCE).GetValue(prefab.GetComponent(Production("YARG.Settings.Preview.FakeNote")));
            var ratios = new Dictionary<bool, float>();
            foreach (bool starPower in new[] { false, true })
            {
                float ghost = ProductionGroupWidth(noteGroups, Enum.Parse(noteType, "Ghost"), starPower, noteType);
                float normal = ProductionGroupWidth(noteGroups, Enum.Parse(noteType, "Normal"), starPower, noteType);
                Assert.That(ghost, Is.GreaterThan(0f));
                Assert.That(normal, Is.GreaterThan(0f));
                ratios.Add(starPower, ghost / normal);
            }
            return ratios;
        }

        private static float ProductionGroupWidth(IList noteGroups, object requestedType, bool starPower, Type noteType)
        {
            object selected = null;
            if (starPower)
                selected = FindGroup(noteGroups, requestedType, true);
            selected ??= FindGroup(noteGroups, requestedType, false);
            // FakeNote.FindNoteGroup's final fallback for any absent type is regular Normal.
            selected ??= FindGroup(noteGroups, Enum.Parse(noteType, "Normal"), false);
            Assert.That(selected, Is.Not.Null, $"Production FakeNote has no model group for {requestedType} or regular Normal fallback.");
            var group = (Component)selected.GetType().GetField("Group", INSTANCE).GetValue(selected);
            Assert.That(group, Is.Not.Null);
            var renderers = group.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty, $"Production selected group {group.name} must render geometry.");
            return renderers.Max(renderer => renderer.bounds.max.x) - renderers.Min(renderer => renderer.bounds.min.x);
        }

        private static object FindGroup(IList noteGroups, object noteType, bool starPower)
        {
            foreach (var pair in noteGroups)
            {
                var pairType = pair.GetType();
                if (Equals(pairType.GetField("NoteType", INSTANCE).GetValue(pair), noteType) &&
                    (bool)pairType.GetField("StarPower", INSTANCE).GetValue(pair) == starPower)
                    return pair;
            }
            return null;
        }

        internal (Component player, Component pool) CreatePreparedPlayer(EliteDrumsLayoutPreset layout)
        {
            var root = Own(new GameObject("Production fake preview player"));
            root.SetActive(false);
            var player = root.AddComponent(Production("YARG.Settings.Preview.FakeTrackPlayer"));
            player.GetType().GetProperty("SelectedGameMode", INSTANCE).SetValue(player, GameMode.EliteDrums);
            player.GetType().GetProperty("EliteLayoutOverride", INSTANCE).SetValue(player, layout);

            var poolObject = new GameObject("Production fake preview note pool");
            poolObject.transform.SetParent(root.transform, false);
            poolObject.SetActive(false);
            var pool = poolObject.AddComponent(Production("YARG.Gameplay.KeyedPool"));
            var poolBase = pool.GetType().BaseType;
            poolBase.GetField("_prewarmAmount", INSTANCE).SetValue(pool, 0);
            poolBase.GetField("_objectCap", INSTANCE).SetValue(pool, 32);
            poolObject.SetActive(true);

            var presetType = Production("YARG.Themes.ThemePreset");
            var preset = presetType.GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
            var factoryType = typeof(Func<,,>).MakeGenericType(presetType, style.GetType(), typeof(GameObject));
            var factory = Delegate.CreateDelegate(factoryType,
                Production("YARG.Settings.Preview.FakeNote").GetMethod("CreateFakeNoteFromTheme", BindingFlags.Public | BindingFlags.Static));
            var colorProfile = new ColorProfile("Fake preview geometry test");
            var infoType = player.GetType().GetNestedType("Info");
            var fretInitializerType = typeof(Action<,,>).MakeGenericType(infoType, presetType, style.GetType());
            var fretInitializer = System.Linq.Expressions.Expression.Lambda(fretInitializerType,
                System.Linq.Expressions.Expression.Empty(),
                fretInitializerType.GetMethod("Invoke").GetParameters().Select(parameter =>
                    System.Linq.Expressions.Expression.Parameter(parameter.ParameterType))).Compile();
            player.GetType().GetMethod("Initialize", INSTANCE).Invoke(player, new[]
            {
                preset, pool, factory,
                Production("YARG.Core.Game.CameraPreset").GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null),
                colorProfile,
                Production("YARG.Core.Game.EnginePreset").GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null),
                Production("YARG.Core.Game.HighwayPreset").GetField("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null),
                false, fretInitializer
            });
            root.SetActive(true);
            ((Behaviour)player).enabled = false;
            return (player, pool);
        }

        internal void DisposePreparedPlayers()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
                if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        private static float RenderedWidth(Component note)
        {
            var renderers = note.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            Assert.That(renderers, Is.Not.Empty, "The active production theme model must have enabled renderers.");
            return renderers.Max(renderer => renderer.bounds.max.x) - renderers.Min(renderer => renderer.bounds.min.x);
        }

        private static EliteDrumsLayoutPreset TenLaneLayout()
        {
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Preview geometry ten lane");
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            return layout;
        }
    }
}
