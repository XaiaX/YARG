using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteConfigurationPointerPlayModeTests
    {
        private GameObject _canvasObject;
        private GameObject _eventSystemObject;
        private GameObject _managerObject;
        private GameObject _menuObject;
        private GameObject _parentMenuObject;
        private GameObject _parentCanvasObject;
        private GameObject _themeManagerObject;
        private EventSystem _eventSystem;
        private Camera _dropdownCamera;
        private RenderTexture _dropdownTarget;
        private object _originalSettings;
        private bool _originalSettingsInitialized;
        private bool _capturedSettings;
        private bool _ownsAudio;
        private Type _localizationType;
        private System.Collections.Generic.IDictionary<string, string> _localizationMap;
        private System.Collections.Generic.KeyValuePair<string, string>[] _previousLocalizations;
        private string _previousCulture;
        private GameObject _menuDataObject;
        private Component _menuData;
        private object _previousMenuColors;

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

            var themeManagerType = Production("YARG.Themes.ThemeManager");
            const BindingFlags STATIC = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            if (themeManagerType.GetProperty("Instance", STATIC).GetValue(null) == null)
            {
                _themeManagerObject = new GameObject("Elite configuration pointer test ThemeManager");
                _themeManagerObject.AddComponent(themeManagerType);
                yield return null;
            }

            Assert.That(themeManagerType.GetProperty("Instance", STATIC).GetValue(null), Is.Not.Null,
                "The production theme manager must be available before creating the pointer test menu.");

            // Match the graphics fixture: load the real language file, never replace
            // captions or option labels with test-authored strings.
            Production("YARG.Helpers.PathHelper").GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, null);
            _localizationType = Production("YARG.Localization.LocalizationManager");
            _localizationMap = (System.Collections.Generic.IDictionary<string, string>)_localizationType
                .GetField("_localizationMap", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _previousLocalizations = _localizationMap.ToArray();
            _previousCulture = (string)_localizationType.GetProperty("CultureCode").GetValue(null);
            var load = _localizationType.GetMethod("TryParseAndLoadLanguage", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)load.Invoke(null, new object[] { "en-US" }), Is.True,
                "The pointer fixture must load the production English dictionary.");
            Assert.That(_localizationMap.TryGetValue("Settings.PresetSetting.PreviewVisuals.Visuals.Name", out var caption), Is.True,
                "The production English dictionary must contain the preview-options caption key.");
            Assert.That(caption, Is.EqualTo("Preview Options"));
#if UNITY_EDITOR
            var menuDataType = Production("YARG.Menu.Data.MenuData");
            _menuData = (Component)menuDataType.GetProperty("Instance", STATIC).GetValue(null);
            if (_menuData == null)
            {
                _menuDataObject = new GameObject("Pointer capture production MenuData");
                _menuDataObject.SetActive(false);
                _menuData = _menuDataObject.AddComponent(menuDataType);
                menuDataType.GetField("_navigationIcons", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(_menuData, UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                        "Assets/Art/Menu/Data/NavigationIcons.asset"));
            }
            var colorsField = menuDataType.GetField("_colors", BindingFlags.Instance | BindingFlags.NonPublic);
            _previousMenuColors = colorsField.GetValue(_menuData);
            var colors = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Art/Menu/Data/MenuColors.asset");
            Assert.That(colors, Is.Not.Null, "Capture the production blue action palette, not prefab fallback green.");
            colorsField.SetValue(_menuData, colors);
            if (_menuDataObject != null) _menuDataObject.SetActive(true);
#endif
        }

        [UnityTest]
        public IEnumerator ProductionCellReceivesPointerClickThroughOwningCanvasRaycaster()
        {
#if UNITY_EDITOR
            _canvasObject = new GameObject("Elite configuration pointer test canvas",
                typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;

            _parentMenuObject = new GameObject("Hidden parent menu", typeof(RectTransform));
            _parentMenuObject.transform.SetParent(_canvasObject.transform, false);
            _parentCanvasObject = new GameObject("Parent menu canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _parentCanvasObject.transform.SetParent(_parentMenuObject.transform, false);
            var parentCanvas = _parentCanvasObject.GetComponent<Canvas>();
            parentCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            parentCanvas.overrideSorting = true;

            _eventSystemObject = new GameObject("Elite configuration pointer test event system",
                typeof(EventSystem), Production("UnityEngine.InputSystem.UI.InputSystemUIInputModule"));
            _eventSystem = _eventSystemObject.GetComponent<EventSystem>();

            _managerObject = new GameObject("Elite configuration pointer test MenuManager");
            var managerType = Production("YARG.Menu.MenuManager");
            var manager = (Behaviour)_managerObject.AddComponent(managerType);
            manager.enabled = false;
            yield return null;

            var managerMenu = managerType.GetNestedType("Menu");
            var eliteMenu = Enum.Parse(managerMenu, "EliteDrumsLayout");
            var menu = (Component)managerType.GetMethod("ShowMenu", new[] { managerMenu, typeof(Transform) })
                .Invoke(manager, new object[] { eliteMenu, parentCanvas.transform });
            _menuObject = menu.gameObject;
            Assert.That(_menuObject.transform.parent, Is.EqualTo(canvas.transform),
                "A menu requested under a nested menu Canvas must attach to the active root Canvas.");
            Assert.That(_menuObject.GetComponentInParent<Canvas>(), Is.SameAs(canvas));
            Assert.That(_menuObject.GetComponentInParent<GraphicRaycaster>(), Is.SameAs(canvas.GetComponent<GraphicRaycaster>()));

            _parentMenuObject.SetActive(false);
            Assert.That(_menuObject.activeInHierarchy, Is.True,
                "Hiding the parent menu must not deactivate the newly opened menu.");

            var preset = EliteDrumsLayoutPreset.Compact.CopyWithNewName("Pointer interaction test");
            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            var initialize = menuType.GetMethod("Initialize", new[]
            {
                typeof(EliteDrumsLayoutPreset), typeof(Action<EliteDrumsLayoutPreset>), typeof(Action)
            });
            initialize.Invoke(_menuObject.GetComponent(menuType), new object[] { preset, (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { }) });
            yield return null;

            var cellType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutCell");
            var cell = _menuObject.GetComponentsInChildren(cellType, true).Cast<Component>()
                .Single(candidate => (int)cellType.GetProperty("Column").GetValue(candidate) == 0 &&
                    (bool)cellType.GetProperty("Drum").GetValue(candidate));
            var button = (Button)cellType.GetField("_button", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cell);
            Assert.That(button, Is.Not.Null);
            Assert.That(button.interactable, Is.True);

            var rect = (RectTransform)button.transform;
            var screenPosition = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(_eventSystem) { position = screenPosition, button = PointerEventData.InputButton.Left };
            var raycastResults = new System.Collections.Generic.List<RaycastResult>();
            _eventSystem.RaycastAll(pointer, raycastResults);
            Assert.That(raycastResults, Is.Not.Empty, "The active canvas GraphicRaycaster must hit the real production cell.");
            var topResult = raycastResults[0].gameObject;
            Assert.That(topResult.transform.IsChildOf(button.transform) || topResult == button.gameObject,
                Is.True, "The top EventSystem raycast result must belong to the production cell Button hierarchy.");

            Assert.That(ExecuteEvents.ExecuteHierarchy(topResult, pointer, ExecuteEvents.pointerDownHandler), Is.Not.Null);
            Assert.That(ExecuteEvents.ExecuteHierarchy(topResult, pointer, ExecuteEvents.pointerUpHandler), Is.Not.Null);
            Assert.That(ExecuteEvents.ExecuteHierarchy(topResult, pointer, ExecuteEvents.pointerClickHandler), Is.Not.Null);
            yield return null;

            var configuration = _menuObject.GetComponent(menuType);
            var popup = (GameObject)menuType.GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(configuration);
            Assert.That(popup.activeSelf, Is.True,
                "A real pointer click on the top raycast result must open the production cell management popup.");
            Assert.That((int)menuType.GetField("_column", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(configuration),
                Is.EqualTo(0));
            Assert.That((bool)menuType.GetField("_drum", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(configuration),
                Is.True);
#else
            Assert.Ignore("Requires the production Elite Drums menu resource.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator RootCanvasMenuRemainsVisibleWhenPreviousMenuIsHidden()
        {
            _parentMenuObject = new GameObject("Profiles root Canvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _parentMenuObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var menuObjectType = Production("YARG.Menu.MenuObject");
            var previous = _parentMenuObject.AddComponent(menuObjectType);
            var managerType = Production("YARG.Menu.MenuManager");
            var menuType = managerType.GetNestedType("Menu");
            var profiles = Enum.Parse(menuType, "ProfileList");
            menuObjectType.GetMethod("Initialize").Invoke(previous, new[] { profiles });
            _managerObject = new GameObject("Root Canvas test manager");
            _parentMenuObject.transform.SetParent(_managerObject.transform, false);
            var manager = (Behaviour)_managerObject.AddComponent(managerType);
            manager.enabled = false;
            managerType.GetMethod("PushMenu").Invoke(manager, new object[] { profiles, true });
            var elite = Enum.Parse(menuType, "EliteDrumsLayout");
            var opened = (Component)managerType.GetMethod("ShowMenu", new[] { menuType, typeof(Transform) })
                .Invoke(manager, new object[] { elite, canvas.transform });
            _menuObject = opened.gameObject;
            yield return null;
            Assert.That(_parentMenuObject.activeSelf, Is.False);
            Assert.That(_menuObject.activeInHierarchy, Is.True,
                "Configure must not be hidden along with the Profiles root Canvas.");
            Assert.That(_menuObject.GetComponentInParent<Canvas>().isActiveAndEnabled, Is.True);
            var configurationType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            var configuration = _menuObject.GetComponent(configurationType);
            configurationType.GetMethod("Initialize", new[]
            {
                typeof(EliteDrumsLayoutPreset), typeof(Action<EliteDrumsLayoutPreset>), typeof(Action)
            }).Invoke(configuration, new object[]
            {
                EliteDrumsLayoutPreset.Compact, (Action<EliteDrumsLayoutPreset>)(_ => { }),
                (Action)(() => managerType.GetMethod("PopMenu").Invoke(manager, null))
            });
            for (int frame = 0; frame < 10; frame++) yield return null;
            ScreenCapture.CaptureScreenshot("/tmp/yarg-configure-root-canvas.png");
            for (int frame = 0; frame < 5; frame++) yield return null;
            var back = (Button)configurationType.GetField("_back", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(configuration);
            back.onClick.Invoke();
            Assert.That(_parentMenuObject.activeInHierarchy, Is.True);
            Assert.That(_menuObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator ProductionDropdownListsHaveReadableOnScreenItemsAndPointerSelectionFeedback()
        {
            foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) })
            {
                _dropdownTarget = new RenderTexture(resolution.x, resolution.y, 24);
                Assert.That(_dropdownTarget.Create(), Is.True);
                yield return CheckProductionDropdownLists();
                UnityEngine.Object.Destroy(_menuObject);
                UnityEngine.Object.Destroy(_canvasObject);
                UnityEngine.Object.Destroy(_eventSystemObject);
                _dropdownCamera.targetTexture = null;
                UnityEngine.Object.Destroy(_dropdownCamera.gameObject);
                _dropdownTarget.Release();
                UnityEngine.Object.Destroy(_dropdownTarget);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ToolbarDropdownsOpenAndSelectAtReferenceResolutions()
        {
            yield return ProductionDropdownListsHaveReadableOnScreenItemsAndPointerSelectionFeedback();
        }

        private void CaptureDropdownState(string state)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(_dropdownTarget.width, _dropdownTarget.height, TextureFormat.RGBA32, false);
            try
            {
                _dropdownCamera.Render();
                RenderTexture.active = _dropdownTarget;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                image.Apply();
                string path = $"/tmp/yarg-toolbar-{state}-{image.width}x{image.height}.png";
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log("Toolbar state screenshot: " + path);
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(image);
            }
        }

        private IEnumerator CheckProductionDropdownLists()
        {
            _canvasObject = new GameObject("Production dropdown test canvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasObject.GetComponent<Canvas>();
            _dropdownCamera = new GameObject("Dropdown render target camera", typeof(Camera)).GetComponent<Camera>();
            _dropdownCamera.orthographic = true;
            _dropdownCamera.targetTexture = _dropdownTarget;
            _dropdownCamera.aspect = (float)_dropdownTarget.width / _dropdownTarget.height;
            _dropdownCamera.pixelRect = new Rect(0, 0, _dropdownTarget.width, _dropdownTarget.height);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _dropdownCamera;
            canvas.planeDistance = 1f;
            // CanvasScaler reads the Editor GameView dimensions even when a camera
            // renders to a texture. Apply the production width-based reference scale
            // explicitly to the controlled target instead of the unrelated GameView.
            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            canvas.scaleFactor = _dropdownTarget.width / 1920f;
            Canvas.ForceUpdateCanvases();
            Assert.That(_dropdownCamera.pixelWidth, Is.EqualTo(_dropdownTarget.width));
            Assert.That(_dropdownCamera.pixelHeight, Is.EqualTo(_dropdownTarget.height));
            Assert.That(canvas.pixelRect.width, Is.EqualTo(_dropdownTarget.width).Within(1f));
            Assert.That(canvas.pixelRect.height, Is.EqualTo(_dropdownTarget.height).Within(1f));
            _eventSystemObject = new GameObject("Production dropdown event system", typeof(EventSystem),
                Production("UnityEngine.InputSystem.UI.InputSystemUIInputModule"));
            _eventSystem = _eventSystemObject.GetComponent<EventSystem>();
            var prefab = Resources.Load<GameObject>("EliteDrumsLayoutMenu");
            Assert.That(prefab, Is.Not.Null);
            _menuObject = UnityEngine.Object.Instantiate(prefab, canvas.transform, false);
            _menuObject.SetActive(true);
            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            var menu = _menuObject.GetComponent(menuType);
            menuType.GetMethod("Initialize", new[]
            {
                typeof(EliteDrumsLayoutPreset), typeof(Action<EliteDrumsLayoutPreset>), typeof(Action)
            }).Invoke(menu, new object[]
            {
                EliteDrumsLayoutPreset.Compact.CopyWithNewName("Opened dropdown test"),
                (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { })
            });
            yield return null;
            const BindingFlags FIELDS = BindingFlags.Instance | BindingFlags.NonPublic;
            var rows = (System.Collections.IEnumerable)menuType.GetMethod("GetNavigationRows", FIELDS)
                .Invoke(menu, null);
            var reachable = rows.Cast<System.Collections.IEnumerable>()
                .SelectMany(row => row.Cast<Component>()).ToArray();
            Assert.That(reachable, Is.Not.Empty, "Initialization must produce usable navigation rows.");
            foreach (var name in new[] { "_back", "_layoutDropdown", "_openLayoutFolder", "_previewColor", "_previewOptions", "_copyLayout", "_importLayout" })
            {
                var control = (Component)menuType.GetField(name, FIELDS).GetValue(menu);
                var selectable = control as Selectable;
                bool enabled = selectable != null && selectable.IsInteractable() && control.gameObject.activeInHierarchy;
                Assert.That(reachable.Any(target => target.gameObject == control.gameObject), Is.EqualTo(enabled),
                    name + " must be in the initialized navigation rows exactly when enabled.");
            }
            foreach (var field in new[] { "_shiftLeft", "_shiftRight", "_reverseLayout" })
            {
                var action = (Button)menuType.GetField(field, FIELDS).GetValue(menu);
                Assert.That(action.gameObject.activeInHierarchy, Is.True, "Editable layouts expose " + field);
                // Compact has an assigned first column and an empty last column.
                // Shifting left must not discard that assignment; right/reverse are safe.
                bool expectedEnabled = field != "_shiftLeft";
                var palette = _menuData.GetType().GetField("_colors", FIELDS).GetValue(_menuData);
                var blue = (Color)palette.GetType().GetField("BrightButton").GetValue(palette);
                var colored = action.GetComponentInParent(Production("YARG.Menu.ColoredButton"));
                Assert.That((Color)colored.GetType().GetProperty("BackgroundColor").GetValue(colored),
                    Is.EqualTo(expectedEnabled ? blue : Color.gray), "Capture runtime-applied action palette: " + field);
                Assert.That(action.IsInteractable(), Is.EqualTo(expectedEnabled),
                    "Compact must disable destructive left shift and enable safe transformations: " + field);
                Assert.That(reachable.Any(target => target.gameObject == action.gameObject), Is.EqualTo(expectedEnabled),
                    "Only enabled transform actions belong to keyboard/controller navigation: " + field);
            }
            var builtInNotice = (GameObject)menuType.GetField("_builtInNotice", FIELDS).GetValue(menu);
            Assert.That(builtInNotice.activeInHierarchy, Is.False, "Actions and the built-in notice are mutually exclusive.");
            CaptureDropdownState("editable-actions");
            CaptureDropdownState("initial");
            foreach (string field in new[] { "_layoutDropdown", "_previewColor", "_previewOptions" })
            {
                var dropdown = (Component)menuType.GetField(field, FIELDS).GetValue(menu);
                var caption = (Component)DropdownProperty(dropdown, field == "_previewOptions" ? "placeholder" : "captionText");
                AssertReadableText(caption);
                if (field == "_previewOptions")
                    Assert.That((string)DropdownProperty(caption, "text"), Is.EqualTo("Preview Options"),
                        "The actual production placeholder must remain localized after activation and its first player-loop frame.");
                dropdown.GetType().GetMethod("Show", Type.EmptyTypes).Invoke(dropdown, null);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var list = dropdown.transform.Find("Dropdown List");
                Assert.That(list, Is.Not.Null, field + " must instantiate the production dropdown list.");
                if (field == "_layoutDropdown")
                {
                    var closed = ScreenBounds((RectTransform)dropdown.transform);
                    var opened = ScreenBounds((RectTransform)list);
                    Assert.That(opened.xMin, Is.EqualTo(closed.xMin).Within(1f), "Layout list left edge follows closed control.");
                    Assert.That(opened.xMax, Is.EqualTo(closed.xMax).Within(1f), "Layout list right edge follows closed control.");
                }
                var toggles = list.GetComponentsInChildren<Toggle>().Where(toggle => toggle.gameObject.activeInHierarchy).ToArray();
                Assert.That(toggles, Is.Not.Empty);
                if (field == "_previewOptions")
                {
                    var labels = toggles.Select(toggle => (string)DropdownProperty(
                        toggle.GetComponentsInChildren(Production("TMPro.TMP_Text")).Cast<Component>().Single(), "text")).ToArray();
                    foreach (string expected in new[] { "Star Power Notes", "Star Power Active", "Groove", "Lefty Flip" })
                        Assert.That(labels.Any(label => label.Contains(expected)), Is.True,
                            "The opened production list must contain the real option label: " + expected);
                }
                var viewport = list.GetComponentInChildren<ScrollRect>().viewport;
                int visible = 0;
                foreach (var toggle in toggles)
                {
                    var rect = (RectTransform)toggle.transform;
                    var center = RectTransformUtility.WorldToScreenPoint(_dropdownCamera, rect.TransformPoint(rect.rect.center));
                    if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, center, _dropdownCamera)) continue;
                    visible++;
                    var text = toggle.GetComponentsInChildren(Production("TMPro.TMP_Text")).Cast<Component>().Single();
                    AssertReadableText(text);
                    if (field != "_previewOptions")
                    {
                        var labelBounds = ScreenBounds((RectTransform)text.transform);
                        var dotBounds = ScreenBounds(toggle.graphic.rectTransform);
                        Assert.That(labelBounds.xMin, Is.GreaterThan(dotBounds.xMax + 4f * _canvasObject.GetComponent<Canvas>().scaleFactor),
                            "Every item reserves a separate dot gutter, even when its dot is hidden.");
                    }
                    var bounds = ScreenBounds(rect);
                    Assert.That(bounds.height, Is.GreaterThanOrEqualTo(20f), "Visible production item must have a usable transformed hit height.");
                    Assert.That(bounds.width, Is.GreaterThanOrEqualTo(80f));
                    AssertPointerTarget(toggle.gameObject, center);
                }
                Assert.That(visible, Is.GreaterThan(0), "The opened list must expose selectable items inside its viewport.");
                CaptureDropdownState(field.TrimStart('_') + "-open");
                if (field == "_previewColor" || field == "_layoutDropdown")
                {
                    var selected = toggles.First(toggle =>
                    {
                        var itemRect = (RectTransform)toggle.transform;
                        var point = RectTransformUtility.WorldToScreenPoint(_dropdownCamera, itemRect.TransformPoint(itemRect.rect.center));
                        return !toggle.isOn && RectTransformUtility.RectangleContainsScreenPoint(viewport, point, _dropdownCamera);
                    });
                    var label = selected.GetComponentsInChildren(Production("TMPro.TMP_Text")).Cast<Component>().Single();
                    string expectedCaption = (string)DropdownProperty(label, "text");
                    var selectedRect = (RectTransform)selected.transform;
                    var point = RectTransformUtility.WorldToScreenPoint(_dropdownCamera, selectedRect.TransformPoint(selectedRect.rect.center));
                    var target = AssertPointerTarget(selected.gameObject, point);
                    var pointer = new PointerEventData(_eventSystem) { position = point, button = PointerEventData.InputButton.Left };
                    Assert.That(ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler), Is.Not.Null);
                    yield return new WaitForSecondsRealtime(0.2f);
                    Assert.That((string)DropdownProperty(caption, "text"), Is.EqualTo(expectedCaption),
                        "Selecting a production dropdown item must update the visible caption.");
                    AssertReadableText(caption);
                    CaptureDropdownState(field.TrimStart('_') + "-selected");
                }
                if (field == "_previewOptions")
                {
                    for (int selection = 0; selection < 2; selection++)
                    {
                        var first = dropdown.transform.Find("Dropdown List").GetComponentsInChildren<Toggle>()
                            .First(toggle => toggle.gameObject.activeInHierarchy);
                        var text = first.GetComponentsInChildren(Production("TMPro.TMP_Text")).Cast<Component>().Single();
                        string before = (string)DropdownProperty(text, "text");
                        Assert.That(before, Does.Contain(selection == 0 ? "Off" : "On"));
                        var rect = (RectTransform)first.transform;
                        var center = RectTransformUtility.WorldToScreenPoint(_dropdownCamera, rect.TransformPoint(rect.rect.center));
                        var target = AssertPointerTarget(first.gameObject, center);
                        var pointer = new PointerEventData(_eventSystem) { position = center, button = PointerEventData.InputButton.Left };
                        Assert.That(ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler), Is.Not.Null);
                        // TMP_Dropdown destroys its list after its unscaled fade interval.
                        yield return new WaitForSecondsRealtime(0.2f);
                        dropdown.GetType().GetMethod("Show", Type.EmptyTypes).Invoke(dropdown, null);
                        yield return null;
                        var updated = dropdown.transform.Find("Dropdown List").GetComponentsInChildren<Toggle>()
                            .First(toggle => toggle.gameObject.activeInHierarchy);
                        var updatedText = updated.GetComponentsInChildren(Production("TMPro.TMP_Text")).Cast<Component>().Single();
                        Assert.That((string)DropdownProperty(updatedText, "text"), Does.Contain(selection == 0 ? "On" : "Off"),
                            "Selecting the actual production item must update its displayed state on reopening.");
                        CaptureDropdownState(selection == 0 ? "options-on" : "options-off");
                    }
                }
                dropdown.GetType().GetMethod("Hide", Type.EmptyTypes).Invoke(dropdown, null);
                yield return new WaitForSecondsRealtime(0.2f);
            }
        }

        private static object DropdownProperty(Component component, string property) => component.GetType()
            .GetProperty(property, BindingFlags.Instance | BindingFlags.Public).GetValue(component);

        private Rect ScreenBounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var points = corners.Select(corner => RectTransformUtility.WorldToScreenPoint(_dropdownCamera, corner)).ToArray();
            return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y),
                points.Max(point => point.x), points.Max(point => point.y));
        }

        private void AssertReadableText(Component text)
        {
            Assert.That(text, Is.Not.Null);
            Assert.That(text.gameObject.activeInHierarchy, Is.True);
            text.GetType().GetMethod("ForceMeshUpdate", new[] { typeof(bool), typeof(bool) })
                .Invoke(text, new object[] { false, false });
            var rect = (RectTransform)text.transform;
            var bounds = ScreenBounds(rect);
            Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(-1f));
            Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(-1f));
            Assert.That(bounds.xMax, Is.LessThanOrEqualTo(_dropdownTarget.width + 1f));
            Assert.That(bounds.yMax, Is.LessThanOrEqualTo(_dropdownTarget.height + 1f));
            Assert.That((string)DropdownProperty(text, "text"), Is.Not.Empty);
            var rendered = (Bounds)DropdownProperty(text, "textBounds");
            float renderedHeight = Vector2.Distance(
                RectTransformUtility.WorldToScreenPoint(_dropdownCamera, rect.TransformPoint(rendered.min)),
                RectTransformUtility.WorldToScreenPoint(_dropdownCamera,
                    rect.TransformPoint(rendered.min + Vector3.up * rendered.size.y)));
            Assert.That(renderedHeight, Is.GreaterThanOrEqualTo(9f), "Actual transformed glyph bounds must remain readable.");
            string path = string.Join("/", text.GetComponentsInParent<Transform>(true).Reverse().Select(parent => parent.name));
            string diagnostic = $"target={_dropdownTarget.width}x{_dropdownTarget.height} path={path} " +
                $"text='{DropdownProperty(text, "text")}' font={DropdownProperty(text, "fontSize")} " +
                $"localRect={rect.rect} pixels={bounds} glyphHeight={renderedHeight:F2} " +
                $"preferredWidth={DropdownProperty(text, "preferredWidth")} preferredHeight={DropdownProperty(text, "preferredHeight")}";
            Debug.Log("[Production dropdown readability] " + diagnostic);
            Assert.That((bool)DropdownProperty(text, "isTextTruncated"), Is.False,
                "Production caption/item text must fit rather than silently ellipsize: " + diagnostic);
        }

        private GameObject AssertPointerTarget(GameObject owner, Vector2 center)
        {
            var pointer = new PointerEventData(_eventSystem) { position = center };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            _eventSystem.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            var target = hits[0].gameObject;
            Assert.That(target == owner || target.transform.IsChildOf(owner.transform), Is.True,
                "The top raycast at the item's center must belong to the production item, not its blocker or underlying menu.");
            return target;
        }

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_dropdownCamera != null)
            {
                _dropdownCamera.targetTexture = null;
                UnityEngine.Object.Destroy(_dropdownCamera.gameObject);
            }
            if (_dropdownTarget != null)
            {
                _dropdownTarget.Release();
                UnityEngine.Object.Destroy(_dropdownTarget);
            }
            if (_menuObject != null) UnityEngine.Object.Destroy(_menuObject);
            if (_managerObject != null) UnityEngine.Object.Destroy(_managerObject);
            if (_themeManagerObject != null) UnityEngine.Object.Destroy(_themeManagerObject);
            if (_eventSystemObject != null) UnityEngine.Object.Destroy(_eventSystemObject);
            if (_parentMenuObject != null) UnityEngine.Object.Destroy(_parentMenuObject);
            if (_canvasObject != null) UnityEngine.Object.Destroy(_canvasObject);
            yield return null;
            if (_menuDataObject != null)
            {
                UnityEngine.Object.Destroy(_menuDataObject);
                yield return null;
            }
            else if (_menuData != null)
                _menuData.GetType().GetField("_colors", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(_menuData, _previousMenuColors);
            _menuData = null;

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
    }
}
