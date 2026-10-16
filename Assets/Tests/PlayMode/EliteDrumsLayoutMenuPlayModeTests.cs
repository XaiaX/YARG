using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YARG.Core.Game;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteDrumsLayoutMenuPlayModeTests
    {
        private GameObject _root;
        private GameObject _menuHost;
        private GameObject _secondRoot;
        private GameObject _themeRoot;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _capturedSettings;
        private bool _ownsAudioManager;
        private GameObject _menuServicesRoot;
        private GameObject _menuCanvasHost;
        private string _layoutRoot;
        private string _previousLayoutRoot;
        private object _layoutContainer;
        private object[] _previousLayouts;
        private Component _dialogManager;
        private GameObject _dialogRoot;
        private Type _layoutContainerType;
        private PropertyInfo _layoutRootProperty;
        private IList _layoutContents;

        private static object Field(object value, string name) => value.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        private static void Call(object value, string method, params object[] args)
        {
            var match = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length &&
                    candidate.GetParameters().Select((parameter, index) => args[index] == null ||
                        parameter.ParameterType.IsInstanceOfType(args[index])).All(matches => matches));
            match.Invoke(value, args);
        }
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object Selected(object menu)
        {
            var navigation = Field(menu, "_navigation");
            return navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation);
        }

        private static HashSet<object> TraverseNavigation(object menu, object stopAt = null)
        {
            var visited = new HashSet<object>();
            // Traverse the real directional callbacks from the current selection, not registration indices.
            for (int i = 0; i < 6; i++) Call(menu, "NavigateGrid", 0, -1);
            for (int row = 0; row < 6; row++)
            {
                for (int i = 0; i < 24; i++) Call(menu, "NavigateGrid", -1, 0);
                for (int i = 0; i < 24; i++)
                {
                    var selected = Selected(menu);
                    visited.Add(selected);
                    if (ReferenceEquals(selected, stopAt)) return visited;
                    Call(menu, "NavigateGrid", 1, 0);
                }
                Call(menu, "NavigateGrid", 0, 1);
            }
            Assert.That(stopAt, Is.Null, "The requested control must be reachable through directional navigation.");
            return visited;
        }

        private static void NavigateToFirstCell(object menu)
        {
            var cell = ((Array)Field(menu, "_cells")).GetValue(0);
            TraverseNavigation(menu, cell);
            Assert.That(Selected(menu), Is.SameAs(cell));
        }

        [UnitySetUp]
        public IEnumerator SetUpThemeManager()
        {
            _capturedSettings = false;
            _ownsAudioManager = false;

            var settingsManager = Production("YARG.Settings.SettingsManager");
            var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
            var initialized = settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            _originalSettings = settingsManager.GetProperty("Settings").GetValue(null);
            _originalSettingsInitialized = (bool)initialized.GetValue(null);
            _capturedSettings = true;

            // SettingsContainer construction invokes callbacks that require the audio handler.
            var audioHandler = Production("YARG.Core.Audio.GlobalAudioHandler");
            var audioInstance = audioHandler.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
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

            var themeManager = Production("YARG.Themes.ThemeManager");
            const BindingFlags SINGLETON_FLAGS = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            if (themeManager.GetProperty("Instance", SINGLETON_FLAGS).GetValue(null) == null)
            {
                _themeRoot = new GameObject("Elite layout menu test theme manager");
                _themeRoot.AddComponent(themeManager);
                yield return null;
            }

            var instance = themeManager.GetProperty("Instance", SINGLETON_FLAGS).GetValue(null);
            Assert.That(instance, Is.Not.Null, "the theme manager singleton must be awake before constructing previews");
            var preset = Production("YARG.Themes.ThemePreset").GetField("Default",
                BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var fretPrefab = (GameObject)themeManager.GetMethod("CreateFretPrefabFromTheme").Invoke(instance,
                new[] { preset, Enum.Parse(Production("YARG.Themes.VisualStyle"), "FiveLaneDrums"), "fret" });
            Assert.That(fretPrefab, Is.Not.Null, "the default theme must be loaded before previews start");
            var fret = fretPrefab.GetComponent(Production("YARG.Gameplay.Visuals.Fret"));
            Assert.That(fret, Is.Not.Null);
            var bind = fret.GetType().GetProperty("ThemeBind").GetValue(fret);
            Assert.That(bind, Is.Not.Null);
            Assert.That(((System.Collections.IEnumerable)bind.GetType()
                .GetMethod("GetColoredMaterials").Invoke(bind, null)).Cast<Material>(), Is.Not.Empty,
                "the loaded themed fret must contain its authored colored materials");
        }

        private void MountMenuServices()
        {
            _menuServicesRoot = new GameObject("Elite layout menu isolated services");
            _menuServicesRoot.SetActive(false);
            var menuData = _menuServicesRoot.AddComponent(Production("YARG.Menu.Data.MenuData"));
            var menuDataType = menuData.GetType();
            menuDataType.GetField("_navigationIcons", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(menuData, AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Art/Menu/Data/NavigationIcons.asset"));
            menuDataType.GetField("_colors", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(menuData, AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Art/Menu/Data/MenuColors.asset"));

            var helpBarObject = new GameObject("Elite layout test HelpBar");
            helpBarObject.transform.SetParent(_menuServicesRoot.transform, false);
            var helpBar = helpBarObject.AddComponent(Production("YARG.Menu.Persistent.HelpBar"));
            var buttonContainer = new GameObject("HelpBar buttons").transform;
            buttonContainer.SetParent(helpBarObject.transform, false);
            var buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Menu/Persistent/HelpBarButton.prefab");
            var musicPlayerObject = new GameObject("MusicPlayer");
            musicPlayerObject.transform.SetParent(helpBarObject.transform, false);
            musicPlayerObject.SetActive(false);
            var musicPlayer = musicPlayerObject.AddComponent(Production("YARG.Menu.Persistent.MusicPlayer"));
            var helpBarType = helpBar.GetType();
            helpBarType.GetField("_buttonContainer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(helpBar, buttonContainer);
            helpBarType.GetField("_buttonPrefab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(helpBar, buttonPrefab);
            helpBarType.GetProperty("MusicPlayer").GetSetMethod(true).Invoke(helpBar, new[] { musicPlayer });

            var navigator = _menuServicesRoot.AddComponent(Production("YARG.Menu.Navigation.Navigator"));
            var dialogRoot = new GameObject("Dialog Container");
            dialogRoot.transform.SetParent(_menuServicesRoot.transform, false);
            var dialogManager = _menuServicesRoot.AddComponent(Production("YARG.Menu.Persistent.DialogManager"));
            var dialogManagerType = dialogManager.GetType();
            dialogManagerType.GetField("_dialogContainer", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(dialogManager, dialogRoot.transform);
            dialogManagerType.GetField("_renameDialog", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(dialogManager, AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Menu/Common/Dialogs/RenameDialog.prefab").GetComponent(Production("YARG.Menu.Dialogs.RenameDialog")));
            dialogManagerType.GetField("_messagePrefab", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(dialogManager, AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Menu/Common/Dialogs/MessageDialog.prefab").GetComponent(Production("YARG.Menu.Dialogs.MessageDialog")));
            _dialogManager = (Component)dialogManager;
            _dialogRoot = dialogRoot;
            _menuServicesRoot.SetActive(true);
        }

        [UnityTest]
        public IEnumerator BuiltInCopyUsesRequestedNameAndProducesEditableDraft()
        {
#if UNITY_EDITOR
            var menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(menuPrefab, Is.Not.Null);
            _menuCanvasHost = new GameObject("Elite layout menu test Canvas");
            var canvas = _menuCanvasHost.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _menuCanvasHost.AddComponent<GraphicRaycaster>();
            var scaler = _menuCanvasHost.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            _root = UnityEngine.Object.Instantiate(menuPrefab, _menuCanvasHost.transform, false);
            var menu = _root.GetComponent(menuType);

            MountMenuServices();
            var renameType = Production("YARG.Menu.Dialogs.RenameDialog");

            _layoutContainerType = Production("YARG.Settings.Customization.EliteDrumsLayoutPresetContainer");
            var managerType = Production("YARG.Settings.Customization.CustomContentManager");
            _layoutContainer = managerType.GetField("EliteDrumsLayouts", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            _layoutRootProperty = _layoutContainerType.GetProperty("ContentRootOverride", BindingFlags.Public | BindingFlags.Static);
            _previousLayoutRoot = (string)_layoutRootProperty.GetValue(null);
            var contentField = _layoutContainerType.GetField("_content", BindingFlags.Instance | BindingFlags.NonPublic);
            _layoutContents = (IList)contentField.GetValue(_layoutContainer);
            _previousLayouts = _layoutContents.Cast<object>().ToArray();
            _layoutContents.Clear();
            _layoutRoot = Path.Combine(Path.GetTempPath(), "eliteDrumsLayoutCopy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_layoutRoot);
            _layoutRootProperty.SetValue(null, _layoutRoot);

            Call(menu, "Initialize", EliteDrumsLayoutPreset.Compact, null, (Action)(() => { }));
            yield return null;
            ((UnityEngine.UI.Button)Field(menu, "_copyLayout")).onClick.Invoke();

            var dialog = _dialogRoot.GetComponentInChildren(renameType, true);
            Assert.That(dialog, Is.Not.Null, "Copy must display the production rename dialog.");
            yield return null;
            var inputFieldType = Production("TMPro.TMP_InputField");
            var input = dialog.GetType().GetField("_inputField", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(dialog);
            inputFieldType.GetProperty("text").SetValue(input, "Copied layout");
            var coloredButtonType = Production("YARG.Menu.ColoredButton");
            var buttonContainer = (Transform)dialog.GetType().GetMethod("get_DialogButtonContainer",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, null);
            var buttons = buttonContainer.GetComponentsInChildren(coloredButtonType, true).Cast<Component>().ToArray();
            Assert.That(buttons, Has.Length.EqualTo(2));
            var confirmClick = buttons[1].GetType().GetProperty("OnClick").GetValue(buttons[1]);
            confirmClick.GetType().GetMethod("Invoke").Invoke(confirmClick, null);
            yield return null;

            var custom = (IList)_layoutContainerType.GetProperty("CustomPresets").GetValue(_layoutContainer);
            Assert.That(custom.Count, Is.EqualTo(1));
            var copied = (EliteDrumsLayoutPreset)custom[0];
            Assert.That(copied.Id, Is.Not.EqualTo(EliteDrumsLayoutPreset.Compact.Id));
            Assert.That(copied.Name, Is.EqualTo("Copied layout"));
            Assert.That(menuType.GetProperty("Draft").GetValue(menu), Is.Not.Null);
            Assert.That(((EliteDrumsLayoutPreset)menuType.GetProperty("Draft").GetValue(menu)).Id,
                Is.EqualTo(copied.Id));
            Assert.That((bool)menuType.GetProperty("IsDirty").GetValue(menu), Is.False);
#else
            Assert.Ignore("Requires the serialized production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator CustomLayoutDeleteRequiresDelayedConfirmationAndCancelKeepsPreset()
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null);
            _menuCanvasHost = new GameObject("Elite layout delete test Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            _root = UnityEngine.Object.Instantiate(prefab, _menuCanvasHost.transform, false);
            var menu = _root.GetComponent(menuType);
            MountMenuServices();

            _layoutContainerType = Production("YARG.Settings.Customization.EliteDrumsLayoutPresetContainer");
            _layoutContainer = Production("YARG.Settings.Customization.CustomContentManager")
                .GetField("EliteDrumsLayouts", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            _layoutRootProperty = _layoutContainerType.GetProperty("ContentRootOverride", BindingFlags.Public | BindingFlags.Static);
            _previousLayoutRoot = (string)_layoutRootProperty.GetValue(null);
            _layoutContents = (IList)_layoutContainerType.GetField("_content", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_layoutContainer);
            _previousLayouts = _layoutContents.Cast<object>().ToArray();
            _layoutContents.Clear();
            _layoutRoot = Path.Combine(Path.GetTempPath(), "eliteDrumsLayoutDelete_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_layoutRoot);
            _layoutRootProperty.SetValue(null, _layoutRoot);

            var custom = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Delete flow test");
            _layoutContainerType.GetMethod("CommitPreset").Invoke(_layoutContainer, new object[] { custom });
            Call(menu, "Initialize", custom, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }));
            yield return null;
            var deleteButton = (Button)Field(menu, "_deleteLayout");
            Assert.That(deleteButton.interactable, Is.True);
            deleteButton.onClick.Invoke();
            Assert.That((bool)_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.True);
            // DialogManager's default Close button is destroyed at the end of the frame
            // when the compact confirmation replaces it with Cancel and Delete.
            yield return null;
            var dialog = _dialogRoot.GetComponentInChildren(Production("YARG.Menu.Dialogs.MessageDialog"), true);
            Assert.That(dialog, Is.Not.Null);
            var buttons = ((Transform)dialog.GetType().GetMethod("get_DialogButtonContainer",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, null))
                .GetComponentsInChildren(Production("YARG.Menu.ColoredButton"), true);
            Assert.That(buttons, Has.Length.EqualTo(2));
            var confirm = (Component)buttons.GetValue(1);
            Assert.That(confirm.GetComponentInChildren<Button>().interactable, Is.False,
                "The real delete confirmation must start unarmed.");
            Assert.That(((IList)_layoutContainerType.GetProperty("CustomPresets").GetValue(_layoutContainer)).Count,
                Is.EqualTo(1));
            var cancel = (Component)buttons.GetValue(0);
            var cancelClick = cancel.GetType().GetProperty("OnClick").GetValue(cancel);
            cancelClick.GetType().GetMethod("Invoke").Invoke(cancelClick, null);
            yield return null;
            Assert.That((bool)_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.False);
            Assert.That(((IList)_layoutContainerType.GetProperty("CustomPresets").GetValue(_layoutContainer)).Count,
                Is.EqualTo(1), "Cancel must not delete the layout.");

            deleteButton.onClick.Invoke();
            Assert.That((bool)_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.True);
            yield return null;
            var reopenedDialog = _dialogRoot.GetComponentInChildren(Production("YARG.Menu.Dialogs.MessageDialog"), true);
            var reopenedButtons = ((Transform)reopenedDialog.GetType().GetMethod("get_DialogButtonContainer",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(reopenedDialog, null))
                .GetComponentsInChildren(Production("YARG.Menu.ColoredButton"), true);
            Assert.That(reopenedButtons, Has.Length.EqualTo(2),
                "The reopened dialog must contain only Cancel and Delete after deferred cleanup.");
            var reopenedConfirm = (Component)reopenedButtons.GetValue(1);
            Assert.That(reopenedConfirm.GetComponentInChildren<Button>().interactable, Is.False,
                "The delete delay must reset each time the dialog opens.");
            yield return new WaitForSecondsRealtime(2.1f);
            Assert.That(reopenedConfirm.GetComponentInChildren<Button>().interactable, Is.True);
            var confirmClick = reopenedConfirm.GetType().GetProperty("OnClick").GetValue(reopenedConfirm);
            confirmClick.GetType().GetMethod("Invoke").Invoke(confirmClick, null);
            yield return null;
            Assert.That((bool)_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.False);
            Assert.That(((IList)_layoutContainerType.GetProperty("CustomPresets").GetValue(_layoutContainer)).Count,
                Is.Zero, "Confirmed delete must remove the custom layout from the real container.");
            Assert.That(((EliteDrumsLayoutPreset)menuType.GetProperty("Draft").GetValue(menu)).Id,
                Is.EqualTo(EliteDrumsLayoutPreset.Compact.Id), "The editor must fall back to a built-in layout.");
#else
            Assert.Ignore("Requires the serialized production prefabs from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ToolbarAndGridNavigationReachAllEnabledControls()
        {
#if UNITY_EDITOR
            var prefab = Resources.Load<GameObject>("EliteDrumsLayoutMenu");
            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            var menu = OpenMenuThroughHost(prefab, menuType, out _root);
            var incomplete = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Incomplete navigation");
            incomplete.Slots[1] = EliteDrumsLayoutSlot.Ordinary(null, null);
            var custom = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Custom navigation");
            _layoutContainerType = Production("YARG.Settings.Customization.EliteDrumsLayoutPresetContainer");
            _layoutContainer = Production("YARG.Settings.Customization.CustomContentManager")
                .GetField("EliteDrumsLayouts", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            _layoutContents = (IList)_layoutContainerType.GetField("_content", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_layoutContainer);
            _previousLayouts = _layoutContents.Cast<object>().ToArray();
            _layoutContents.Clear();
            _layoutContents.Add(incomplete);
            _layoutContents.Add(custom);
            foreach (var source in new object[] { EliteDrumsLayoutPreset.Compact, custom, incomplete })
            {
                // Initialize only accepts structurally valid layouts; the incomplete preset
                // must be reached the real way, by selecting it in the management dropdown.
                if (ReferenceEquals(source, incomplete))
                {
                    var layouts = (EliteDrumsLayoutPreset[])menuType
                        .GetField("_layouts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
                    int index = Array.IndexOf(layouts, incomplete);
                    Assert.That(index, Is.GreaterThanOrEqualTo(0));
                    SelectPreviewLayout(menu, index);
                }
                else
                {
                    Call(menu, "Initialize", source, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }));
                }
                yield return null;
                Canvas.ForceUpdateCanvases();
                if (!ReferenceEquals(source, incomplete))
                    Assert.That(((Component)Selected(menu)).gameObject,
                        Is.EqualTo(((Button)Field(menu, "_back")).gameObject));
                var visited = TraverseNavigation(menu);
                foreach (var name in new[] { "_back", "_layoutDropdown", "_openLayoutFolder", "_previewColor", "_previewOptions",
                    "_save", "_renameLayout", "_copyLayout", "_deleteLayout", "_importLayout", "_exportLayout" })
                {
                    var control = (Selectable)Field(menu, name);
                    bool enabled = control.IsInteractable() && control.gameObject.activeInHierarchy;
                    Assert.That(visited.Cast<Component>().Any(target => target.gameObject == control.gameObject),
                        Is.EqualTo(enabled), source + ": " + name);
                }
                foreach (Component cell in (Array)Field(menu, "_cells"))
                    Assert.That(visited.Contains(cell),
                        Is.EqualTo(cell.gameObject.activeInHierarchy && cell.GetComponent<Button>().IsInteractable()),
                        source + ": " + cell.name);
                for (int i = 0; i < 8; i++) Call(menu, "NavigateGrid", 0, -1);
                var top = Selected(menu);
                Call(menu, "NavigateGrid", 0, -1);
                Assert.That(Selected(menu), Is.SameAs(top), "Up at the screen boundary must stay on-screen.");
            }
#else
            Assert.Ignore("Requires production prefabs.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ManagementNavigationSkipsDisabledActionsAndReachesSave()
        {
#if UNITY_EDITOR
            var prefab = Resources.Load<GameObject>("EliteDrumsLayoutMenu");
            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            var menu = OpenMenuThroughHost(prefab, menuType, out _root);
            // A temporary layout (not yet saved to the container) disables Copy/Rename/Export
            // while Delete/Import/Save stay available; navigation must skip the disabled ones.
            var source = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Temporary navigation");
            Call(menu, "Initialize", source, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }));
            yield return null;
            var visited = TraverseNavigation(menu).Cast<Component>().ToArray();
            foreach (var name in new[] { "_renameLayout", "_copyLayout", "_exportLayout" })
            {
                var control = (Selectable)Field(menu, name);
                Assert.That(control.IsInteractable(), Is.False, name + " must be disabled for a temporary layout.");
                Assert.That(visited.Any(target => target.gameObject == control.gameObject), Is.False,
                    name + " must be skipped by navigation while disabled.");
            }
            var rows = ((IEnumerable)menuType.GetMethod("GetNavigationRows",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null)).Cast<IEnumerable>().ToArray();
            var managementOrder = rows[2].Cast<Component>().Select(control => control.gameObject).ToArray();
            Assert.That(managementOrder, Is.EqualTo(new[]
            {
                "_save", "_renameLayout", "_copyLayout", "_deleteLayout", "_importLayout", "_exportLayout"
            }.Select(name => (Selectable)Field(menu, name))
                .Where(control => control.IsInteractable() && control.gameObject.activeInHierarchy)
                .Select(control => control.gameObject).ToArray()),
                "Runtime navigation follows the visible management action order while skipping disabled actions.");
            var save = (Selectable)Field(menu, "_save");
            Assert.That(visited.Any(target => target.gameObject == save.gameObject), Is.True,
                "Navigation must still reach Save when the row contains disabled actions.");
            Assert.That(save.IsInteractable(), Is.True);
#else
            Assert.Ignore("Requires production prefabs.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SaveThenBackAndDirtyBackUseExistingConfirmation()
        {
#if UNITY_EDITOR
            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            MountMenuServices();
            var menu = OpenMenuThroughHost(Resources.Load<GameObject>("EliteDrumsLayoutMenu"), menuType, out _root);
            var source = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Save navigation");
            int saves = 0;
            int closes = 0;
            EliteDrumsLayoutPreset saved = null;
            Action<EliteDrumsLayoutPreset> save = value => { saves++; saved = value; };
            Action close = () => closes++;
            Call(menu, "Initialize", source, save, close);
            yield return null;
            TraverseNavigation(menu, ((Button)Field(menu, "_save")).GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable")));
            Call(Field(menu, "_navigation"), "ConfirmSelection");
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(closes, Is.EqualTo(1));
            Assert.That(saved.Id, Is.EqualTo(source.Id));
            Assert.That(saved, Is.Not.SameAs(source));
            Assert.That(_root.activeSelf, Is.False);
            Call(menu, "Initialize", saved, save, close);
            Call(menu, "Back");
            Assert.That(closes, Is.EqualTo(2));
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.False);
            Call(menu, "Initialize", saved, save, close);
            NavigateToFirstCell(menu);
            Call(Field(menu, "_navigation"), "ConfirmSelection");
            Call(Field(menu, "_popupNavigation"), "ConfirmSelection");
            Assert.That(menuType.GetProperty("IsDirty").GetValue(menu), Is.True);
            Call(menu, "Back");
            yield return null;
            Assert.That(_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.True);
            Assert.That(_dialogRoot.GetComponentInChildren(Production("YARG.Menu.Dialogs.MessageDialog"), true), Is.Not.Null);
            Assert.That(closes, Is.EqualTo(2), "Dirty Back must not close before confirmation.");
            Assert.That(saves, Is.EqualTo(1), "Dirty Back must not implicitly save.");
            Call(menu, "Back");
            Assert.That(_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.False);
            Assert.That(menuType.GetProperty("IsDirty").GetValue(menu), Is.True);
            Assert.That(_root.activeSelf, Is.True);
#else
            Assert.Ignore("Requires production prefabs.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ProductionPopupCancelRestoresInvokingCell()
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null);
            var menuType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(
                "YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu")).First(type => type != null);
            var menu = OpenMenuThroughHost(prefab, menuType, out _root);
            var source = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("isolated menu test");
            Call(menu, "Initialize", source, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }));
            AssertSelectedDraft(menu, menuType, source);
            yield return null;
            var navigation = Field(menu, "_navigation");
            NavigateToFirstCell(menu);
            var invokingCell = navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation);
            Call(navigation, "ConfirmSelection");
            yield return null;
            Assert.That(((GameObject)Field(menu, "_popup")).activeSelf, Is.True);
            var popup = (GameObject)Field(menu, "_popup");
            Canvas.ForceUpdateCanvases();
            var panel = popup.transform.Find("Assignment Popup Panel").GetComponent<RectTransform>();
            var popupRect = popup.GetComponent<RectTransform>();
            var outsideScreenPoint = RectTransformUtility.WorldToScreenPoint(null,
                popupRect.TransformPoint(new Vector3(-popupRect.rect.width * 0.4f, 0f, 0f)));
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint(popupRect, outsideScreenPoint), Is.True,
                "The outside-click point must still be inside the full-screen blocker.");
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint(panel, outsideScreenPoint), Is.False);
            var eventSystemObject = new GameObject("Elite layout test EventSystem", typeof(EventSystem),
                Production("UnityEngine.InputSystem.UI.InputSystemUIInputModule"));
            var eventSystem = eventSystemObject.GetComponent<EventSystem>();
            var canvas = _root.GetComponentInParent<Canvas>();
            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null,
                "The production menu's owning Canvas must provide its raycaster.");
            var eventData = new PointerEventData(eventSystem)
            {
                position = outsideScreenPoint,
                button = PointerEventData.InputButton.Left
            };
            var raycastResults = new List<RaycastResult>();
            eventSystem.RaycastAll(eventData, raycastResults);
            var backdropButton = (Button)Field(menu, "_popupBackdropButton");
            Assert.That(backdropButton, Is.Not.Null);
            Assert.That(backdropButton.onClick.GetPersistentEventCount(), Is.Zero,
                "The generated prefab does not persist builder-time AddListener callbacks.");
            var blocker = backdropButton.gameObject;
            Assert.That(raycastResults.Count, Is.GreaterThan(0));
            Assert.That(raycastResults[0].gameObject, Is.EqualTo(blocker),
                "Outside clicks must hit the full-screen blocker rather than an underlying cell.");
            ExecuteEvents.ExecuteHierarchy(raycastResults[0].gameObject, eventData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(raycastResults[0].gameObject, eventData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(raycastResults[0].gameObject, eventData, ExecuteEvents.pointerClickHandler);
            Assert.That(popup.activeSelf, Is.False);
            Assert.That(source.Slots[0].Cymbal, Is.Null, "The outside click must not assign the underlying cell.");
            UnityEngine.Object.Destroy(eventSystemObject);
            Call(menu, "OpenCell", 0, false);
            var choiceIds = (string[])Field(menu, "_choiceIds");
            var choices = (Button[])Field(menu, "_choices");
            var popupNavigation = Field(menu, "_popupNavigation");
            int clearIndex = Array.IndexOf(choiceIds, "clear");
            Assert.That(clearIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(choices[clearIndex].interactable, Is.False);
            var clearNavigation = choices[clearIndex].GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
            Assert.That(popupNavigation.GetType().GetProperty("SelectedBehaviour").GetValue(popupNavigation),
                Is.Not.SameAs(clearNavigation));
            for (int i = 0; i < choiceIds.Length * 2; i++)
            {
                Call(popupNavigation, "SelectNext", false);
                Assert.That(popupNavigation.GetType().GetProperty("SelectedBehaviour").GetValue(popupNavigation),
                    Is.Not.SameAs(clearNavigation), "Controller navigation must skip disabled Clear.");
            }
            var labels = (Array)Field(menu, "_choiceLabels");
            for (int i = choiceIds.Length; i < choices.Length; i++)
            {
                Assert.That(choices[i].transform.parent.gameObject.activeSelf, Is.False,
                    "Shrinking the picker must hide the entire option row, including its sibling label.");
                Assert.That((string)((Component)labels.GetValue(i)).GetType().GetProperty("text").GetValue(labels.GetValue(i)), Is.Empty);
            }
            Call(menu, "OpenCell", 0, true);
            var grownChoiceIds = (string[])Field(menu, "_choiceIds");
            Assert.That(grownChoiceIds.Length, Is.GreaterThan(choiceIds.Length));
            for (int i = choiceIds.Length; i < grownChoiceIds.Length; i++)
            {
                Assert.That(choices[i].transform.parent.gameObject.activeSelf, Is.True);
                Assert.That((string)((Component)labels.GetValue(i)).GetType().GetProperty("text").GetValue(labels.GetValue(i)), Is.Not.Empty);
            }
            Call(menu, "OpenCell", 1, false);
            var assignedChoices = (string[])Field(menu, "_choiceIds");
            int assignedClearIndex = Array.IndexOf(assignedChoices, "clear");
            var assignedClear = choices[assignedClearIndex];
            Assert.That(assignedClear.interactable, Is.True);
            var assignedClearNavigation = assignedClear.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
            bool selectedClear = false;
            for (int i = 0; i < assignedChoices.Length * 2; i++)
            {
                Call(popupNavigation, "SelectNext", false);
                selectedClear |= popupNavigation.GetType().GetProperty("SelectedBehaviour").GetValue(popupNavigation) == assignedClearNavigation;
            }
            Assert.That(selectedClear, Is.True, "Assigned-row Clear must return to controller navigation.");
            Call(menu, "Back");
            yield return null;
            Assert.That(((GameObject)Field(menu, "_popup")).activeSelf, Is.False);
            Assert.That(navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation),
                Is.SameAs(invokingCell));
            Assert.That(source.Slots[0].Cymbal, Is.Null);
#else
            Assert.Ignore("Requires the serialized production prefab from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ProductionEditUsesDetachedDraftAndBackKeepsEditing()
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null);
            _root = UnityEngine.Object.Instantiate(prefab);
            var menuType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(
                "YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu")).First(type => type != null);
            var menu = _root.GetComponent(menuType);
            MountMenuServices();
            var source = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("isolated draft test");
            Call(menu, "Initialize", source, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }));
            AssertSelectedDraft(menu, menuType, source);
            yield return null;

            var navigation = Field(menu, "_navigation");
            NavigateToFirstCell(menu);
            Call(navigation, "ConfirmSelection");
            yield return null;
            Assert.That(((GameObject)Field(menu, "_popup")).activeSelf, Is.True);
            var popupNavigation = Field(menu, "_popupNavigation");
            Call(popupNavigation, "ConfirmSelection");
            yield return null;

            var draft = (EliteDrumsLayoutPreset)menuType.GetProperty("Draft").GetValue(menu);
            Assert.That(source.Slots[0].Cymbal, Is.Null);
            Assert.That(draft.Slots[0].Cymbal, Is.Not.Null);
            Assert.That(menuType.GetProperty("IsDirty").GetValue(menu), Is.True);

            navigation = Field(menu, "_navigation");
            Call(navigation, "ConfirmSelection");
            yield return null;
            Assert.That(((GameObject)Field(menu, "_popup")).activeSelf, Is.True);
            Call(menu, "Back");
            yield return null;
            Assert.That(((GameObject)Field(menu, "_popup")).activeSelf, Is.False);
            Assert.That(_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.False,
                "Back while the picker is open must dismiss only the picker.");
            Assert.That(menuType.GetProperty("IsDirty").GetValue(menu), Is.True);

            Call(menu, "Back");
            yield return null;
            Assert.That(_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.True);
            var message = _dialogRoot.GetComponentInChildren(Production("YARG.Menu.Dialogs.MessageDialog"), true);
            Assert.That(message, Is.Not.Null, "dirty Back must use the shared compact confirmation dialog.");
            var buttons = ((Transform)message.GetType().GetMethod("get_DialogButtonContainer",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(message, null))
                .GetComponentsInChildren(Production("YARG.Menu.ColoredButton"), true);
            Assert.That(buttons, Has.Length.EqualTo(2));
            var discardButton = (Component)buttons.GetValue(1);
            var discardUiButton = discardButton.GetComponentInChildren<Button>();
            Assert.That(discardUiButton.interactable, Is.False, "Discard must be gated immediately on each opening.");
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(discardUiButton.interactable, Is.True, "Discard arms after one unscaled second.");
            var keepButton = (Component)buttons.GetValue(0);
            var keepClick = keepButton.GetType().GetProperty("OnClick").GetValue(keepButton);
            keepClick.GetType().GetMethod("Invoke").Invoke(keepClick, null);
            yield return null;
            Assert.That(_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager), Is.False);
            Assert.That(_root.activeSelf, Is.True);
            Assert.That(menuType.GetProperty("IsDirty").GetValue(menu), Is.True);
            var keptDraft = (EliteDrumsLayoutPreset)menuType.GetProperty("Draft").GetValue(menu);
            Assert.That(keptDraft.Slots[0].Cymbal, Is.EqualTo(draft.Slots[0].Cymbal));
            Call(menu, "Back");
            yield return null;
            var reopenedMessage = _dialogRoot.GetComponentInChildren(Production("YARG.Menu.Dialogs.MessageDialog"), true);
            var reopenedButtons = ((Transform)reopenedMessage.GetType().GetMethod("get_DialogButtonContainer",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(reopenedMessage, null))
                .GetComponentsInChildren(Production("YARG.Menu.ColoredButton"), true);
            Assert.That(((Component)reopenedButtons.GetValue(1)).GetComponentInChildren<Button>().interactable, Is.False,
                "Every new discard confirmation must restart its delay.");
            Call(menu, "Back");
#else
            Assert.Ignore("Requires the serialized production prefab from the Editor asset database.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator RequestedPaletteRefreshesCellIndicatorsOnFreshAndReusedMenu()
        {
#if UNITY_EDITOR
            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            var menu = OpenMenuThroughHost(Resources.Load<GameObject>("EliteDrumsLayoutMenu"), menuType, out _root);
            var source = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Palette initialization");
            foreach (var sentinel in new[] { System.Drawing.Color.Magenta, System.Drawing.Color.Lime })
            {
                var palette = new ColorProfile("Requested palette " + sentinel.Name);
                palette.EliteDrums.Fret1 = sentinel;
                Call(menu, "Initialize", source, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }), palette);
                yield return null;
                var cell = ((Array)Field(menu, "_cells")).GetValue(EliteDrumsLayoutPreset.SLOT_COUNT);
                var actual = ((Image)Field(cell, "_fretPill")).color;
                Color expected = new Color32(sentinel.R, sentinel.G, sentinel.B, sentinel.A);
                Assert.That(actual, Is.EqualTo(expected), "Requested palette must be rendered immediately, including when reusing the menu.");
                Assert.That(Field(menu, "_previewColorProfile"), Is.SameAs(palette));
            }
#else
            Assert.Ignore("Requires production prefabs.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ProductionPreviewSwitchesLayoutsAndColorsWithoutSharingOrLeakingObjects()
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null);
            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            var firstMenu = CreateMenu(prefab, menuType, out _root);
            var secondMenu = CreateMenu(prefab, menuType, out _secondRoot);
            var firstLayout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("preview A");
            var secondLayout = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("preview B");
            Call(firstMenu, "Initialize", firstLayout, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }));
            Call(secondMenu, "Initialize", secondLayout, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }));

            var profileA = new ColorProfile("Preview color A");
            profileA.EliteDrums.Fret1 = System.Drawing.Color.Magenta;
            profileA.EliteDrums.Fret10 = System.Drawing.Color.Cyan;
            var profileB = new ColorProfile("Preview color B");
            profileB.EliteDrums.Fret1 = System.Drawing.Color.Lime;
            profileB.EliteDrums.Fret10 = System.Drawing.Color.Lime;
            ConfigurePreviewOptions(firstMenu, new[] { EliteDrumsLayoutPreset.Compact,
                EliteDrumsLayoutPreset.Full, TenLaneLayout() }, new[] { profileA, profileB });
            ConfigurePreviewOptions(secondMenu, new[] { EliteDrumsLayoutPreset.Compact,
                EliteDrumsLayoutPreset.Full, TenLaneLayout() }, new[] { profileA, profileB });
            SelectPreviewColor(firstMenu, 0);
            SelectPreviewColor(secondMenu, 1);
            yield return null;
            yield return null;

            var firstWorld = (Transform)Field(firstMenu, "_previewWorld");
            var secondWorld = (Transform)Field(secondMenu, "_previewWorld");
            var firstPlayer = PreviewPlayer(firstWorld);
            var secondPlayer = PreviewPlayer(secondWorld);
            Assert.That(Vector3.Distance(firstWorld.position, secondWorld.position), Is.GreaterThan(9000f),
                "separate editors need isolated preview camera frusta");
            Assert.That((bool)Field(firstMenu, "_forceStarPowerNotes"), Is.False);
            Assert.That((bool)Field(secondMenu, "_forceStarPowerNotes"), Is.False);
            AssertPreview(firstPlayer, 5, 0, System.Drawing.Color.Magenta);
            AssertPreview(secondPlayer, 8, 0, System.Drawing.Color.Lime);

            var secondFret = PreviewFrets(secondPlayer).First();
            var secondMaterial = FretMaterial(secondFret);
            var secondOriginalColor = secondMaterial.color;
            SelectPreviewLayout(firstMenu, 1);
            yield return null;
            AssertPreview(PreviewPlayer(firstWorld), 8, 0, System.Drawing.Color.Magenta);
            Assert.That(secondWorld.childCount, Is.EqualTo(1), "rebuilding one menu must not clear the other menu's preview");
            Assert.That(secondMaterial.color, Is.EqualTo(secondOriginalColor));

            SelectPreviewLayout(firstMenu, 2);
            yield return null;
            AssertPreview(PreviewPlayer(firstWorld), 10, 9, System.Drawing.Color.Cyan);
            SelectPreviewColor(firstMenu, 1);
            yield return null;
            AssertPreview(PreviewPlayer(firstWorld), 10, 9, System.Drawing.Color.Lime);
            Assert.That(FretMaterial(PreviewFrets(PreviewPlayer(firstWorld)).First()), Is.Not.SameAs(secondMaterial),
                "separate menu previews must own separate instantiated materials");
            Assert.That(secondMaterial.color, Is.EqualTo(secondOriginalColor),
                "switching this menu's color profile must not recolor the other preview");

            var firstOwnedObjects = firstWorld.Cast<Transform>().Select(child => child.gameObject).ToArray();
            var firstMaterial = FretMaterial(PreviewFrets(PreviewPlayer(firstWorld)).First());
            _root.SetActive(false);
            yield return null;
            Assert.That(firstWorld.childCount, Is.Zero, "disabling the menu destroys its owned preview objects");
            Assert.That(firstOwnedObjects.All(value => value == null), Is.True);
            Assert.That(firstMaterial == null, Is.True, "destroying the preview also releases its instantiated material");
            Assert.That(secondWorld.childCount, Is.EqualTo(1), "closing one menu leaves the other preview intact");
            Assert.That(FretMaterial(PreviewFrets(PreviewPlayer(secondWorld)).First()).color,
                Is.EqualTo(secondOriginalColor));
#else
            Assert.Ignore("Requires the serialized production prefab from the Editor asset database.");
            yield break;
#endif
        }

        private Component OpenMenuThroughHost(GameObject prefab, Type menuType, out GameObject root)
        {
            _menuHost = new GameObject("Elite layout test menu host",
                typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _menuHost.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var managerType = Production("YARG.Menu.MenuManager");
            var manager = (Behaviour)_menuHost.AddComponent(managerType);
            // The fixture opens its own menu; do not run the scene's MainMenu startup.
            manager.enabled = false;
            var menuEnum = managerType.GetNestedType("Menu");
            var menu = Enum.Parse(menuEnum, "EliteDrumsLayout");
            var opened = (Component)managerType.GetMethod("ShowMenu", new[] { menuEnum })
                .Invoke(manager, new[] { menu });
            root = opened.gameObject;
            return root.GetComponent(menuType);
        }

        private static Component CreateMenu(GameObject prefab, Type menuType, out GameObject root)
        {
            root = UnityEngine.Object.Instantiate(prefab);
            return root.GetComponent(menuType);
        }

        private static void AssertSelectedDraft(Component menu, Type menuType, EliteDrumsLayoutPreset source)
        {
            var draft = (EliteDrumsLayoutPreset)menuType.GetProperty("Draft").GetValue(menu);
            Assert.That(draft.Id, Is.EqualTo(source.Id), "Initialize must preserve the supplied detached layout.");
            var layouts = (EliteDrumsLayoutPreset[])menuType
                .GetField("_layouts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
            var dropdown = Field(menu, "_layoutDropdown");
            int selectedIndex = (int)dropdown.GetType().GetProperty("value").GetValue(dropdown);
            Assert.That(layouts[selectedIndex].Id, Is.EqualTo(source.Id),
                "The management selector must retain the supplied layout instead of switching to a built-in default.");
            Assert.That(layouts[selectedIndex].Name, Is.EqualTo(source.Name));
            Assert.That(((UnityEngine.UI.Button)Field(menu, "_renameLayout")).interactable, Is.False);
            Assert.That(((UnityEngine.UI.Button)Field(menu, "_deleteLayout")).interactable, Is.False);
        }

        private static EliteDrumsLayoutPreset TenLaneLayout()
        {
            var layout = EliteDrumsLayoutPreset.Full;
            layout.Name = "Ten lane test layout";
            layout.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            layout.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            return layout;
        }

        private static void ConfigurePreviewOptions(object menu, EliteDrumsLayoutPreset[] layouts,
            ColorProfile[] colors)
        {
            var layoutDropdown = Field(menu, "_layoutDropdown");
            menu.GetType().GetField("_layouts", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(menu, layouts);
            SetDropdownOptions(layoutDropdown, layouts.Select(layout => layout.Name).ToList());
            var colorDropdown = Field(menu, "_previewColor");
            menu.GetType().GetField("_previewColors", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(menu, colors);
            SetDropdownOptions(colorDropdown, colors.Select(color => color.Name).ToList());
        }

        private static void SetDropdownOptions(object dropdown, List<string> options)
        {
            var type = dropdown.GetType();
            type.GetMethod("ClearOptions", Type.EmptyTypes).Invoke(dropdown, null);
            type.GetMethod("AddOptions", new[] { typeof(List<string>) }).Invoke(dropdown, new object[] { options });
        }

        private static void SelectDropdown(object dropdown, int index)
        {
            var type = dropdown.GetType();
            type.GetMethod("SetValueWithoutNotify", new[] { typeof(int) }).Invoke(dropdown, new object[] { index });
            var changed = type.GetProperty("onValueChanged").GetValue(dropdown);
            changed.GetType().GetMethod("Invoke").Invoke(changed, new object[] { index });
        }

        private static void SelectPreviewLayout(object menu, int index) =>
            SelectDropdown(Field(menu, "_layoutDropdown"), index);

        private static void SelectPreviewColor(object menu, int index) =>
            SelectDropdown(Field(menu, "_previewColor"), index);

        private static Component PreviewPlayer(Transform world) => world.GetComponentInChildren(
            Production("YARG.Settings.Preview.FakeTrackPlayer"), true);

        private static Component[] PreviewFrets(Component player) => player.GetComponentsInChildren(
            Production("YARG.Gameplay.Visuals.Fret"), true);

        private static Material FretMaterial(Component fret)
        {
            var bind = fret.GetType().GetProperty("ThemeBind").GetValue(fret);
            return ((System.Collections.IEnumerable)bind.GetType()
                .GetMethod("GetColoredMaterials").Invoke(bind, null)).Cast<Material>().First();
        }

        private static void AssertPreview(Component player, int fretCount, int savedSlot,
            System.Drawing.Color expectedSlotColor)
        {
            Assert.That(player, Is.Not.Null);
            var frets = PreviewFrets(player);
            Assert.That(frets.Length, Is.EqualTo(fretCount), "preview should contain one real receptor per resolved lane");
            Assert.That(frets.Select(fret => fret.GetInstanceID()).Distinct().Count(), Is.EqualTo(fretCount));
            var selectedLane = frets.Single(fret => Mathf.Abs(fret.transform.localPosition.x -
                (-1f + (savedSlot + 0.5f) * 2f / fretCount)) < 0.01f);
            var actual = FretMaterial(selectedLane).color;
            Assert.That(actual.r, Is.EqualTo(expectedSlotColor.R / 255f).Within(0.01f));
            Assert.That(actual.g, Is.EqualTo(expectedSlotColor.G / 255f).Within(0.01f));
            Assert.That(actual.b, Is.EqualTo(expectedSlotColor.B / 255f).Within(0.01f));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_dialogManager != null && (bool)_dialogManager.GetType().GetProperty("IsDialogShowing").GetValue(_dialogManager))
                _dialogManager.GetType().GetMethod("ClearDialog").Invoke(_dialogManager, null);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_menuHost != null) UnityEngine.Object.Destroy(_menuHost);
            if (_secondRoot != null) UnityEngine.Object.Destroy(_secondRoot);
            if (_themeRoot != null) UnityEngine.Object.Destroy(_themeRoot);
            if (_menuServicesRoot != null) UnityEngine.Object.Destroy(_menuServicesRoot);
            if (_menuCanvasHost != null) UnityEngine.Object.Destroy(_menuCanvasHost);
            if (_layoutRootProperty != null) _layoutRootProperty.SetValue(null, _previousLayoutRoot);
            if (_layoutContents != null)
            {
                _layoutContents.Clear();
                foreach (var preset in _previousLayouts ?? Array.Empty<object>()) _layoutContents.Add(preset);
            }
            if (!string.IsNullOrEmpty(_layoutRoot))
            {
                try { Directory.Delete(_layoutRoot, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (_dialogRoot != null) UnityEngine.Object.Destroy(_dialogRoot);
            _root = null;
            _secondRoot = null;
            _themeRoot = null;
            yield return null;

            try
            {
                if (_capturedSettings)
                {
                    var settingsManager = Production("YARG.Settings.SettingsManager");
                    var settingContainer = settingsManager.GetNestedType("SettingContainer", BindingFlags.Public);
                    settingsManager.GetProperty("Settings").GetSetMethod(true).Invoke(null,
                        new[] { _originalSettings });
                    settingContainer.GetField("IsInitialized", BindingFlags.Public | BindingFlags.Static)
                        .SetValue(null, _originalSettingsInitialized);
                }
            }
            finally
            {
                _capturedSettings = false;
                if (_ownsAudioManager)
                {
                    Production("YARG.Core.Audio.GlobalAudioHandler")
                        .GetMethod("Close", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                    _ownsAudioManager = false;
                }
            }
        }
    }
}
