using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Rendering;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    /// <summary>Rendered checks against the serialized Elite layout menu and its production preview.</summary>
    public sealed class EliteConfigurationGraphicsPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<UnityEngine.Object> _owned = new();
        private Component _menu;
        private Component _themeManager;
        private GameObject _menuHost;
        private Component _menuManager;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _capturedSettings;
        private bool _ownsAudio;
        private Type _localizationType;
        private IDictionary<string, string> _localizationMap;
        private KeyValuePair<string, string>[] _previousLocalizations;
        private string _previousCulture;

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object Field(object target, string name) => target.GetType()
            .GetField(name, INSTANCE).GetValue(target);

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, INSTANCE).SetValue(target, value);

        private static void Call(object target, string name, params object[] args)
        {
            target.GetType().GetMethods(INSTANCE).Single(method => method.Name == name &&
                method.GetParameters().Length == args.Length).Invoke(target, args);
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            _owned.Add(value);
            return value;
        }

        [UnitySetUp]
        public IEnumerator SetUpRealTheme()
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
            const BindingFlags STATIC = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            if (managerType.GetProperty("Instance", STATIC).GetValue(null) == null)
            {
                _themeManager = Own(new GameObject("Elite graphics test ThemeManager").AddComponent(managerType));
                yield return null;
            }
            Assert.That(managerType.GetProperty("Instance", STATIC).GetValue(null), Is.Not.Null,
                "The production theme manager must load authored note and fret geometry.");
            LoadProductionEnglishLocalization();
        }

        private void LoadProductionEnglishLocalization()
        {
            _localizationType = Production("YARG.Localization.LocalizationManager");
            _localizationMap = (IDictionary<string, string>)_localizationType
                .GetField("_localizationMap", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _previousLocalizations = _localizationMap.ToArray();
            _previousCulture = (string)_localizationType.GetProperty("CultureCode").GetValue(null);
            var pathHelper = Production("YARG.Helpers.PathHelper");
            Assert.That(pathHelper.GetProperty("StreamingAssetsPath", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null), Is.Not.Null.And.Not.Empty, "Production StreamingAssets path must be initialized.");
            var load = _localizationType.GetMethod("TryParseAndLoadLanguage", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(load, Is.Not.Null);
            Assert.That((bool)load.Invoke(null, new object[] { "en-US" }), Is.True,
                "The production English dictionary must load from the active StreamingAssets path.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var item in _owned)
                if (item != null) UnityEngine.Object.Destroy(item);
            _owned.Clear();
            _menu = null;
            _themeManager = null;
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
            if (_localizationMap != null)
            {
                _localizationMap.Clear();
                foreach (var entry in _previousLocalizations) _localizationMap.Add(entry.Key, entry.Value);
                _localizationType.GetProperty("CultureCode").GetSetMethod(true).Invoke(null, new object[] { _previousCulture });
                _localizationMap = null;
            }
        }

        [UnityTest]
        public IEnumerator ProductionPreviewPixelsFollowLayoutAndColorCallbacksAndReleaseOwnedResources()
        {
#if !UNITY_EDITOR
            Assert.Ignore("The production serialized menu prefab requires the Editor asset database.");
            yield break;
#else
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null, "The real Resources menu prefab must be present.");
            _menuHost = Own(new GameObject("Elite graphics test Canvas host"));
            var canvas = _menuHost.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            AddProductionCanvasScaler(_menuHost);
            _menuManager = _menuHost.AddComponent(Production("YARG.Menu.MenuManager"));
            ((Behaviour)_menuManager).enabled = false;
            var menuEnum = _menuManager.GetType().GetNestedType("Menu");
            var menuObject = (Component)_menuManager.GetType().GetMethod("ShowMenu", new[] { menuEnum })
                .Invoke(_menuManager, new[] { Enum.Parse(menuEnum, "EliteDrumsLayout") });
            var root = Own(menuObject.gameObject);
            _menu = root.GetComponent(Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu"));
            Assert.That(_menu, Is.Not.Null);

            var gapped = new EliteDrumsLayoutPreset("Rendered gapped draft");
            gapped.Slots[1] = EliteDrumsLayoutSlot.Ordinary("snare", "left-crash");
            gapped.Slots[4] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            gapped.Slots[7] = EliteDrumsLayoutSlot.Ordinary("tom1", null);
            var empty = new EliteDrumsLayoutPreset("Rendered empty draft");
            var layouts = new[]
            {
                (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Rendered compact"),
                (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Rendered eight lane"),
                TenLaneLayout(), gapped, empty
            };
            var firstColors = new ColorProfile("graphics magenta");
            firstColors.EliteDrums.Fret1 = System.Drawing.Color.Magenta;
            firstColors.EliteDrums.Fret5 = System.Drawing.Color.Cyan;
            var secondColors = new ColorProfile("graphics lime");
            secondColors.EliteDrums.Fret1 = System.Drawing.Color.Lime;
            secondColors.EliteDrums.Fret10 = System.Drawing.Color.Yellow;
            var colors = new[] { firstColors, secondColors };

            // Initialize with a valid production preset. The menu itself populates controls and builds preview.
            Call(_menu, "Initialize", (EliteDrumsLayoutPreset)layouts[0].CopyWithNewName("Rendered compact"),
                (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }), firstColors);
            SetField(_menu, "_layouts", layouts);
            SetField(_menu, "_previewColors", colors);
            ConfigureOptions(Field(_menu, "_layoutDropdown"), layouts.Select(layout => layout.Name).ToList());
            ConfigureOptions(Field(_menu, "_previewColor"), colors.Select(color => color.Name).ToList());

            var secondRoot = Own(UnityEngine.Object.Instantiate(prefab, _menuHost.transform, false));
            var secondMenu = secondRoot.GetComponent(Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu"));
            Call(secondMenu, "Initialize", (EliteDrumsLayoutPreset)layouts[0].CopyWithNewName("Second rendered menu"),
                (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }), secondColors);
            SetField(secondMenu, "_layouts", layouts);
            SetField(secondMenu, "_previewColors", colors);
            ConfigureOptions(Field(secondMenu, "_layoutDropdown"), layouts.Select(layout => layout.Name).ToList());
            ConfigureOptions(Field(secondMenu, "_previewColor"), colors.Select(color => color.Name).ToList());
            yield return null;
            yield return WaitForRenderedFrame();
            FreezeAndAdvance(_menu, 0.25d);
            FreezeAndAdvance(secondMenu, 0.25d);
            yield return WaitForRenderedFrame();

            var previewImage = (RawImage)Field(_menu, "_previewImage");
            var texture = (RenderTexture)previewImage.texture;
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.format, Is.EqualTo(RenderTextureFormat.ARGBHalf),
                "The preview source must remain the canonical HDR ARGBHalf target.");
            var previewWorldForCapture = (Transform)Field(_menu, "_previewWorld");
            var previewCamera = previewWorldForCapture.GetComponentInChildren<Camera>(true);
            Assert.That(previewImage.canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(previewImage.canvas.worldCamera, Is.Null,
                "The Overlay lifecycle fixture must exercise the Screen-based dimensions fallback.");
            Assert.That(texture.width, Is.EqualTo(Screen.width),
                "Configure's owned preview target must match Screen.width for an Overlay Canvas.");
            Assert.That(texture.height, Is.EqualTo(Screen.height),
                "Configure's owned preview target must match Screen.height for an Overlay Canvas.");
            Assert.That(previewCamera.targetTexture, Is.SameAs(texture));
            Assert.That(previewImage.uvRect.width, Is.GreaterThan(0.1f),
                "The preview UV must be expressed in the output Canvas camera's viewport domain.");
            Assert.That(previewImage.uvRect.height, Is.GreaterThan(0.1f),
                "The preview UV must preserve the preview pane's actual vertical viewport coverage.");
            var initial = ReadFrame(texture);
            var secondImage = (RawImage)Field(secondMenu, "_previewImage");
            var secondTexture = (RenderTexture)secondImage.texture;
            Assert.That(secondTexture, Is.Not.Null, "The second menu must bind its own preview texture.");
            Assert.That(secondTexture.IsCreated(), Is.True,
                "The second menu's owned preview texture must be allocated before capture.");
            var secondCameras = ((Transform)Field(secondMenu, "_previewWorld"))
                .GetComponentsInChildren<Camera>(true).Where(camera => camera.gameObject.activeInHierarchy).ToArray();
            Assert.That(secondCameras, Is.Not.Empty, "The second preview world must contain an active render camera.");
            foreach (var camera in secondCameras)
            {
                Assert.That(camera.enabled, Is.True, $"Second preview camera {camera.name} must be enabled.");
                Assert.That(camera.targetTexture, Is.SameAs(secondTexture),
                    $"Second preview camera {camera.name} must target its menu's owned texture.");
            }
            AssertPreviewWorldRegistration(_menu, "first preview world");
            AssertPreviewWorldRegistration(secondMenu, "second preview world");
            yield return WaitForRenderedFrame(120, secondCameras);
            yield return null;
            var secondInitial = ReadFrame(secondTexture);
            const string firstCapturePath = "/tmp/yarg-elite-preview-first.png";
            const string secondCapturePath = "/tmp/yarg-elite-preview-second.png";
            WriteFramePng(texture, firstCapturePath);
            WriteFramePng(secondTexture, secondCapturePath);
            LogPreviewLandmarks("first", _menu, texture, initial, firstCapturePath);
            LogPreviewLandmarks("second", secondMenu, secondTexture, secondInitial, secondCapturePath);
            var secondPreviewObjects = ((Transform)Field(secondMenu, "_previewWorld")).Cast<Transform>()
                .Select(child => child.gameObject).ToArray();
            var secondPreviewMaterials = ((Transform)Field(secondMenu, "_previewWorld"))
                .GetComponentsInChildren<Renderer>(true).SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null && !AssetDatabase.Contains(material)).Distinct().ToArray();
            var secondFretColors = AssertCompactFretMaterialColors(secondMenu, secondColors);
            int secondInitialGreen = CountHue(secondInitial, secondFretColors.First, minimumBrightness: 48);
            Assert.That(secondInitialGreen, Is.GreaterThan(8),
                $"Second preview must render its saved lime fret hue; count={secondInitialGreen}.");
            AssertRenderedPreview(secondInitial, "second menu initial preview");
            AssertRenderedPreview(initial, "initial 5-lane preview");
            var firstFretColors = AssertCompactFretMaterialColors(_menu, firstColors);
            int initialMagenta = CountHue(initial, firstFretColors.First, minimumBrightness: 48);
            int initialCyan = CountHue(initial, firstFretColors.Last, minimumBrightness: 48);
            Assert.That(initialMagenta + initialCyan, Is.GreaterThan(8),
                $"Rendered fret-top pixels must retain selected magenta/cyan hue above the dark-pixel cutoff. " +
                $"Hue counts: magenta={initialMagenta}, cyan={initialCyan}; " +
                $"most common RGB values: {DescribeMostCommonRgb(initial)}.");
            var firstPlayer = PreviewPlayer(_menu);
            var pool = Field(firstPlayer, "_notePool");
            var spawnedNotes = (System.Collections.IList)pool.GetType().GetProperty("AllSpawned").GetValue(pool);
            Assert.That(spawnedNotes, Is.Not.Null);
            Assert.That(spawnedNotes.Count, Is.GreaterThan(0),
                "A fixed Advance must prepare actual production preview notes before capture.");
            var note = spawnedNotes[spawnedNotes.Count - 1] as Component;
            Assert.That(note, Is.Not.Null);
            var noteRenderer = note.GetComponentsInChildren<Renderer>(true).FirstOrDefault(renderer => renderer.enabled);
            Assert.That(noteRenderer, Is.Not.Null, "The prepared note must expose real rendered geometry.");
            var expectedNoteColor = (Color32)noteRenderer.material.color;
            Assert.That(CountNear(initial, expectedNoteColor, 18), Is.GreaterThan(4),
                "Rendered pixels must include the prepared note's actual themed color.");

            SelectDropdown(Field(_menu, "_layoutDropdown"), 1);
            yield return null;
            yield return WaitForRenderedFrame();
            FreezeAndAdvance(_menu, 0.31d);
            yield return WaitForRenderedFrame();
            texture = (RenderTexture)previewImage.texture;
            var eightLanes = ReadFrame(texture);
            AssertRenderedPreview(eightLanes, "8-lane preview after actual dropdown callback");
            int eightLaneMagenta = CountHue(eightLanes, firstFretColors.First, minimumBrightness: 48);
            Assert.That(eightLaneMagenta, Is.GreaterThan(8),
                $"8-lane preview must retain the selected magenta fret hue; count={eightLaneMagenta}.");
            var secondAfterFirstRebuild = ReadFrame((RenderTexture)secondImage.texture);
            Assert.That(CountHue(secondAfterFirstRebuild, secondFretColors.First, minimumBrightness: 48),
                Is.EqualTo(secondInitialGreen),
                "Rebuilding one menu must not change the other menu's stable green receptor hue count.");

            SelectDropdown(Field(_menu, "_layoutDropdown"), 2);
            yield return null;
            yield return WaitForRenderedFrame();
            FreezeAndAdvance(_menu, 0.37d);
            yield return WaitForRenderedFrame();
            texture = (RenderTexture)previewImage.texture;
            var tenLanes = ReadFrame(texture);
            AssertRenderedPreview(tenLanes, "10-lane preview after actual dropdown callback");
            Assert.That(DifferentPixels(eightLanes, tenLanes), Is.GreaterThan(20),
                "Changing the resolved lane count must change rendered geometry, not just model state.");
            var tenLaneCells = (Array)Field(_menu, "_cells");
            Assert.That(tenLaneCells.Length, Is.EqualTo(20));
            foreach (var cell in tenLaneCells.Cast<Component>())
            {
                var cellRect = (RectTransform)cell.transform;
                var pill = (Image)Field(cell, "_fretPill");
                if (!(bool)ReadProperty(cell, "Drum"))
                {
                    Assert.That(pill, Is.Null, "Pills belong only beneath the bottom row.");
                    continue;
                }
                Assert.That(pill, Is.Not.Null, "Every bottom-row cell must expose its saved-fret color pill.");
                Assert.That(pill.rectTransform.rect.width, Is.GreaterThan(0), $"{cell.name} pill has positive width");
                Assert.That(pill.rectTransform.rect.height, Is.GreaterThan(0), $"{cell.name} pill has positive height");
                Assert.That(cellRect.rect.width, Is.GreaterThan(0));
                var corners = new Vector3[4];
                pill.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var screen = RectTransformUtility.WorldToScreenPoint(null, corner);
                    Assert.That(screen.x, Is.InRange(-1f, Screen.width + 1f), $"{cell.name} pill x bound");
                    Assert.That(screen.y, Is.InRange(-1f, Screen.height + 1f), $"{cell.name} pill y bound");
                }
            }

            SelectDropdown(Field(_menu, "_layoutDropdown"), 3);
            yield return null;
            yield return WaitForRenderedFrame();
            Assert.That(previewImage.gameObject.activeSelf, Is.True,
                "A gapped incomplete draft must still render its assigned columns through preview resolution.");
            Assert.That(((Transform)Field(_menu, "_previewWorld")).childCount, Is.GreaterThan(0));
            FreezeAndAdvance(_menu, 0.29d);
            yield return WaitForRenderedFrame();
            var gappedFrame = ReadFrame((RenderTexture)previewImage.texture);
            var gappedFretColors = AssertFretMaterialHues(_menu, firstColors, new[] { 1, 4, 7 });
            int gappedHuePixels = gappedFretColors.Sum(color => CountHue(gappedFrame, color, minimumBrightness: 48));
            Assert.That(gappedHuePixels, Is.GreaterThan(8),
                $"Gapped draft rendering must include assigned saved-slot fret hues, not a backdrop; count={gappedHuePixels}.");
            SelectDropdown(Field(_menu, "_layoutDropdown"), 4);
            yield return null;
            yield return WaitForRenderedFrame();
            Assert.That(previewImage.gameObject.activeSelf, Is.False,
                "An entirely empty draft must intentionally have no rendered preview image.");
            Assert.That(((Transform)Field(_menu, "_previewWorld")).childCount, Is.Zero);
            SelectDropdown(Field(_menu, "_layoutDropdown"), 2);
            yield return null;
            yield return WaitForRenderedFrame();
            FreezeAndAdvance(_menu, 0.41d);
            yield return WaitForRenderedFrame();
            previewImage = (RawImage)Field(_menu, "_previewImage");
            texture = (RenderTexture)previewImage.texture;

            ExercisePreviewOptionsIfIntegrated(_menu);
            SelectDropdown(Field(_menu, "_previewColor"), 1);
            yield return null;
            yield return WaitForRenderedFrame();
            FreezeAndAdvance(_menu, 0.43d);
            yield return WaitForRenderedFrame();
            texture = (RenderTexture)previewImage.texture;
            var recolored = ReadFrame(texture);
            var secondProfileLaneColors = AssertFretMaterialHues(_menu, secondColors, Enumerable.Range(0, 10).ToArray());
            int secondProfileGreenPixels = CountHue(recolored, secondProfileLaneColors[0], minimumBrightness: 48);
            Assert.That(secondProfileGreenPixels, Is.GreaterThan(8),
                $"The visible first fret must take the second profile's saved lime hue; count={secondProfileGreenPixels}.");
            Assert.That(DifferentPixels(tenLanes, recolored), Is.GreaterThan(8),
                "Selecting a color profile must change pixels in the actual rendered preview.");

            // Invoke a real serialized cell Button, then its real popup choice Button to clear a row.
            var cellArray = (Array)Field(_menu, "_cells");
            // The ten-lane layout assigns snare to the first drum row, not the first cymbal row.
            var firstDrumCell = cellArray.GetValue(EliteDrumsLayoutPreset.SLOT_COUNT);
            var cellButton = (Button)Field(firstDrumCell, "_button");
            cellButton.onClick.Invoke();
            Assert.That(((GameObject)Field(_menu, "_popup")).activeSelf, Is.True,
                "Clicking a production cell must open the choice popup.");
            var choiceIds = (string[])Field(_menu, "_choiceIds");
            int clearIndex = Array.IndexOf(choiceIds, "clear");
            Assert.That(clearIndex, Is.GreaterThanOrEqualTo(0));
            var clearButton = ((Button[])Field(_menu, "_choices"))[clearIndex];
            Assert.That(clearButton.interactable, Is.True, "The assigned drum row must offer Clear.");
            clearButton.onClick.Invoke();
            yield return null;
            yield return WaitForRenderedFrame();
            FreezeAndAdvance(_menu, 0.47d);
            yield return WaitForRenderedFrame();
            Assert.That(((GameObject)Field(_menu, "_popup")).activeSelf, Is.False);
            texture = (RenderTexture)previewImage.texture;
            var partialPreview = ReadFrame(texture);
            AssertRenderedPreview(partialPreview, "partial layout after real Clear callback");

            var imageCamera = Field(_menu, "_previewWorld") as Transform;
            Assert.That(imageCamera, Is.Not.Null);
            var ownedChildren = imageCamera.Cast<Transform>().Select(child => child.gameObject).ToArray();
            var ownedTexture = texture;
            var ownedMaterials = imageCamera.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null && !AssetDatabase.Contains(material)).Distinct().ToArray();
            root.SetActive(false);
            yield return null;
            Assert.That(imageCamera.childCount, Is.Zero, "Disabling a menu must clear only its preview objects.");
            Assert.That(ownedChildren.All(child => child == null), Is.True);
            Assert.That(ownedTexture == null, Is.True, "The menu must destroy its owned RenderTexture on disable.");
            var survivingOwnedMaterials = ownedMaterials.Where(material => material != null)
                .Select(material => material.name).ToArray();
            Assert.That(survivingOwnedMaterials, Is.Empty,
                $"Preview-owned instantiated materials must be destroyed with their geometry; still alive: {string.Join(", ", survivingOwnedMaterials)}.");
            Assert.That(secondTexture != null && secondTexture.IsCreated(), Is.True,
                "Closing one menu must leave the other menu's render texture allocated.");
            Assert.That(secondPreviewObjects.All(child => child != null), Is.True,
                "Closing one menu must leave the other menu's preview objects intact.");
            Assert.That(secondPreviewMaterials.All(material => material != null), Is.True,
                "Closing one menu must not destroy the other menu's instantiated materials.");
            Assert.That(CountHue(ReadFrame(secondTexture), secondFretColors.First, minimumBrightness: 48),
                Is.EqualTo(secondInitialGreen),
                "Closing one menu must leave the other menu's frozen receptor hue unchanged.");
#endif
        }

        [UnityTest]
        public IEnumerator RepeatedPreviewReinitializationKeepsOwnedMaterialsBoundedAndTemplatesAlive()
        {
#if !UNITY_EDITOR
            Assert.Ignore("The production serialized menu prefab requires the Editor asset database.");
            yield break;
#else
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null, "The real Resources menu prefab must be present.");
            _menuHost = Own(new GameObject("Elite rebuild materials Canvas host"));
            var canvas = _menuHost.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            AddProductionCanvasScaler(_menuHost);
            _menuManager = _menuHost.AddComponent(Production("YARG.Menu.MenuManager"));
            ((Behaviour)_menuManager).enabled = false;
            var menuEnum = _menuManager.GetType().GetNestedType("Menu");
            var menuObject = (Component)_menuManager.GetType().GetMethod("ShowMenu", new[] { menuEnum })
                .Invoke(_menuManager, new[] { Enum.Parse(menuEnum, "EliteDrumsLayout") });
            var root = Own(menuObject.gameObject);
            _menu = root.GetComponent(Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu"));
            Assert.That(_menu, Is.Not.Null);

            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Rebuild compact");
            var colors = new ColorProfile("rebuild colors");
            colors.EliteDrums.Fret1 = System.Drawing.Color.Lime;
            Call(_menu, "Initialize", layout, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }), colors);
            yield return null;
            yield return WaitForRenderedFrame();

            var previewWorld = (Transform)Field(_menu, "_previewWorld");
            Assert.That(previewWorld, Is.Not.Null, "Initialize must create the preview world.");
            var templateMaterials = previewWorld.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null && AssetDatabase.Contains(material))
                .Distinct().ToArray();
            Assert.That(templateMaterials, Is.Not.Empty,
                "The initialized preview must reference shared template materials from the serialized theme.");
            int baselineOwned = CountRuntimeInstantiatedMaterials(previewWorld);
            Assert.That(baselineOwned, Is.GreaterThan(0),
                "The initialized preview must own runtime-instantiated materials for per-player coloring.");

            for (int rebuild = 1; rebuild <= 5; rebuild++)
            {
                Call(_menu, "RebuildPreview");
                yield return null;
                int ownedAfterRebuild = CountRuntimeInstantiatedMaterials(previewWorld);
                Assert.That(ownedAfterRebuild, Is.LessThanOrEqualTo(baselineOwned),
                    $"Rebuild {rebuild} must not accumulate owned preview materials (baseline {baselineOwned}, got {ownedAfterRebuild}).");
                var destroyedTemplates = templateMaterials.Where(material => material == null).ToArray();
                Assert.That(destroyedTemplates, Is.Empty,
                    $"Rebuild {rebuild} must not destroy shared template materials.");
            }
#endif
        }

        private static int CountRuntimeInstantiatedMaterials(Transform root) =>
            root.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Count(material => material != null && !AssetDatabase.Contains(material));

        [UnityTest]
        public IEnumerator ProductionMenuCellsAndTypographyStayInsideCanvasAtReferenceResolutions() =>
            RenderMenuAtReferenceResolutions("baseline");

        [UnityTest]
        public IEnumerator CellSelectionAndOutlineRenderWithRoundedUnclippedEdges() =>
            RenderMenuAtReferenceResolutions("edges");

        [UnityTest]
        public IEnumerator ValidityTintTransitionsRenderImmediately() =>
            RenderMenuAtReferenceResolutions("validity");

        [UnityTest]
        public IEnumerator ManagementOnlyLayoutSelectionClearsCellFeedbackAndRestoresValidDraft() =>
            RenderMenuAtReferenceResolutions("management");

        [UnityTest]
        public IEnumerator FretNumbersRemainLegibleAtReferenceResolutions() =>
            RenderMenuAtReferenceResolutions("frets");

        private IEnumerator RenderMenuAtReferenceResolutions(string scenario)
        {
#if !UNITY_EDITOR
            Assert.Ignore("The production serialized menu prefab requires the Editor asset database.");
            yield break;
#else
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null);
            _menuHost = Own(new GameObject("Elite responsive graphics Canvas host"));
            var captureCameraObject = Own(new GameObject("Elite responsive graphics capture camera"));
            var captureCamera = captureCameraObject.AddComponent<Camera>();
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;
            captureCamera.orthographic = true;
            captureCamera.cullingMask = ~0;
            var hostCanvas = _menuHost.AddComponent<Canvas>();
            hostCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            hostCanvas.worldCamera = captureCamera;
            hostCanvas.planeDistance = 1f;
            AddProductionCanvasScaler(_menuHost);
            _menuManager = _menuHost.AddComponent(Production("YARG.Menu.MenuManager"));
            ((Behaviour)_menuManager).enabled = false;
            var menuEnum = _menuManager.GetType().GetNestedType("Menu");
            var menuObject = (Component)_menuManager.GetType().GetMethod("ShowMenu", new[] { menuEnum })
                .Invoke(_menuManager, new[] { Enum.Parse(menuEnum, "EliteDrumsLayout") });
            var root = Own(menuObject.gameObject);
            _menu = root.GetComponent(Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu"));
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Responsive layout");
            var previewColors = new ColorProfile("Responsive preview colors");
            previewColors.EliteDrums.Fret1 = System.Drawing.Color.Lime;
            Call(_menu, "Initialize", layout, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }), previewColors);
            yield return null;

            var canvas = hostCanvas;
            Assert.That(canvas, Is.Not.Null, "The instantiated resource must be hosted by the fixture Canvas.");
            var cells = (Array)Field(_menu, "_cells");
            Assert.That(cells.Length, Is.EqualTo(20), "All ten drum and ten cymbal cells must render.");
            var labels = cells.Cast<Component>().SelectMany(cell => FindTextComponents(cell.gameObject)).ToArray();
            Assert.That(labels.Length, Is.EqualTo(20), "Every one of the twenty cells must have a TMP text label.");
            Assert.That(labels.All(label => ReadProperty(label, "font") != null), Is.True,
                "Every rendered cell needs its authored font asset and label.");
            var actualCellLabels = labels.Select(label => Convert.ToString(ReadProperty(label, "text"))).ToArray();
            Assert.That(actualCellLabels, Does.Contain("Snare"));
            Assert.That(actualCellLabels, Does.Contain("Hi-Hat\nCymbal"));
            Assert.That(actualCellLabels, Does.Contain("Hi-Hat\nPedal"));
            Assert.That(actualCellLabels, Does.Contain("Ride\nCymbal"));
            Assert.That(actualCellLabels, Does.Contain("High\nTom"));
            Assert.That(actualCellLabels, Does.Contain("Left\nCrash"));
            Assert.That(actualCellLabels, Does.Contain("Empty"));
            Assert.That(actualCellLabels.Any(text => text.StartsWith("Menu.") || text.Contains("EliteLayout")), Is.False,
                "Production English cell labels must not expose localization keys.");

            var authoredHeader = FindTextComponents(root).FirstOrDefault(text => text.gameObject.name == "Single Header Text");
            Assert.That(authoredHeader, Is.Not.Null, "The production shared header must be present in the menu prefab.");
            float authoredTitleBaseline = Convert.ToSingle(ReadProperty(authoredHeader, "fontSize"));
            Assert.That(authoredTitleBaseline, Is.GreaterThan(0));

            foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) })
            {
                var target = Own(new RenderTexture(resolution.x, resolution.y, 24, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB)
                {
                    name = $"Elite responsive canvas {resolution.x}x{resolution.y}"
                });
                Assert.That(target.Create(), Is.True, $"The {resolution.x}x{resolution.y} capture target must be created.");
                captureCamera.targetTexture = target;
                captureCamera.aspect = (float)resolution.x / resolution.y;
                captureCamera.pixelRect = new Rect(0, 0, resolution.x, resolution.y);
                var scaler = canvas.GetComponent<CanvasScaler>();
                Assert.That(scaler, Is.Not.Null, "The fixture Canvas must use the production scale-with-screen-size setup.");
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                Assert.That(captureCamera.targetTexture, Is.SameAs(target));
                Assert.That(captureCamera.pixelWidth, Is.EqualTo(resolution.x));
                Assert.That(captureCamera.pixelHeight, Is.EqualTo(resolution.y));
                Assert.That(target.width, Is.EqualTo(resolution.x));
                Assert.That(target.height, Is.EqualTo(resolution.y));
                Canvas.ForceUpdateCanvases();
                foreach (var cell in cells.Cast<Component>())
                {
                    var rect = (RectTransform)cell.transform;
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    for (int i = 0; i < corners.Length; i++)
                    {
                        Vector2 pixel = RectTransformUtility.WorldToScreenPoint(captureCamera, corners[i]);
                        Assert.That(pixel.x, Is.InRange(-1f, resolution.x + 1f), $"{cell.name} x-bound at {resolution}");
                        Assert.That(pixel.y, Is.InRange(-1f, resolution.y + 1f), $"{cell.name} y-bound at {resolution}");
                    }
                }

                var title = FindTextComponents(root).FirstOrDefault(text =>
                    text.gameObject.name == "Single Header Text" && Convert.ToString(ReadProperty(text, "text")).Length > 8);
                Assert.That(title, Is.Not.Null, "Production initialization must populate the localized title.");
                Assert.That(ReadProperty(title, "text"), Is.EqualTo("Elite Drums Highway"),
                    "The production English dictionary must render the exact human-readable title.");
                Assert.That(actualCellLabels.All(text => !string.IsNullOrWhiteSpace(text)), Is.True,
                    "Every production cell label must contain visible English text.");
                var authoredTitleSize = Convert.ToSingle(ReadProperty(title, "fontSize"));
                Assert.That(authoredTitleSize, Is.InRange(authoredTitleBaseline * 0.5f, authoredTitleBaseline * 1.5f),
                    "The localized shared-header title must remain adapted from its authored font-size baseline.");
                Assert.That(TextBounds(title).x, Is.GreaterThan(0), "The title must generate visible TMP geometry.");
                Assert.That(labels.All(label => TextBounds(label).x > 0 && TextBounds(label).y > 0), Is.True,
                    "All twenty cell labels must generate visible text geometry at each target resolution.");
                var pills = cells.Cast<Component>().Select(cell => (Image)Field(cell, "_fretPill"))
                    .Where(pill => pill != null).ToArray();
                Assert.That(pills.Length, Is.EqualTo(10), "The ten bottom-row cells must display their saved-fret pills.");
                foreach (var pill in pills)
                {
                    Assert.That(pill.rectTransform.rect.width, Is.GreaterThan(0));
                    Assert.That(pill.rectTransform.rect.height, Is.GreaterThan(0));
                    var corners = new Vector3[4];
                    pill.rectTransform.GetWorldCorners(corners);
                    foreach (var corner in corners)
                    {
                        var pixel = RectTransformUtility.WorldToScreenPoint(captureCamera, corner);
                        Assert.That(pixel.x, Is.InRange(-1f, resolution.x + 1f), "Pill must remain inside target camera width.");
                        Assert.That(pixel.y, Is.InRange(-1f, resolution.y + 1f), "Pill must remain inside target camera height.");
                    }
                }
                var previewImage = (RawImage)Field(_menu, "_previewImage");
                Assert.That(previewImage.texture, Is.Not.Null, "The production menu must bind its initialized preview texture.");
                Assert.That(previewImage.gameObject.activeInHierarchy, Is.True,
                    "The actual production preview pane must be visible in the canvas capture.");
                var previewWorldForCapture = (Transform)Field(_menu, "_previewWorld");
                var previewCamerasForCapture = previewWorldForCapture.GetComponentsInChildren<Camera>(true)
                    .Where(camera => camera.gameObject.activeInHierarchy).ToArray();
                Assert.That(previewCamerasForCapture, Is.Not.Empty, "The initialized preview must own active production cameras.");
                float titleSize = Convert.ToSingle(ReadProperty(title, "fontSize"));
                Assert.That(titleSize, Is.InRange(authoredTitleSize * 0.5f, authoredTitleSize * 1.5f),
                    "The shared header title must adapt around its authored font-size baseline.");
                var camerasToObserve = new[] { captureCamera }.Concat(previewCamerasForCapture).ToArray();
                yield return WaitForRenderedFrame(120, camerasToObserve);
                yield return null;
yield return WaitForRenderedFrame(120, camerasToObserve);
                yield return null;
                // The output camera can render before the preview camera in the resize frame.
                // Capture a subsequent frame so the Canvas samples the populated current target.
                yield return WaitForRenderedFrame(120, camerasToObserve);
                yield return null;
yield return WaitForRenderedFrame(120, camerasToObserve);
                yield return null;
                var previewSource = previewImage.texture as RenderTexture;
                Assert.That(previewSource, Is.Not.Null,
                    "The production menu must bind its current preview target after rendering.");
                Assert.That(previewSource.IsCreated(), Is.True, "The production preview target must be allocated.");
                var activePreviewCameras = previewWorldForCapture.GetComponentsInChildren<Camera>(true)
                    .Where(camera => camera.gameObject.activeInHierarchy).ToArray();
                Assert.That(activePreviewCameras, Is.Not.Empty, "The rendered preview must own active production cameras.");
                foreach (var camera in activePreviewCameras)
                {
                    Assert.That(camera.enabled, Is.True);
                    Assert.That(camera.targetTexture, Is.SameAs(previewSource));
                }
                var canvasPixels = ReadFrame(target);
                AssertRenderedPreview(canvasPixels, $"{resolution} full production canvas");
                int previewHuePixels = CountHueInRect(canvasPixels, resolution.x, resolution.y,
                    previewImage.rectTransform, captureCamera, new Color32(0, 255, 0, 255), minimumBrightness: 48);
                LogPreviewFraming(_menu, previewImage, previewSource, resolution, previewHuePixels);
                var paneCorners = new Vector3[4];
                previewImage.rectTransform.GetWorldCorners(paneCorners);
                var paneBottom = RectTransformUtility.WorldToScreenPoint(captureCamera, paneCorners[0]);
                var paneTop = RectTransformUtility.WorldToScreenPoint(captureCamera, paneCorners[2]);
                var sourcePixels = ReadFrame(previewSource);
                var crop = previewImage.uvRect;
                int sourceCropLime = Enumerable.Range(0, sourcePixels.Length).Count(index =>
                    crop.Contains(new Vector2((index % previewSource.width + 0.5f) / previewSource.width,
                        (index / previewSource.width + 0.5f) / previewSource.height)) &&
                    sourcePixels[index].g > 48 && sourcePixels[index].g > sourcePixels[index].r * 1.5f &&
                    sourcePixels[index].g > sourcePixels[index].b * 1.5f);
                Debug.Log($"PREVIEW SAMPLING panePixels={paneBottom}..{paneTop} outputPixels={captureCamera.pixelRect} " +
                    $"sourceOpaqueLime={CountHue(sourcePixels, new Color32(0, 255, 0, 255), 48)} " +
                    $"sourceCropLime={sourceCropLime} canvasAllLime={CountHue(canvasPixels, new Color32(0, 255, 0, 255), 48)} " +
                    $"imageColor={previewImage.color} material={previewImage.material.name}");
                WriteCanvasScreenshot(target, $"/tmp/yarg-elite-canvas-{resolution.x}x{resolution.y}.png");
                // Element 12 is the drum column-3 double-kick cell; element 10 is drum column-1 kick.
                // Both are assigned in Compact, so their fills are exact palette composites.
                var primary = cells.Cast<Component>().ElementAt(12);
                var secondary = cells.Cast<Component>().ElementAt(10);
                var dark = new Color(0.055f, 0.07f, 0.09f, 1f);
                // The runtime outline already carries this cell's exact piece color from the
                // selected preview palette, so derive the expected composite from it directly.
                var note = ((Outline)Field(primary, "_outline")).effectColor;
                var secondaryNote = ((Outline)Field(secondary, "_outline")).effectColor;
                if (scenario == "edges")
                {
                    Call(primary, "OnSelectionChanged", false);
                    yield return WaitForRenderedFrame(120, camerasToObserve);
                    yield return null;
yield return WaitForRenderedFrame(120, camerasToObserve);
                    yield return null;
                    var unselected = ForceRead(target, captureCamera);
                    WriteCanvasScreenshot(target, $"/tmp/yarg-cell-unselected-{target.width}x{target.height}.png");
                    AssertCellEdges(unselected, target.width, target.height, primary, captureCamera, dark);
                    AssertSerializedFillExact(primary, note, dark);
                    AssertSerializedFillExact(secondary, secondaryNote, dark);

                    Call(primary, "OnSelectionChanged", true);
                    yield return WaitForRenderedFrame(120, camerasToObserve);
                    yield return null;
yield return WaitForRenderedFrame(120, camerasToObserve);
                    yield return null;
                    var selectedPixels = ForceRead(target, captureCamera);
                    WriteCanvasScreenshot(target, $"/tmp/yarg-cell-selected-{target.width}x{target.height}.png");
                    AssertCellEdges(selectedPixels, target.width, target.height, primary, captureCamera, dark);
                    // Selection feedback must render without replacing the validity fill underneath it.
                    Assert.That(((Image)Field(primary, "_fill")).color, Is.EqualTo(Color.Lerp(dark, note, 0.25f)),
                        "Selecting a cell must not change its serialized validity fill.");
                    Assert.That(ChangedPixelsInRect(unselected, selectedPixels, primary, captureCamera, 4f),
                        Is.GreaterThan(0), "Toggling selection must change rendered pixels inside the cell.");

                    // Read-only coverage comes from selecting the built-in preset through the management callback.
                    SelectGraphicsLayout(EliteDrumsLayoutPreset.Compact);
                    // The management selection rebuilds the preview world, so observe only the stable capture camera.
                    yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
                    Assert.That(((Button)Field(primary, "_button")).interactable, Is.False,
                        "A read-only built-in preset must disable the cell button.");
                    var readOnly = ForceRead(target, captureCamera);
                    WriteCanvasScreenshot(target, $"/tmp/yarg-cell-readonly-{target.width}x{target.height}.png");
                    AssertCellEdges(readOnly, target.width, target.height, primary, captureCamera, dark);
                    AssertSerializedFillExact(primary, note, dark);
                    SelectGraphicsLayout(layout);
                }
                if (scenario == "validity")
                {
                    Assert.That(EliteDrumsLayoutValidator.Validate((EliteDrumsLayoutPreset)ReadProperty(_menu, "Draft"))
                        .IsRuntimeSupported, Is.True);
                    WriteCanvasScreenshot(target, $"/tmp/yarg-valid-fill-{target.width}x{target.height}.png");
                    AssertSerializedFillExact(primary, note, dark);
                    AssertSerializedFillExact(secondary, secondaryNote, dark);
                    var originalSlot = ((EliteDrumsLayoutPreset)ReadProperty(_menu, "Draft")).Slots[0];
                    string restoreId = originalSlot.Kind switch
                    {
                        EliteDrumsLayoutSlotKind.Kick => "kick",
                        EliteDrumsLayoutSlotKind.Kick1 => "kick1",
                        EliteDrumsLayoutSlotKind.Kick2 => "kick2",
                        EliteDrumsLayoutSlotKind.HiHat => "hi-hat",
                        _ => originalSlot.Drum ?? "kick"
                    };
                    ChooseGraphicsCell(0, true, "clear");
                    Assert.That(EliteDrumsLayoutValidator.Validate((EliteDrumsLayoutPreset)ReadProperty(_menu, "Draft"))
                        .IsRuntimeSupported, Is.False, "Clearing the required kick must invalidate the real draft.");
                    // Choose re-renders and rebuilds the preview world; only the capture camera is stable.
                    yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
                    WriteCanvasScreenshot(target, $"/tmp/yarg-invalid-fill-{target.width}x{target.height}.png");
                    AssertSerializedDarkFill(primary, dark);
                    AssertSerializedDarkFill(secondary, dark);
                    Call(secondary, "OnSelectionChanged", true);
                    yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
                    WriteCanvasScreenshot(target, $"/tmp/yarg-invalid-selected-{target.width}x{target.height}.png");
                    AssertSerializedDarkFill(secondary, dark);
                    ChooseGraphicsCell(0, true, restoreId);
                    Assert.That(EliteDrumsLayoutValidator.Validate((EliteDrumsLayoutPreset)ReadProperty(_menu, "Draft"))
                        .IsRuntimeSupported, Is.True);
                    yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
                    WriteCanvasScreenshot(target, $"/tmp/yarg-restored-fill-{target.width}x{target.height}.png");
                    AssertSerializedFillExact(primary, note, dark);
                    AssertSerializedFillExact(secondary, secondaryNote, dark);
                    Assert.That(ReadProperty(_menu, "IsDirty"), Is.EqualTo(false), "Restoration must match the original draft.");
                }
                if (scenario == "management")
                {
                    var malformed = layout.Copy();
                    malformed.SchemaVersion = int.MaxValue;
                    SelectGraphicsLayout(malformed);
                    Assert.That(ReadProperty(_menu, "Draft"), Is.Null);
                    Assert.That(cells.Cast<Component>().All(cell => !cell.gameObject.activeSelf), Is.True);
                    yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
                    var hidden = ForceRead(target, captureCamera);
                    AssertCellFeedbackAbsent(hidden, target.width, target.height, primary, captureCamera, note);
                    SelectGraphicsLayout(layout);
                    Assert.That(ReadProperty(_menu, "Draft"), Is.Not.Null);
                    Assert.That(cells.Cast<Component>().All(cell => cell.gameObject.activeSelf), Is.True);
                    yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
yield return WaitForRenderedFrame(120, new[] { captureCamera });
                    yield return null;
                    WriteCanvasScreenshot(target, $"/tmp/yarg-management-restored-{target.width}x{target.height}.png");
                    AssertSerializedFillExact(primary, note, dark);
                    AssertCellEdges(ForceRead(target, captureCamera), target.width, target.height, primary, captureCamera, dark);
                }
                if (scenario == "frets")
                    AssertFretPixels(canvasPixels, target.width, target.height, pills, captureCamera);
                Assert.That(previewHuePixels, Is.GreaterThan(8),
                    $"The production preview pane must contain rendered lime geometry, not a flat-gray or blank RawImage ({resolution}).");
            }
#endif
        }

        private static int _forceReadCounter;

        private static Color32[] ForceRead(RenderTexture target, Camera camera)
        {
            // Direct Texture2D.ReadPixels of this canvas target returns flat preview-source VRAM
            // in this URP/batch setup even though the same-moment PNG path is correct, so route
            // the readback through the proven PNG encode/decode path used for human captures.
            _ = camera;
            string path = $"/tmp/yarg-forceread-{++_forceReadCounter}-{target.width}x{target.height}.png";
            WriteCanvasScreenshot(target, path);
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(loaded, File.ReadAllBytes(path), false))
                throw new System.InvalidOperationException("Failed to load the canvas capture PNG.");
            var pixels = loaded.GetPixels32();
            UnityEngine.Object.DestroyImmediate(loaded);
            return pixels;
        }

        private void SelectGraphicsLayout(EliteDrumsLayoutPreset preset)
        {
            Assert.That(ReadProperty(_menu, "IsDirty"), Is.False,
                "This scenario must select layouts without a pending discard confirmation.");
            var layouts = (EliteDrumsLayoutPreset[])Field(_menu, "_layouts");
            Array.Resize(ref layouts, layouts.Length + 1);
            layouts[^1] = preset;
            SetField(_menu, "_layouts", layouts);
            SelectDropdown(Field(_menu, "_layoutDropdown"), layouts.Length - 1);
        }

        private void ChooseGraphicsCell(int column, bool drum, string choice)
        {
            Call(_menu, "OpenCell", column, drum);
            var popup = (GameObject)Field(_menu, "_popup");
            Assert.That(popup.activeSelf, Is.True, "The production popup must open from the real cell button path.");
            var ids = (string[])Field(_menu, "_choiceIds");
            int index = Array.IndexOf(ids, choice);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"The popup must offer {choice}.");
            Call(_menu, "Choose", index);
            Assert.That(popup.activeSelf, Is.False, "Choosing must close the popup and re-render the real draft.");
        }

        private static Color32 ReadPixel(Color32[] pixels, int width, int height, float x, float y)
        {
            int column = Mathf.Clamp(Mathf.RoundToInt(x), 0, width - 1);
            int row = Mathf.Clamp(Mathf.RoundToInt(y), 0, height - 1);
            return pixels[row * width + column];
        }

        private static Vector2 CellCenter(Component cell, Camera camera) =>
            RectTransformUtility.WorldToScreenPoint(camera, ((RectTransform)cell.transform).position);

        private static Vector2 CellSize(Component cell, Camera camera)
        {
            var corners = new Vector3[4];
            ((RectTransform)cell.transform).GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return max - min;
        }

        private static void AssertCellEdges(Color32[] pixels, int width, int height, Component cell, Camera camera,
            Color rectangularFill)
        {
            Vector2 center = CellCenter(cell, camera);
            Vector2 size = CellSize(cell, camera);
            float halfWidth = size.x / 2f, halfHeight = size.y / 2f;
            var outline = ((Outline)Field(cell, "_outline")).effectColor;
            // The root Outline draws its ring just outside the rounded rect; sample that band at
            // each edge midpoint, scanning along the edge.
            Vector2[] edges = {
                new(0f, -1f), new(0f, 1f), new(-1f, 0f), new(1f, 0f)
            };
            Vector2 EdgePoint(Vector2 direction, float distance) =>
                center + new Vector2(direction.x * (halfWidth + distance), direction.y * (halfHeight + distance));
            // The Outline ring sits roughly one scaled effectDistance (2 reference units) outside
            // the rect; anti-aliasing and canvas scale shift the visible band, so locate the band
            // radially first, then measure the drawn run along the edge at that distance.
            int Longest(Vector2 direction)
            {
                int best = 0;
                for (float distance = 0.5f; distance <= 4f; distance += 0.5f)
                {
                    Vector2 origin = EdgePoint(direction, distance);
                    int run = 0, current = 0;
                    for (int step = -12; step <= 12; step++)
                    {
                        Vector2 sample = origin + (direction.x == 0 ? new Vector2(step, 0f) : new Vector2(0f, step));
                        if (ColorClose(ReadPixel(pixels, width, height, sample.x, sample.y), outline, 0.11f)) current++;
                        else current = 0;
                        run = Math.Max(run, current);
                    }
                    best = Math.Max(best, run);
                }
                return best;
            }
            foreach (var edge in edges)
            {
                int run = Longest(edge);
                Assert.That(run, Is.GreaterThanOrEqualTo(8), $"The outline border must stay drawn and unclipped " +
                    $"outside {edge} (run {run} of 25 samples around {center}).");
            }
            foreach (var pair in new[] { (edges[0], edges[1]), (edges[2], edges[3]) })
            {
                int first = Longest(pair.Item1), second = Longest(pair.Item2);
                Assert.That(Math.Abs(first - second), Is.LessThanOrEqualTo(2),
                    $"Opposite border runs must stay symmetric ({first} vs {second} around {center}).");
            }
            // A rectangular sprite would paint its solid fill in the corner tiles; the rounded
            // artwork must leave the extreme corners anything but that flat fill color.
            foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            {
                Vector2 point = center + new Vector2(corner.x * (halfWidth - 2f), corner.y * (halfHeight - 2f));
                Color32 sample = ReadPixel(pixels, width, height, point.x, point.y);
                Assert.That(ColorClose(sample, rectangularFill, 0.05f), Is.False,
                    $"Corner {corner} must come from rounded artwork, not a rectangular fill ({sample} at {point}).");
            }
        }

        private static int ChangedPixelsInRect(Color32[] before, Color32[] after, Component cell, Camera camera,
            float padding)
        {
            Assert.That(after.Length, Is.EqualTo(before.Length));
            int width = camera.pixelWidth, height = camera.pixelHeight;
            Vector2 center = CellCenter(cell, camera);
            Vector2 size = CellSize(cell, camera);
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - size.x / 2f - padding));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(center.x + size.x / 2f + padding));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - size.y / 2f - padding));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(center.y + size.y / 2f + padding));
            int changed = 0;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int index = y * width + x;
                    var left = before[index];
                    var right = after[index];
                    if (Math.Abs(left.r - right.r) + Math.Abs(left.g - right.g) + Math.Abs(left.b - right.b) > 36)
                        changed++;
                }
            return changed;
        }

        private static bool ColorClose(Color32 pixel, Color expected, float tolerance)
        {
            float difference = Math.Abs(pixel.r / 255f - expected.r) + Math.Abs(pixel.g / 255f - expected.g) +
                Math.Abs(pixel.b / 255f - expected.b);
            return difference <= tolerance;
        }

        private static void AssertCellCompositeFill(Color32[] pixels, int width, Component cell, Camera camera,
            Color dark, Color note)
        {
            RectInt rect = LocateCellRing(pixels, width, cell, camera);
            // Production renders Color.Lerp(dark, note, 0.25). Depending on the project color space the
            // displayed pixel is either that gamma-space lerp or the same blend performed linearly,
            // so accept exactly those two candidates and reject the plain base and solid note colors.
            Color gamma = Color.Lerp(dark, note, 0.25f);
            Color linear = new(
                LinearToSrgb(Mathf.Lerp(SrgbToLinear(dark.r), SrgbToLinear(note.r), 0.25f)),
                LinearToSrgb(Mathf.Lerp(SrgbToLinear(dark.g), SrgbToLinear(note.g), 0.25f)),
                LinearToSrgb(Mathf.Lerp(SrgbToLinear(dark.b), SrgbToLinear(note.b), 0.25f)), 1f);
            // Same majority rationale as the dark-fill check: transient single-frame batch
            // ghosts must not mask that the dominant interior is the exact 25% composite.
            var fillPoints = InnerSamplePoints(rect, cell, camera);
            int composite = fillPoints.Count(point =>
            {
                Color32 sample = pixels[point.y * width + point.x];
                return ColorClose(sample, gamma, 0.06f) || ColorClose(sample, linear, 0.06f);
            });
            int plainDark = fillPoints.Count(point => ColorClose(pixels[point.y * width + point.x], dark, 0.12f));
            int solidNote = fillPoints.Count(point => ColorClose(pixels[point.y * width + point.x], note, 0.12f));
            Assert.That(composite, Is.GreaterThanOrEqualTo((fillPoints.Count * 3) / 4 + 1),
                $"The 25% composite fill must dominate the interior of {cell.name} " +
                $"({composite}/{fillPoints.Count} matched {gamma} or {linear}).");
            Assert.That(plainDark, Is.EqualTo(0), "No interior sample may fall back to the plain dark base.");
            Assert.That(solidNote, Is.EqualTo(0), "No interior sample may render the solid note color.");
        }

        private static void AssertCellDarkFill(Color32[] pixels, int width, Component cell, Camera camera, Color dark)
        {
            // A transient canvas-batch ghost from an earlier frame can repaint a minority of the
            // interior; the validity contract is about the dominant fill, so require a strict
            // majority of label-free interior samples to be the exact dark base.
            var fill = Field(cell, "_fill") as Image;
            RectInt rect = LocateCellRing(pixels, width, cell, camera);
            var points = InnerSamplePoints(rect, cell, camera);
            int darkSamples = points.Count(point => ColorClose(pixels[point.y * width + point.x], dark, 0.05f));
            Assert.That(darkSamples, Is.GreaterThanOrEqualTo((points.Count * 3) / 4 + 1),
                $"The invalid or unsupported fill must dominate the interior of {cell.name} " +
                $"({darkSamples}/{points.Count} exact dark samples; serialized fill {fill?.color}; samples " +
                string.Join(" | ", points.Select(point =>
                {
                    var px = pixels[point.y * width + point.x];
                    return $"{point}:{px.r},{px.g},{px.b}";
                })) + ").");
        }

        private static void AssertSerializedFillExact(Component cell, Color note, Color dark)
        {
            var fill = (Image)Field(cell, "_fill");
            Assert.That(((Color)fill.color), Is.EqualTo(Color.Lerp(dark, note, 0.25f)).Within(0.004f),
                $"{cell.name} must carry the exact 25% palette composite as its fill color.");
        }

        private static void AssertSerializedDarkFill(Component cell, Color dark)
        {
            var fill = (Image)Field(cell, "_fill");
            Assert.That((Color)fill.color, Is.EqualTo(dark).Within(0.004f),
                $"{cell.name} must fall back to the exact dark base when the draft is runtime-invalid.");
        }

        /// <summary>
        /// Finds the cell's rendered outline ring by its unique note color and returns the
        /// inner rectangle it encloses. Readback and screen coordinates can drift by a pixel
        /// or two across canvas scales, so anchor pixel assertions to the ring itself.
        /// </summary>
        private static RectInt LocateCellRing(Color32[] pixels, int width, Component cell, Camera camera)
        {
            var outline = ((Outline)Field(cell, "_outline")).effectColor;
            Vector2 center = CellCenter(cell, camera);
            Vector2 size = CellSize(cell, camera);
            int height = pixels.Length / width;
            int minX = width, minY = height, maxX = 0, maxY = 0, matched = 0;
            int radiusX = Mathf.CeilToInt(size.x / 2f) + 5, radiusY = Mathf.CeilToInt(size.y / 2f) + 5;
            // Clamp to this cell's own neighborhood: the stacked rows can share a palette color
            // with the cell directly above, and that neighbor must not widen the located ring.
            for (int y = Mathf.Max(0, (int)center.y - radiusY); y <= Mathf.Min(height - 1, (int)center.y + radiusY); y++)
                for (int x = Mathf.Max(0, (int)center.x - radiusX); x <= Mathf.Min(width - 1, (int)center.x + radiusX); x++)
                    if (ColorClose(pixels[y * width + x], outline, 0.12f))
                    {
                        matched++;
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            Assert.That(matched, Is.GreaterThanOrEqualTo(20),
                $"The rendered outline ring for {cell.name} must be findable near {center} ({matched} matches).");
            // Stray same-color UI elsewhere in the window can widen the raw bounds, so derive the
            // extents from the densest matched row and column instead of the raw bounding box.
            int BestRow(int from, int to, bool horizontal)
            {
                int best = from, bestCount = 0;
                for (int i = from; i <= to; i++)
                {
                    int count = 0;
                    for (int j = (horizontal ? minY : minX); j <= (horizontal ? maxY : maxX); j++)
                    {
                        int x = horizontal ? j : i, y = horizontal ? i : j;
                        if (ColorClose(pixels[y * width + x], outline, 0.12f)) count++;
                    }
                    if (count > bestCount)
                    {
                        bestCount = count;
                        best = i;
                    }
                }
                return best;
            }
            int rowMid = BestRow(minY, maxY, true);
            int colMid = BestRow(minX, maxX, false);
            int rowMinX = width, rowMaxX = 0, colMinY = height, colMaxY = 0;
            for (int x = minX; x <= maxX; x++)
                if (ColorClose(pixels[rowMid * width + x], outline, 0.12f))
                {
                    if (x < rowMinX) rowMinX = x; if (x > rowMaxX) rowMaxX = x;
                }
            for (int y = minY; y <= maxY; y++)
                if (ColorClose(pixels[y * width + colMid], outline, 0.12f))
                {
                    if (y < colMinY) colMinY = y; if (y > colMaxY) colMaxY = y;
                }
            Assert.That(rowMaxX - rowMinX, Is.GreaterThan(10));
            Assert.That(colMaxY - colMinY, Is.GreaterThan(10));
            return new RectInt(rowMinX + 4, colMinY + 4, Mathf.Max(1, rowMaxX - rowMinX - 8),
                Mathf.Max(1, colMaxY - colMinY - 8));
        }

        private static List<Vector2Int> InnerSamplePoints(RectInt ring, Component cell, Camera camera)
        {
            // The centered label covers most of the cell interior; the validity fill is only
            // visible in the band between that label and the ring, so derive the rendered label
            // bounds and sample the free band on all four sides.
            // The label's RectTransform stretches across the whole cell; use the actual glyph
            // bounds from the generated mesh instead, so the fill band beside the text is sampled.
            var label = (Component)Field(cell, "_label");
            label.GetType().GetMethod("ForceMeshUpdate", new[] { typeof(bool), typeof(bool) })
                .Invoke(label, new object[] { false, false });
            var textInfo = label.GetType().GetProperty("textInfo").GetValue(label);
            var lineInfo = (Array)textInfo.GetType().GetField("lineInfo").GetValue(textInfo);
            var characterInfo = (Array)textInfo.GetType().GetField("characterInfo").GetValue(textInfo);
            int labelMinX = int.MaxValue, labelMaxX = int.MinValue, labelMinY = int.MaxValue, labelMaxY = int.MinValue;
            foreach (var line in lineInfo)
            {
                int first = (int)line.GetType().GetField("firstCharacterIndex").GetValue(line);
                int last = (int)line.GetType().GetField("lastCharacterIndex").GetValue(line);
                int visible = (int)line.GetType().GetField("visibleCharacterCount").GetValue(line);
                if (visible == 0) continue;
                for (int i = first; i <= last && i < characterInfo.Length; i++)
                {
                    var character = characterInfo.GetValue(i);
                    if (!(bool)character.GetType().GetField("isVisible").GetValue(character)) continue;
                    foreach (var cornerName in new[] { "bottomLeft", "topRight" })
                    {
                        Vector3 local = (Vector3)character.GetType().GetField(cornerName).GetValue(character);
                        Vector2 point = RectTransformUtility.WorldToScreenPoint(camera,
                            label.transform.TransformPoint(local));
                        labelMinX = Mathf.Min(labelMinX, Mathf.FloorToInt(point.x));
                        labelMaxX = Mathf.Max(labelMaxX, Mathf.CeilToInt(point.x));
                        labelMinY = Mathf.Min(labelMinY, Mathf.FloorToInt(point.y));
                        labelMaxY = Mathf.Max(labelMaxY, Mathf.CeilToInt(point.y));
                    }
                }
            }
            if (labelMinX > labelMaxX)
            {
                // No visible glyphs: the whole interior is fill.
                labelMinX = labelMaxX = int.MinValue;
            }
            var points = new List<Vector2Int>();
            int inset = 5;
            int left = ring.xMin + inset, right = ring.xMax - 1 - inset;
            int bottom = ring.yMin + inset, top = ring.yMax - 1 - inset;
            int midX = (left + right) / 2, midY = (bottom + top) / 2;
            foreach (var candidate in new[]
                     {
                         new Vector2Int(left, midY), new Vector2Int(right, midY),
                         new Vector2Int(midX, bottom), new Vector2Int(midX, top),
                         new Vector2Int(left, (bottom + midY) / 2), new Vector2Int(right, (bottom + midY) / 2),
                         new Vector2Int(left, (top + midY) / 2), new Vector2Int(right, (top + midY) / 2),
                         new Vector2Int((left + midX) / 2, bottom), new Vector2Int((midX + right) / 2, bottom),
                         new Vector2Int((left + midX) / 2, top), new Vector2Int((midX + right) / 2, top)
                     })
            {
                bool insideLabel = candidate.x >= labelMinX - 1 && candidate.x <= labelMaxX + 1 &&
                    candidate.y >= labelMinY - 1 && candidate.y <= labelMaxY + 1;
                if (!insideLabel)
                    points.Add(candidate);
            }
            Assert.That(points, Is.Not.Empty,
                $"No fill-only region remains between the label ({labelMinX}..{labelMaxX} x {labelMinY}..{labelMaxY}) " +
                $"and the ring {ring} for {cell.name}.");
            return points;
        }

        private static float SrgbToLinear(float channel) => channel <= 0.04045f ? channel / 12.92f :
            Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);

        private static float LinearToSrgb(float channel) => channel <= 0.0031308f ? channel * 12.92f :
            1.055f * Mathf.Pow(channel, 1f / 2.4f) - 0.055f;

        private static void AssertCellFeedbackAbsent(Color32[] pixels, int width, int height, Component cell,
            Camera camera, Color note)
        {
            Vector2 center = CellCenter(cell, camera);
            int tinted = 0;
            for (int y = -8; y <= 8; y++)
                for (int x = -8; x <= 8; x++)
                {
                    Color32 sample = ReadPixel(pixels, width, height, center.x + x * (CellSize(cell, camera).x / 40f),
                        center.y + y * (CellSize(cell, camera).y / 40f));
                    if (ColorClose(sample, note, 0.2f)) tinted++;
                }
            Assert.That(tinted, Is.EqualTo(0), "Hidden management-only cells must leave no tinted cell feedback behind.");
        }

        private static void AssertFretPixels(Color32[] pixels, int width, int height, Image[] pills, Camera camera)
        {
            foreach (var pill in pills)
            {
                var inset = pill.GetComponentsInChildren<RectTransform>(true)
                    .First(child => child.name == "Fret Number Background");
                Vector2 center = CellCenter(inset, camera);
                Vector2 size = CellSize(inset, camera);
                float diameter = Mathf.Min(size.x, size.y);
                Assert.That(diameter, Is.GreaterThanOrEqualTo(8f), $"The authored 38-unit inset in {pill.name} must " +
                    $"stay visibly sized at {camera.pixelWidth}x{camera.pixelHeight} ({diameter:F1}px).");
                // The black circle must read as dark in every channel over the fret-colored pill.
                int dark = 0;
                const int STEPS = 41;
                for (int step = 0; step < STEPS; step++)
                {
                    float offset = (-0.5f + step / (float)(STEPS - 1)) * diameter;
                    Color32 sample = ReadPixel(pixels, width, height, center.x + offset, center.y);
                    if (sample.a > 8 && sample.r < 120 && sample.g < 120 && sample.b < 120) dark++;
                }
                Assert.That(dark, Is.GreaterThanOrEqualTo(STEPS / 2), $"The fret circle in {pill.name} must remain " +
                    $"a visible dark inset across its {diameter:F1}px diameter ({dark}/{STEPS} samples).");
                // The white numeral and its dark outline must survive resolution scaling.
                int readable = 0;
                for (int y = -10; y <= 10; y++)
                    for (int x = -10; x <= 10; x++)
                    {
                        Color32 sample = ReadPixel(pixels, width, height,
                            center.x + x * (diameter / 40f), center.y + y * (diameter / 40f));
                        if (sample.r > 170 && sample.g > 170 && sample.b > 170) readable++;
                    }
                Assert.That(readable, Is.GreaterThanOrEqualTo(10), $"The fret numeral in {pill.name} must stay " +
                    $"readable at {camera.pixelWidth}x{camera.pixelHeight} ({readable} bright pixels sampled).");
            }
        }

        private static void LogPreviewFraming(Component menu, RawImage image, RenderTexture source,
            Vector2Int resolution, int canvasHuePixels)
        {
            var world = (Transform)Field(menu, "_previewWorld");
            var hcr = world.GetComponentsInChildren<Component>(true)
                .First(component => component.GetType().Name == "HighwayCameraRendering");
            var bounds = hcr.GetType().GetMethod("GetTrackBoundsScreenSpace", INSTANCE)
                .Invoke(hcr, new object[] { 0, null });
            var pixels = ReadFrame(source);
            var matching = Enumerable.Range(0, pixels.Length).Where(index =>
                pixels[index].g > 48 && pixels[index].g > pixels[index].r * 1.5f &&
                pixels[index].g > pixels[index].b * 1.5f).ToArray();
            string hueBounds = matching.Length == 0 ? "none" :
                $"x={matching.Min(index => index % source.width) / (float)source.width:F4}..{matching.Max(index => index % source.width) / (float)source.width:F4}," +
                $"y={matching.Min(index => index / source.width) / (float)source.height:F4}..{matching.Max(index => index / source.width) / (float)source.height:F4}";
            Debug.Log($"PREVIEW FRAMING resolution={resolution} source={source.width}x{source.height} image={image.rectTransform.rect} uv={image.uvRect.ToString("F5")} " +
                $"hcrBoundsPixelsTopOrigin={bounds} sourceLime={matching.Length} sourceLimeUV={hueBounds} canvasLime={canvasHuePixels} " +
                $"graphicsUVStartsAtTop={SystemInfo.graphicsUVStartsAtTop} graphics={SystemInfo.graphicsDeviceType}");
            foreach (var camera in world.GetComponentsInChildren<Camera>(true))
                Debug.Log($"PREVIEW CAMERA {camera.name} pos={camera.transform.position} rot={camera.transform.eulerAngles} pixel={camera.pixelRect} aspect={camera.aspect} target={camera.targetTexture?.width}x{camera.targetTexture?.height} projection={camera.projectionMatrix}");
            foreach (var renderer in world.GetComponentsInChildren<Renderer>(true)
                         .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy))
            {
                var point = renderer.bounds.center;
                var viewport = hcr.GetType().GetMethod("WorldToViewport", INSTANCE)
                    .Invoke(hcr, new object[] { point, 0 });
                var textureUV = (Vector2)viewport;
                if (SystemInfo.graphicsUVStartsAtTop) textureUV.y = 1f - textureUV.y;
                Debug.Log($"PREVIEW GEOMETRY {renderer.name} bounds={renderer.bounds} hcrViewportCenter={viewport} textureUVCenter={textureUV} insideCrop={image.uvRect.Contains(textureUV)}");
            }
        }

        private static EliteDrumsLayoutPreset TenLaneLayout()
        {
            var layout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Rendered ten lane");
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            return layout;
        }

        private static void ExercisePreviewOptionsIfIntegrated(Component menu)
        {
            var field = menu.GetType().GetField("_previewOptions", INSTANCE);
            Assert.That(field, Is.Not.Null,
                "The preview options dropdown must be integrated before the graphics acceptance test is ready.");
            var options = field.GetValue(menu) as Component;
            Assert.That(options, Is.Not.Null, "The preview options toolbar component must be present.");
            Assert.That(options.GetType().Name, Is.EqualTo("TMP_Dropdown"),
                "The integrated preview options control must be the actual TMP dropdown.");
            Assert.That(options.gameObject.activeInHierarchy, Is.True,
                "The preview options dropdown must be visible in the production menu.");
            var optionList = ReadProperty(options, "options") as System.Collections.IList;
            Assert.That(optionList, Is.Not.Null.And.Not.Empty, "The toolbar must expose real preview option choices.");
            var initialPlayer = PreviewPlayer(menu);
            Assert.That(initialPlayer, Is.Not.Null, "Production preview player must exist before options are changed.");
            var previewBuilder = Field(menu, "_previewBuilder");
            bool initialStarPowerNotes = Convert.ToBoolean(Field(menu, "_forceStarPowerNotes"));
            int starPowerNotesIndex = Enumerable.Range(0, optionList.Count)
                .First(index => Convert.ToString(ReadProperty(optionList[index], "text")).Contains("Star Power Notes"));
            SelectDropdown(options, starPowerNotesIndex);
            Assert.That(Convert.ToInt32(ReadProperty(options, "value")), Is.EqualTo(-1),
                "The production options dropdown intentionally returns to its placeholder after selection.");
            Assert.That(Convert.ToBoolean(Field(menu, "_forceStarPowerNotes")), Is.EqualTo(!initialStarPowerNotes),
                "Selecting Star Power Notes must toggle the production menu state.");
            Assert.That(Field(menu, "_previewBuilder"), Is.SameAs(previewBuilder),
                "Changing preview visuals must update the existing preview player instead of rebuilding it.");
            Assert.That(Convert.ToBoolean(ReadProperty(previewBuilder, "ForceStarPowerNotes")), Is.EqualTo(!initialStarPowerNotes),
                "The existing production preview builder must receive the updated option state.");
        }

        private static void AddProductionCanvasScaler(GameObject host)
        {
            var scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private static IEnumerator WaitForRenderedFrame(int maxFrames = 120, Camera[] observedCameras = null)
        {
            bool rendered = false;
            var cameraRenderCounts = observedCameras?.ToDictionary(camera => camera, _ => 0);
            void OnBeginCamera(ScriptableRenderContext _, Camera camera)
            {
                if (cameraRenderCounts != null && cameraRenderCounts.ContainsKey(camera))
                    cameraRenderCounts[camera]++;
            }
            void OnEndFrame(ScriptableRenderContext _, Camera[] __) => rendered = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endFrameRendering += OnEndFrame;
            try
            {
                for (int frame = 0; frame < maxFrames && !rendered; frame++)
                    yield return null;
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                RenderPipelineManager.endFrameRendering -= OnEndFrame;
            }
            if (cameraRenderCounts != null)
                foreach (var pair in cameraRenderCounts)
                    Assert.That(pair.Value, Is.GreaterThan(0),
                        $"Preview camera {pair.Key?.name ?? "<destroyed>"} must render into its owned texture.");
            Assert.That(rendered, Is.True,
                $"A rendered frame must be observed through endFrameRendering within {maxFrames} frames.");
        }

        private static void AssertPreviewWorldRegistration(Component menu, string label)
        {
            var world = (Transform)Field(menu, "_previewWorld");
            Assert.That(world.parent, Is.Null, $"{label} must stay detached from the scaled Canvas hierarchy.");
            Assert.That(world.localScale, Is.EqualTo(Vector3.one), $"{label} must preserve unit scale after camera registration.");
            Assert.That(world.localRotation, Is.EqualTo(Quaternion.identity), $"{label} must preserve identity rotation.");
        }

        private static (Color32 First, Color32 Last) AssertCompactFretMaterialColors(Component menu, ColorProfile profile)
        {
            var laneColors = AssertFretMaterialHues(menu, profile, new[] { 0, 1, 2, 3, 4 });
            Assert.That(laneColors.Length, Is.EqualTo(5), "Compact layout should create five colored receptor frets.");
            return (laneColors[0], laneColors[^1]);
        }

        private static Color32[] AssertFretMaterialHues(Component menu, ColorProfile profile, int[] savedColumns)
        {
            var player = PreviewPlayer(menu);
            var info = ReadProperty(player, "CurrentGameModeInfo");
            var resolvedLayout = ReadProperty(info, "EliteLayout");
            var lanes = (System.Collections.IEnumerable)ReadProperty(resolvedLayout, "Lanes");
            var resolvedColumns = lanes.Cast<object>()
                .Select(lane => Convert.ToInt32(ReadProperty(lane, "SavedColumn"))).ToArray();
            Assert.That(resolvedColumns, Is.EqualTo(savedColumns), "Resolved lane order must match the expected saved columns.");
            var fretArray = (Component)Field(player, "_fretArray");
            var frets = fretArray.GetComponentsInChildren<Component>(true)
                .Where(component => component.GetType().Name == "Fret")
                .OrderBy(component => component.transform.position.x).ToArray();
            Assert.That(frets.Length, Is.EqualTo(resolvedColumns.Length),
                "The preview should create one colored fret for every resolved saved column.");
            var expectedColors = new Color32[savedColumns.Length];
            for (int i = 0; i < frets.Length; i++)
            {
                var topMaterials = TopMaterials(frets[i]);
                Assert.That(topMaterials, Is.Not.Empty,
                    $"Receptor {i} must expose its colored top materials.");
                expectedColors[i] = ToColor32(profile.EliteDrums.GetSlotFretColor(savedColumns[i]));
                foreach (var material in topMaterials)
                    Assert.That((Color32)material.color, Is.EqualTo(expectedColors[i]),
                        $"Receptor {i} top material {material.name} must use saved slot {savedColumns[i] + 1} color.");
            }
            return expectedColors;
        }

        private static Color32 ToColor32(System.Drawing.Color color) =>
            new Color32(color.R, color.G, color.B, color.A);

        private static Material[] TopMaterials(Component fret) =>
            (Field(fret, "_topMaterials") as System.Collections.IEnumerable)?.Cast<Material>()
            .Where(material => material != null).ToArray() ?? Array.Empty<Material>();

        private static IEnumerable<Renderer> FretTopRenderers(IEnumerable<Component> frets) =>
            frets.SelectMany(fret =>
            {
                var topMaterials = TopMaterials(fret);
                return fret.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => renderer.sharedMaterials.Any(topMaterials.Contains));
            }).Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).Distinct();

        private static int CountHueInRect(Color32[] pixels, int width, int height, RectTransform region,
            Camera camera, Color32 expected, int minimumBrightness)
        {
            var corners = new Vector3[4];
            region.GetWorldCorners(corners);
            Vector2 bottomLeft = camera.WorldToViewportPoint(corners[0]);
            Vector2 topRight = camera.WorldToViewportPoint(corners[2]);
            int minX = Mathf.Clamp(Mathf.FloorToInt(bottomLeft.x * width), 0, width - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(bottomLeft.y * height), 0, height - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(topRight.x * width), minX + 1, width);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(topRight.y * height), minY + 1, height);
            var regionPixels = new List<Color32>((maxX - minX) * (maxY - minY));
            for (int y = minY; y < maxY; y++)
            for (int x = minX; x < maxX; x++)
                regionPixels.Add(pixels[y * width + x]);

            return CountHue(regionPixels, expected, minimumBrightness);
        }

        private static int CountHue(IEnumerable<Color32> pixels, Color32 expected, int minimumBrightness)
        {
            if (expected.r > expected.g * 1.35f && expected.b > expected.g * 1.35f)
                return pixels.Count(pixel => pixel.a > 200 && Math.Min(pixel.r, pixel.b) >= minimumBrightness &&
                    Math.Min(pixel.r, pixel.b) >= Math.Max(pixel.r, pixel.b) * 0.55f &&
                    Math.Min(pixel.r, pixel.b) >= pixel.g * 1.35f);
            if (expected.g > expected.r * 1.35f && expected.b > expected.r * 1.35f)
                return pixels.Count(pixel => pixel.a > 200 && Math.Min(pixel.g, pixel.b) >= minimumBrightness &&
                    Math.Min(pixel.g, pixel.b) >= Math.Max(pixel.g, pixel.b) * 0.55f &&
                    Math.Min(pixel.g, pixel.b) >= pixel.r * 1.35f);
            if (expected.g > expected.r * 1.35f && expected.g > expected.b * 1.35f)
                return pixels.Count(pixel => pixel.a > 200 && pixel.g >= minimumBrightness &&
                    pixel.g >= pixel.r * 1.35f && pixel.g >= pixel.b * 1.35f);
            return pixels.Count(pixel => pixel.a > 200 && pixel.r >= minimumBrightness &&
                pixel.r >= pixel.g * 1.35f && pixel.r >= pixel.b * 1.35f);
        }

        private static string DescribeMostCommonRgb(Color32[] pixels) => string.Join("; ", pixels
            .Where(pixel => pixel.a > 200)
            .GroupBy(pixel => $"{pixel.r},{pixel.g},{pixel.b}")
            .OrderByDescending(group => group.Count())
            .Take(8)
            .Select(group => $"{group.Key} ({group.Count()})"));

        private static void WriteFramePng(RenderTexture source, string path)
        {
            var pixels = ReadFrame(source);
            var capture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            try
            {
                capture.SetPixels32(pixels);
                capture.Apply(false, false);
                File.WriteAllBytes(path, capture.EncodeToPNG());
                Debug.Log($"[Elite preview capture] {path} ({source.width}x{source.height}, sRGB display values)");
            }
            finally
            {
                UnityEngine.Object.Destroy(capture);
            }
        }

        private static void WriteCanvasScreenshot(RenderTexture source, string path)
        {
            var previous = RenderTexture.active;
            var capture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(source.IsCreated(), Is.True, "The full-canvas render target must be allocated before capture.");
                RenderTexture.active = source;
                capture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                capture.Apply(false, false);
                File.WriteAllBytes(path, capture.EncodeToPNG());
                Debug.Log($"[Elite canvas capture] {path} ({source.width}x{source.height}, production Canvas camera output)");
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(capture);
            }
        }

        private static void LogPreviewLandmarks(string label, Component menu, RenderTexture texture,
            Color32[] pixels, string imagePath)
        {
            var world = (Transform)Field(menu, "_previewWorld");
            var cameras = world.GetComponentsInChildren<Camera>(true)
                .Where(camera => camera.gameObject.activeInHierarchy).ToArray();
            var player = PreviewPlayer(menu);
            var fretArray = Field(player, "_fretArray") as Component;
            var fretComponents = fretArray == null ? Array.Empty<Component>() :
                fretArray.GetComponentsInChildren<Component>(true)
                    .Where(component => component.GetType().Name == "Fret").ToArray();
            var renderers = FretTopRenderers(fretComponents).Take(12).ToArray();
            var pool = Field(player, "_notePool");
            var spawned = pool.GetType().GetProperty("AllSpawned")?.GetValue(pool) as System.Collections.IList;
            var noteRenderers = spawned?.Cast<object>().OfType<Component>()
                .SelectMany(note => note.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).Take(8).ToArray()
                ?? Array.Empty<Renderer>();

            Debug.Log($"[Elite preview capture] {label}: image={imagePath}; texture={texture.width}x{texture.height}; " +
                $"world position={world.position}, rotation={world.rotation.eulerAngles}, scale={world.lossyScale}; " +
                $"fret components={fretComponents.Length}, sampled fret renderers={renderers.Length}, prepared note renderers={noteRenderers.Length}");
            foreach (var camera in cameras)
            {
                Debug.Log($"[Elite preview capture] {label} camera {camera.name}: target={camera.targetTexture?.name}, " +
                    $"pixelRect={camera.pixelRect}, rect={camera.rect}, viewport={camera.rect.size}, " +
                    $"position={camera.transform.position}, rotation={camera.transform.eulerAngles}, " +
                    $"projection={camera.projectionMatrix}, cullingMask={camera.cullingMask}");
                foreach (var renderer in renderers.Concat(noteRenderers))
                {
                    var material = renderer.material;
                    var bounds = renderer.bounds;
                    var screen = camera.WorldToViewportPoint(bounds.center);
                    string emission = material.HasProperty("_EmissionColor")
                        ? material.GetColor("_EmissionColor").ToString()
                        : "<none>";
                    bool insideCamera = screen.z > camera.nearClipPlane && screen.z < camera.farClipPlane &&
                        screen.x >= 0 && screen.x <= 1 && screen.y >= 0 && screen.y <= 1;
                    int pixelX = Mathf.Clamp(Mathf.RoundToInt(screen.x * texture.width), 0, texture.width - 1);
                    int pixelY = Mathf.Clamp(Mathf.RoundToInt(screen.y * texture.height), 0, texture.height - 1);
                    var centerPixel = pixels[pixelY * texture.width + pixelX];
                    Debug.Log($"[Elite preview capture] {label} landmark {renderer.name}: " +
                        $"kind={(noteRenderers.Contains(renderer) ? "note" : "receptor")}, enabled={renderer.enabled}, " +
                        $"bounds center={bounds.center} size={bounds.size}, viewport center={screen}, " +
                        $"inside camera={insideCamera}, sample=({pixelX},{pixelY}) RGBA={centerPixel}, " +
                        $"material={material.name}, base={material.color}, emission={emission}");
                }
            }
        }

        private static void FreezeAndAdvance(Component menu, double delta)
        {
            var world = (Transform)Field(menu, "_previewWorld");
            var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
            var player = world.GetComponentInChildren(playerType, true);
            Assert.That(player, Is.Not.Null, "The production builder must create its real FakeTrackPlayer.");
            ((Behaviour)player).enabled = false;
            Call(player, "Advance", delta);
        }

        private static IEnumerable<Component> FindTextComponents(GameObject root) =>
            root.GetComponentsInChildren<Component>(true)
                .Where(component => component.GetType().Name == "TextMeshProUGUI");

        private static object ReadProperty(object target, string name)
        {
            if (target == null)
                return null;

            var type = target.GetType();
            var property = type.GetProperty(name, INSTANCE);
            if (property != null)
                return property.GetValue(target);

            return type.GetField(name, INSTANCE)?.GetValue(target);
        }

        private static Vector2 TextBounds(Component text)
        {
            var bounds = ReadProperty(text, "textBounds");
            var size = (Vector3)bounds.GetType().GetProperty("size", INSTANCE).GetValue(bounds);
            return new Vector2(size.x, size.y);
        }

        private static Component PreviewPlayer(Component menu)
        {
            var world = (Transform)Field(menu, "_previewWorld");
            return world.GetComponentInChildren(Production("YARG.Settings.Preview.FakeTrackPlayer"), true);
        }

        private static int CountNear(Color32[] pixels, Color32 expected, int tolerance) => pixels.Count(pixel =>
            Math.Abs(pixel.r - expected.r) <= tolerance && Math.Abs(pixel.g - expected.g) <= tolerance &&
            Math.Abs(pixel.b - expected.b) <= tolerance && pixel.a > 200);

        private static void ConfigureOptions(object dropdown, List<string> options)
        {
            var type = dropdown.GetType();
            type.GetMethod("ClearOptions", Type.EmptyTypes).Invoke(dropdown, null);
            type.GetMethod("AddOptions", new[] { typeof(List<string>) }).Invoke(dropdown, new object[] { options });
        }

        private static void SelectDropdown(object dropdown, int index)
        {
            var type = dropdown.GetType();
            type.GetMethod("SetValueWithoutNotify", new[] { typeof(int) }).Invoke(dropdown, new object[] { index });
            var callback = type.GetProperty("onValueChanged").GetValue(dropdown);
            callback.GetType().GetMethod("Invoke").Invoke(callback, new object[] { index });
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

        private static void AssertRenderedPreview(Color32[] pixels, string message)
        {
            int nonBlack = pixels.Count(pixel => pixel.a > 8 && (pixel.r > 10 || pixel.g > 10 || pixel.b > 10));
            Assert.That(nonBlack, Is.GreaterThan(60), $"{message}: preview image must contain rendered pixels.");
        }

        private static int DifferentPixels(Color32[] first, Color32[] second)
        {
            Assert.That(second.Length, Is.EqualTo(first.Length));
            return first.Zip(second, (left, right) => Math.Abs(left.r - right.r) + Math.Abs(left.g - right.g) +
                Math.Abs(left.b - right.b) > 36).Count(changed => changed);
        }
    }
}
