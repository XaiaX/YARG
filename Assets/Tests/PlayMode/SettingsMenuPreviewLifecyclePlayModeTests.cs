using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using YARG.Core.Game;
using Object = UnityEngine.Object;

namespace YARG.Tests.PresetPreview.PlayMode
{
    public sealed class SettingsMenuPreviewLifecyclePlayModeTests
    {
        private const BindingFlags INSTANCE_FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags STATIC_FLAGS = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string SETTINGS_MENU_KEY = "Tests/SettingsMenu";
        private const string THEME_PREFAB_KEY = "Themes/Rectangular";

        private Assembly _gameAssembly;
        private AsyncOperationHandle<GameObject> _menuPrefabHandle;
        private AsyncOperationHandle<GameObject> _themePrefabHandle;
        private GameObject _themeManagerObject;
        private Component _themeManager;
        private object _themePreset;
        private GameObject _settingsMenuObject;
        private MonoBehaviour _settingsMenuRunner;

        [UnityTest]
        public IEnumerator UpdatePreviewWaitsForLayoutPublishesPreviewAndClosesCleanly()
        {
            _gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .Single(assembly => assembly.GetName().Name == "Assembly-CSharp");
            yield return CreateThemeManager();

            _menuPrefabHandle = Addressables.LoadAssetAsync<GameObject>(SETTINGS_MENU_KEY);
            yield return _menuPrefabHandle;
            Assert.That(_menuPrefabHandle.Status, Is.EqualTo(AsyncOperationStatus.Succeeded),
                $"The production SettingsMenu prefab must load from Addressables key '{SETTINGS_MENU_KEY}'");

            _settingsMenuObject = Object.Instantiate(_menuPrefabHandle.Result);
            var settingsMenuType = _gameAssembly.GetType("YARG.Menu.Settings.SettingsMenu", true);
            _settingsMenuRunner = (MonoBehaviour)_settingsMenuObject.GetComponent(settingsMenuType);
            Assert.That(_settingsMenuRunner, Is.Not.Null);

            ConfigureSettings();
            // The inert boot scene intentionally has no navigation services. Keep the
            // real menu component active as the end-of-frame coroutine owner while
            // skipping unrelated navigation setup in OnEnable.
            var readyField = settingsMenuType.GetField("_ready", INSTANCE_FLAGS);
            readyField.SetValue(_settingsMenuRunner, false);
            _settingsMenuObject.SetActive(true);
            yield return null;

            var singletonType = settingsMenuType.BaseType;
            Assert.That(singletonType.GetProperty("Instance", STATIC_FLAGS).GetValue(null), Is.SameAs(_settingsMenuRunner));
            var previewContainer = (Transform)settingsMenuType.GetField("_previewContainerUI", INSTANCE_FLAGS)
                .GetValue(_settingsMenuRunner);
            Assert.That(previewContainer, Is.Not.Null);
            Assert.That(previewContainer.gameObject.activeSelf, Is.False,
                "The production SettingsMenu prefab preview parent starts inactive");

            var trackBuilderType = _gameAssembly.GetType("YARG.Settings.Metadata.TrackPreviewBuilder", true);
            var builderInterface = _gameAssembly.GetType("YARG.Settings.Metadata.IPreviewBuilder", true);
            var metadataTabType = _gameAssembly.GetType("YARG.Settings.Metadata.MetadataTab", true);
            var trackBuilder = Activator.CreateInstance(trackBuilderType, new object[] { false, false, false });
            var startingGameMode = trackBuilderType.GetProperty("StartingGameMode");
            var gameModeType = Nullable.GetUnderlyingType(startingGameMode.PropertyType);
            Assert.That(gameModeType, Is.Not.Null);
            startingGameMode.SetValue(trackBuilder, Enum.Parse(gameModeType, "FiveFretGuitar"));
            var tab = metadataTabType.GetConstructor(new[] { typeof(string), typeof(string), builderInterface })
                .Invoke(new[] { "Track Test", "Generic", trackBuilder });
            settingsMenuType.GetProperty("CurrentTab", INSTANCE_FLAGS).GetSetMethod(true)
                .Invoke(_settingsMenuRunner, new[] { tab });

            var worldContainer = (Transform)settingsMenuType.GetField("_previewContainerWorld", INSTANCE_FLAGS)
                .GetValue(_settingsMenuRunner);
            Assert.That(worldContainer, Is.Null,
                "The production SettingsMenu prefab intentionally leaves the world preview container unset");
            var unrelatedSceneRoot = new GameObject("Unrelated Scene Root");

            var updatePreview = settingsMenuType.GetMethod("UpdatePreview", INSTANCE_FLAGS);
            var updateTask = updatePreview.Invoke(_settingsMenuRunner, new[] { tab, false });
            var taskAwaiter = updateTask.GetType().GetMethod("GetAwaiter").Invoke(updateTask, null);
            var isCompleted = taskAwaiter.GetType().GetProperty("IsCompleted");
            for (int frame = 0; frame < 120 && !(bool)isCompleted.GetValue(taskAwaiter); frame++)
            {
                yield return null;
            }
            Assert.That((bool)isCompleted.GetValue(taskAwaiter), Is.True, "The production menu preview request must complete");
            taskAwaiter.GetType().GetMethod("GetResult").Invoke(taskAwaiter, null);

            Assert.That(previewContainer.gameObject.activeSelf, Is.True,
                "UpdatePreview activates the serialized preview parent before invoking builders");
            var cameraPreviewType = _gameAssembly.GetType("YARG.Helpers.CameraPreviewTexture", true);
            var cameraPreview = _settingsMenuObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren(cameraPreviewType, true))
                .FirstOrDefault();
            Assert.That(cameraPreview, Is.Not.Null, "The actual track rig is built as a scene-root preview when its parent is null");
            var ownsActivePreview = settingsMenuType.GetMethod("OwnsActiveWorldPreview", INSTANCE_FLAGS);
            var previewRootObject = cameraPreview.transform.root.gameObject;
            Assert.That(ownsActivePreview.Invoke(_settingsMenuRunner, new[] { tab, previewRootObject }), Is.EqualTo(true),
                "The active tab owns its registered preview root");
            var otherTab = metadataTabType.GetConstructor(new[] { typeof(string), typeof(string), builderInterface })
                .Invoke(new object[] { "Other Test", "Generic", trackBuilder });
            Assert.That(ownsActivePreview.Invoke(_settingsMenuRunner, new[] { otherTab, previewRootObject }), Is.EqualTo(false),
                "A different tab cannot mutate or recreate the prior tab's registered preview");
            var camera = cameraPreview.GetComponent<Camera>();
            var sharedTexture = cameraPreviewType.GetProperty("PreviewTexture", STATIC_FLAGS).GetValue(null);
            Assert.That(camera.transform.root.parent, Is.Null,
                "The preview camera retains original scene-root parenting rather than using a UI container");
            Assert.That(camera.targetTexture, Is.SameAs(sharedTexture),
                "The original eager texture lifecycle assigns the shared texture to the preview camera");
            var rawImage = previewContainer.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
            Assert.That(rawImage, Is.Not.Null, "The production builder creates the actual preview RawImage");
            Assert.That(rawImage.texture, Is.SameAs(sharedTexture),
                "The RawImage reads the shared texture after the builder layout wait");
            Assert.That(rawImage.raycastTarget, Is.False, "Preview image must not intercept dropdown clicks");
            Canvas.ForceUpdateCanvases();
            var containerCorners = new Vector3[4];
            var imageCorners = new Vector3[4];
            ((RectTransform)previewContainer).GetWorldCorners(containerCorners);
            rawImage.rectTransform.GetWorldCorners(imageCorners);
            Assert.That(imageCorners[0].x, Is.LessThan(imageCorners[2].x));
            Assert.That(imageCorners[0].y, Is.LessThan(imageCorners[2].y),
                "The actual RawImage has positive layout size beneath the serialized preview parent");
            Assert.That(imageCorners[0].x, Is.LessThanOrEqualTo(containerCorners[2].x));
            Assert.That(imageCorners[2].x, Is.GreaterThanOrEqualTo(containerCorners[0].x));

            var destroyPreview = settingsMenuType.GetMethod("DestroyPreview", INSTANCE_FLAGS);
            var scenePreviewRoot = camera.transform.root.gameObject;
            destroyPreview.Invoke(_settingsMenuRunner, new object[] { false });
            yield return null;
            Assert.That(scenePreviewRoot == null, Is.True,
                "Closing the menu destroys the scene-root preview tracked by SettingsMenu");
            Assert.That(unrelatedSceneRoot != null, Is.True,
                "SettingsMenu teardown leaves unrelated scene roots untouched");
            Assert.That(previewContainer.childCount, Is.Zero, "Closing the menu removes the preview UI and controls");
            Object.Destroy(unrelatedSceneRoot);

            var presetsTabType = _gameAssembly.GetType("YARG.Settings.Metadata.PresetsTab", true);
            var currentTabProperty = settingsMenuType.GetProperty("CurrentTab", INSTANCE_FLAGS);
            var controlBuilderMethodName = "BuildPreviewControls";
            var navGroupType = _gameAssembly.GetType("YARG.Menu.Navigation.NavigationGroup", true);
            var navigationObject = new GameObject("Production Preset Controls Navigation");
            var navigationGroup = navigationObject.AddComponent(navGroupType);
            var presetsTab = Activator.CreateInstance(presetsTabType, new object[] { "Presets", "Customization" });
            currentTabProperty.GetSetMethod(true).Invoke(_settingsMenuRunner, new[] { presetsTab });
            var subTab = presetsTabType.GetProperty("CurrentSubTab", INSTANCE_FLAGS).GetValue(presetsTab);
            var buildControls = subTab.GetType().GetMethod(controlBuilderMethodName, INSTANCE_FLAGS);
            Assert.That(buildControls, Is.Not.Null, "The real PresetSubTab control builder must exist");
            buildControls.Invoke(subTab, new[] { navigationGroup });

            var presetSubTabType = _gameAssembly.GetType("YARG.Settings.Metadata.PresetSubTab", true);
            var controlsProperty = presetSubTabType.GetProperty("PreviewControlsContainer", STATIC_FLAGS);
            var firstControls = (Transform)controlsProperty.GetValue(null);
            Assert.That(firstControls, Is.Not.Null, "Building the real Presets controls registers their container");
            Assert.That(firstControls.parent, Is.SameAs(previewContainer));
            Assert.That(firstControls.GetComponent<LayoutElement>().ignoreLayout, Is.True);
            var controlsRect = (RectTransform)firstControls;
            var sidebar = previewContainer.parent;
            var header = sidebar.Find("Header") as RectTransform;
            Assert.That(header, Is.Not.Null, "The production sidebar exposes its 125px header");
            Canvas.ForceUpdateCanvases();
            Assert.That(Mathf.Abs(header.rect.height - 125f), Is.LessThan(0.1f),
                "The settings header uses the production prefab height");
            var headerCorners = new Vector3[4];
            var previewCornersForHeader = new Vector3[4];
            var controlsCorners = new Vector3[4];
            header.GetWorldCorners(headerCorners);
            ((RectTransform)previewContainer).GetWorldCorners(previewCornersForHeader);
            controlsRect.GetWorldCorners(controlsCorners);
            var previewTopInParent = ((RectTransform)previewContainer).InverseTransformPoint(previewCornersForHeader[1]).y;
            var headerBottomInParent = ((RectTransform)previewContainer).InverseTransformPoint(headerCorners[0]).y;
            var controlsTopInParent = ((RectTransform)previewContainer).InverseTransformPoint(controlsCorners[1]).y;
            Assert.That(Mathf.Abs(previewTopInParent - headerBottomInParent), Is.LessThan(0.1f),
                "The preview container starts exactly at the production header bottom edge");
            Assert.That(Mathf.Abs(controlsTopInParent - (previewTopInParent - 10f)), Is.LessThan(0.1f),
                "The dropdown row sits 10 canvas units below the header into the preview, matching its original sidebar-relative position");
            Assert.That(Mathf.Abs(controlsRect.rect.height - 64f), Is.LessThan(0.1f),
                "The shared Graphics/Presets dropdown row retains its production control height");
            Assert.That(firstControls.GetComponentsInChildren<TMP_Dropdown>(true).Length, Is.GreaterThanOrEqualTo(2),
                "The real controls provide instrument and preview-visual dropdowns");
            Debug.Log("SETTINGS_PREVIEW_HEADER_LAYOUT_ASSERTIONS_PASSED: controls align to production 125-unit header/preview boundary.");

            buildControls.Invoke(subTab, new[] { navigationGroup });
            yield return null;
            Assert.That(controlsProperty.GetValue(null), Is.SameAs(firstControls),
                "Refreshing Presets reuses its one registered controls container");
            Assert.That(previewContainer.GetComponentsInChildren<TMP_Dropdown>(true).Length, Is.EqualTo(2),
                "Refresh does not stack duplicate control dropdowns");

            destroyPreview.Invoke(_settingsMenuRunner, new object[] { false });
            yield return null;
            Assert.That(firstControls == null, Is.True, "Leaving Presets removes its controls container");
            Assert.That(controlsProperty.GetValue(null), Is.Null);

            var reopenedPresets = Activator.CreateInstance(presetsTabType, new object[] { "Presets", "Customization" });
            currentTabProperty.GetSetMethod(true).Invoke(_settingsMenuRunner, new[] { reopenedPresets });
            var reopenedSubTab = presetsTabType.GetProperty("CurrentSubTab", INSTANCE_FLAGS).GetValue(reopenedPresets);
            reopenedSubTab.GetType().GetMethod(controlBuilderMethodName, INSTANCE_FLAGS)
                .Invoke(reopenedSubTab, new[] { navigationGroup });
            var reopenedControls = (Transform)controlsProperty.GetValue(null);
            Assert.That(reopenedControls, Is.Not.Null.And.Not.SameAs(firstControls),
                "Reopening Presets creates one fresh production controls container");
            Assert.That(previewContainer.GetComponentsInChildren<TMP_Dropdown>(true).Length, Is.EqualTo(2));

            destroyPreview.Invoke(_settingsMenuRunner, new object[] { false });
            yield return null;
            Assert.That(previewContainer.childCount, Is.Zero,
                "Switching away from Presets clears its preview UI and exact owned controls container");
            Assert.That(controlsProperty.GetValue(null), Is.Null,
                "Leaving Presets clears the static controls reference");

            // Building the actual production track preview through UpdatePreview keeps
            // normal highway rendering available even when its tab is not Presets.
            var nonPresetTab = metadataTabType.GetConstructor(new[] { typeof(string), typeof(string), builderInterface })
                .Invoke(new object[] { "Track Test", "Generic", trackBuilder });
            currentTabProperty.GetSetMethod(true).Invoke(_settingsMenuRunner, new[] { nonPresetTab });
            var nonPresetTask = updatePreview.Invoke(_settingsMenuRunner, new[] { nonPresetTab, false });
            var nonPresetAwaiter = nonPresetTask.GetType().GetMethod("GetAwaiter").Invoke(nonPresetTask, null);
            var nonPresetIsCompleted = nonPresetAwaiter.GetType().GetProperty("IsCompleted");
            for (int frame = 0; frame < 120 && !(bool)nonPresetIsCompleted.GetValue(nonPresetAwaiter); frame++)
            {
                yield return null;
            }
            Assert.That((bool)nonPresetIsCompleted.GetValue(nonPresetAwaiter), Is.True,
                "The production non-Presets highway preview request must complete");
            nonPresetAwaiter.GetType().GetMethod("GetResult").Invoke(nonPresetAwaiter, null);
            Assert.That(previewContainer.GetComponentInChildren<UnityEngine.UI.RawImage>(true), Is.Not.Null,
                "Normal highway preview remains built outside Presets");
            Assert.That(controlsProperty.GetValue(null), Is.Null,
                "Normal highway previews do not create preset-only dropdown controls");

            Object.Destroy(navigationObject);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_settingsMenuObject != null)
            {
                var settingsMenuType = _gameAssembly.GetType("YARG.Menu.Settings.SettingsMenu", true);
                settingsMenuType.GetField("_ready", INSTANCE_FLAGS).SetValue(_settingsMenuRunner, false);
                Object.Destroy(_settingsMenuObject);
            }
            if (_themeManagerObject != null)
            {
                Object.Destroy(_themeManagerObject);
            }
            yield return null;
            if (_menuPrefabHandle.IsValid()) Addressables.Release(_menuPrefabHandle);
            if (_themePrefabHandle.IsValid()) Addressables.Release(_themePrefabHandle);
        }

        private IEnumerator CreateThemeManager()
        {
            _themePrefabHandle = Addressables.LoadAssetAsync<GameObject>(THEME_PREFAB_KEY);
            yield return _themePrefabHandle;
            Assert.That(_themePrefabHandle.Status, Is.EqualTo(AsyncOperationStatus.Succeeded),
                $"The production theme prefab must load from Addressables key '{THEME_PREFAB_KEY}'");

            var themePresetType = _gameAssembly.GetType("YARG.Themes.ThemePreset", true);
            _themePreset = themePresetType.GetField("Default", STATIC_FLAGS).GetValue(null);
            var themeManagerType = _gameAssembly.GetType("YARG.Themes.ThemeManager", true);
            _themeManagerObject = new GameObject("SettingsMenuPreviewTestThemeManager");
            _themeManager = _themeManagerObject.AddComponent(themeManagerType);
            var themeContainerType = _gameAssembly.GetType("YARG.Themes.ThemeContainer", true);
            var themeContainer = themeContainerType.GetConstructor(new[] { typeof(GameObject), typeof(bool), typeof(AssetBundle) })
                .Invoke(new object[] { _themePrefabHandle.Result, true, null });
            SetField(_themeManager, "_defaultTheme", themeContainer);
            var containers = (System.Collections.IDictionary)themeManagerType
                .GetField("_themeContainers", INSTANCE_FLAGS).GetValue(_themeManager);
            containers.Clear();
            containers.Add(_themePreset, themeContainer);
            ((Behaviour)_themeManager).enabled = false;
            yield return null;
        }

        private void ConfigureSettings()
        {
            var settingsManagerType = _gameAssembly.GetType("YARG.Settings.SettingsManager", true);
            var settingsContainerType = _gameAssembly.GetType("YARG.Settings.SettingsManager+SettingContainer", true);
            var settingsContainer = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(settingsContainerType);
            var toggleSettingType = _gameAssembly.GetType("YARG.Settings.Types.ToggleSetting", true);
            var toggleSetting = toggleSettingType.GetConstructor(new[] { typeof(bool), typeof(Action<bool>) });
            var highwayAnimation = toggleSetting.Invoke(new object[] { false, null });
            var highwayTiltType = _gameAssembly.GetType("YARG.Settings.Types.SliderSetting", true);
            var highwayTilt = highwayTiltType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(Action<float>) })
                .Invoke(new object[] { 0f, 0f, 0.85f, null });
            var resolutionType = _gameAssembly.GetType("YARG.Settings.Types.ResolutionSetting", true);
            var resolution = resolutionType.GetConstructor(new[] { typeof(Action<Resolution?>) }).Invoke(new object[] { null });
            var dropdownType = _gameAssembly.GetType("YARG.Settings.Types.DropdownSetting`1", true)
                .MakeGenericType(typeof(FullScreenMode));
            var fullscreen = dropdownType.GetConstructor(new[]
            {
                typeof(FullScreenMode), typeof(Action<FullScreenMode>), typeof(bool)
            }).Invoke(new object[] { FullScreenMode.Windowed, null, true });
            SetField(settingsContainer, "<EnableHighwayAnimation>k__BackingField", highwayAnimation);
            SetField(settingsContainer, "<HighwayTiltMultiplier>k__BackingField", highwayTilt);
            SetField(settingsContainer, "<LowQuality>k__BackingField", toggleSetting.Invoke(new object[] { false, null }));
            SetField(settingsContainer, "<ShowHitWindow>k__BackingField", toggleSetting.Invoke(new object[] { false, null }));
            SetField(settingsContainer, "<Resolution>k__BackingField", resolution);
            SetField(settingsContainer, "<FullscreenMode>k__BackingField", fullscreen);
            settingsManagerType.GetProperty("Settings", STATIC_FLAGS).GetSetMethod(true)
                .Invoke(null, new[] { settingsContainer });
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, INSTANCE_FLAGS);
            Assert.That(field, Is.Not.Null, $"Expected settings field {name}");
            field.SetValue(target, value);
        }

    }
}
