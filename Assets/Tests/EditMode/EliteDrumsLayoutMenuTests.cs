using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class EliteDrumsLayoutMenuTests
    {
        private GameObject _instance;
        private GameObject _testCanvasObject;
        private Camera _testCamera;
        private RenderTexture _testTarget;
        private RectTransform _helpBar;
        private UnityEngine.SceneManagement.Scene _persistentScene;
        private Component _menu;
        private Component _layoutDropdown;
        private Type _menuType;
        private Type _localizationType;
        private IDictionary<string, string> _localizationMap;
        private KeyValuePair<string, string>[] _previousLocalizations;
        private string _previousCulture;
        private const string PREFAB_PATH = "Assets/Resources/EliteDrumsLayoutMenu.prefab";

        private static object Field(object value, string name) => value.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        private static object Call(object value, string name, params object[] args)
        {
            var method = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == args.Length &&
                    candidate.GetParameters().Select((parameter, index) => args[index] == null ||
                        parameter.ParameterType.IsInstanceOfType(args[index])).All(matches => matches));
            return method.Invoke(value, args);
        }

        private static void AssertRectContained(RectTransform item, RectTransform container, string description)
        {
            var corners = new Vector3[4];
            item.GetWorldCorners(corners);
            var bounds = corners.Select(container.InverseTransformPoint).Select(point => (Vector2)point).ToArray();
            float minX = bounds.Min(point => point.x);
            float maxX = bounds.Max(point => point.x);
            float minY = bounds.Min(point => point.y);
            float maxY = bounds.Max(point => point.y);
            var rect = container.rect;
            Assert.That(minX, Is.GreaterThanOrEqualTo(rect.xMin - 1f), $"{description} left edge must be in preview pane.");
            Assert.That(maxX, Is.LessThanOrEqualTo(rect.xMax + 1f), $"{description} right edge must be in preview pane.");
            Assert.That(minY, Is.GreaterThanOrEqualTo(rect.yMin - 1f), $"{description} lower edge must be in preview pane.");
            Assert.That(maxY, Is.LessThanOrEqualTo(rect.yMax + 1f), $"{description} upper edge must be in preview pane.");
        }

        private static Rect ViewportBounds(RectTransform item)
        {
            var canvas = item.GetComponentInParent<Canvas>().rootCanvas;
            var viewport = (RectTransform)canvas.transform;
            var rect = viewport.rect;
            Assert.That(rect.width, Is.GreaterThan(0f));
            Assert.That(rect.height, Is.GreaterThan(0f));
            var corners = new Vector3[4];
            item.GetWorldCorners(corners);
            var points = corners.Select(viewport.InverseTransformPoint)
                .Select(point => new Vector2((point.x - rect.xMin) / rect.width,
                    (point.y - rect.yMin) / rect.height)).ToArray();
            return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y),
                points.Max(point => point.x), points.Max(point => point.y));
        }

        // TMP_Text is not referenced by this assembly; read label text reflectively.
        private static string Text(object tmp) => (string) tmp.GetType().GetProperty("text").GetValue(tmp);
        private static string LocalizedKey(string key) => (string) AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("YARG.Localization.Localize")).First(type => type != null)
            .GetMethod("Key", new[] { typeof(string) }).Invoke(null, new object[] { key });
        private static Type TmpTextType => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("TMPro.TMP_Text")).First(type => type != null);
        private string ChildText(string objectName) => Text(_instance.transform.Find(objectName).GetComponent(TmpTextType));
        private static string ButtonText(Button button) => Text(button.transform.parent.GetComponentInChildren(TmpTextType, true));

        [SetUp]
        public void MountProductionPrefab()
        {
            LoadEnglishLocalization();
            _persistentScene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/PersistentScene.unity");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert.That(prefab, Is.Not.Null, "Generate the production layout prefab before running menu tests.");
            _testCanvasObject = new GameObject("Elite layout geometry test Canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = _testCanvasObject.GetComponent<Canvas>();
            _testTarget = new RenderTexture(1280, 720, 0);
            _testCamera = new GameObject("Elite layout geometry camera", typeof(Camera)).GetComponent<Camera>();
            _testCamera.enabled = false;
            _testCamera.orthographic = true;
            _testCamera.orthographicSize = 360f;
            _testCamera.targetTexture = _testTarget;
            _testCamera.aspect = 1280f / 720f;
            _testCamera.pixelRect = new Rect(0f, 0f, 1280f, 720f);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _testCamera;
            canvas.planeDistance = 1f;
            // Persistent Canvas uses a 1920x1080 reference resolution (Scale With Screen Size,
            // Expand). At this matching aspect ratio, 1280x720 pixels represent 1920x1080 UI units.
            canvas.scaleFactor = 1280f / 1920f;
            ((RectTransform)_testCanvasObject.transform).sizeDelta = new Vector2(1920f, 1080f);
            // Copy the actual authored footer bounds into the controlled viewport without
            // activating Help Bar behaviours or relying on the preview scene's zero-sized canvas.
            _helpBar = _persistentScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                .First(rect => rect.name == "Help Bar");
            var footerAnchorMin = _helpBar.anchorMin;
            var footerAnchorMax = _helpBar.anchorMax;
            var footerPosition = _helpBar.anchoredPosition;
            var footerSize = _helpBar.sizeDelta;
            var footerPivot = _helpBar.pivot;
            _helpBar = new GameObject("Authored persistent Help Bar bounds", typeof(RectTransform))
                .GetComponent<RectTransform>();
            _helpBar.SetParent(_testCanvasObject.transform, false);
            _helpBar.anchorMin = footerAnchorMin;
            _helpBar.anchorMax = footerAnchorMax;
            _helpBar.pivot = footerPivot;
            _helpBar.sizeDelta = footerSize;
            _helpBar.anchoredPosition = footerPosition;
            Canvas.ForceUpdateCanvases();
            _instance = UnityEngine.Object.Instantiate(prefab, _testCanvasObject.transform, false);
            ((RectTransform)_instance.transform).sizeDelta = Vector2.zero;
            _menuType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(
                "YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu")).First(type => type != null);
            _menu = _instance.GetComponent(_menuType);
            // EditMode mounting does not dispatch MonoBehaviour.Awake. Avoid rendering the game preview;
            // the preview has separate PlayMode coverage and its camera requires runtime textures.
            var previewWorld = _menuType.GetField("_previewWorld", BindingFlags.Instance | BindingFlags.NonPublic);
            previewWorld.SetValue(_menu, null);
            var dropdownField = _menuType.GetField("_layoutDropdown", BindingFlags.Instance | BindingFlags.NonPublic);
            _layoutDropdown = (Component)dropdownField.GetValue(_menu);
            dropdownField.SetValue(_menu, null);
            _menuType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_menu, null);
        }

        [TearDown]
        public void Unmount()
        {
            if (_testCanvasObject != null) UnityEngine.Object.DestroyImmediate(_testCanvasObject);
            if (_testCamera != null)
            {
                _testCamera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(_testCamera.gameObject);
            }
            if (_testTarget != null)
            {
                _testTarget.Release();
                UnityEngine.Object.DestroyImmediate(_testTarget);
            }
            if (_persistentScene.IsValid()) EditorSceneManager.ClosePreviewScene(_persistentScene);
            if (_localizationMap == null) return;
            _localizationMap.Clear();
            foreach (var entry in _previousLocalizations) _localizationMap.Add(entry.Key, entry.Value);
            _localizationType.GetProperty("CultureCode").GetSetMethod(true).Invoke(null, new object[] { _previousCulture });
        }

        private void LoadEnglishLocalization()
        {
            _localizationType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                assembly.GetType("YARG.Localization.LocalizationManager")).First(type => type != null);
            var localizationType = _localizationType;
            _localizationMap = (IDictionary<string, string>)localizationType
                .GetField("_localizationMap", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _previousLocalizations = _localizationMap.ToArray();
            _previousCulture = (string)localizationType.GetProperty("CultureCode").GetValue(null);

            var pathHelperType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                assembly.GetType("YARG.Helpers.PathHelper")).First(type => type != null);
            var streamingAssetsPath = (string)pathHelperType.GetProperty("StreamingAssetsPath",
                BindingFlags.Static | BindingFlags.Public).GetValue(null);
            Assert.That(streamingAssetsPath, Is.Not.Null.And.Not.Empty,
                "PathHelper must use Unity's active StreamingAssets directory.");
            var load = localizationType.GetMethod("TryParseAndLoadLanguage", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)load.Invoke(null, new object[] { "en-US" }), Is.True,
                "The production English dictionary must load from the active StreamingAssets path.");
        }

        private void Initialize(EliteDrumsLayoutPreset preset, Action<EliteDrumsLayoutPreset> save = null) =>
            Call(_menu, "Initialize", preset, save, (Action)(() => { }));

        [TestCase(8, "kick2", EliteDrumsLayoutSlotKind.Kick2)]
        [TestCase(9, "kick1", EliteDrumsLayoutSlotKind.Kick1)]
        public void KickPopupCommitsSwapThenConsolidationWithoutMutatingInput(int column, string id, EliteDrumsLayoutSlotKind kind)
        {
            var original = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Editable");
            original = EliteDrumsLayoutEditor.PlaceKick(original, 8, EliteDrumsLayoutSlotKind.Kick);
            original = EliteDrumsLayoutEditor.PlaceKick(original, 9, EliteDrumsLayoutSlotKind.Kick2);
            Initialize(original);
            Call(_menu, "OpenCell", column, true);
            var ids = (string[])Field(_menu, "_choiceIds");
            Assert.That(ids.Where(s => s.StartsWith("kick")), Is.EqualTo(new[] { "kick", id }));
            _menuType.GetMethod("Choose", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_menu, new object[] { Array.IndexOf(ids, id) });
            var draft = (EliteDrumsLayoutPreset)_menuType.GetProperty("Draft").GetValue(_menu);
            Assert.That(draft.Slots[column].Kind, Is.EqualTo(kind));
            Assert.That(draft.Slots[column == 8 ? 9 : 8].Kind, Is.EqualTo(
                kind == EliteDrumsLayoutSlotKind.Kick1 ? EliteDrumsLayoutSlotKind.Kick2 : EliteDrumsLayoutSlotKind.Kick1));
            Call(_menu, "OpenCell", column, true);
            ids = (string[])Field(_menu, "_choiceIds");
            _menuType.GetMethod("Choose", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_menu, new object[] { Array.IndexOf(ids, "kick") });
            draft = (EliteDrumsLayoutPreset)_menuType.GetProperty("Draft").GetValue(_menu);
            Assert.That(draft.Slots[column].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick));
            Assert.That(draft.Slots[column == 8 ? 9 : 8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
            Assert.That(draft.Slots.Take(8).Select(s => (s.Kind, s.Drum, s.Cymbal)),
                Is.EqualTo(original.Slots.Take(8).Select(s => (s.Kind, s.Drum, s.Cymbal))));
            Assert.That(original.Slots[8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick1));
        }

        [Test]
        public void PiecePopupUsesRowSpecificNamesAndRuntimeActionPalette()
        {
            var dataType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("YARG.Menu.Data.MenuData"))
                .First(t => t != null);
            var sceneData = _persistentScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren(dataType, true)).Single();
            Assert.That(Field(sceneData, "_colors"), Is.Not.Null,
                "Use the actual PersistentScene menu palette, not synthetic test colors.");
            // Preview scenes do not run Awake; bind their authored data only for this test.
            var instanceProperty = dataType.BaseType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var previousInstance = instanceProperty.GetValue(null);
            instanceProperty.GetSetMethod(true).Invoke(null, new object[] { sceneData });
            try
            {
                Initialize((EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("Editable"));
                Call(_menu, "OpenCell", 8, false);
                var ids = (string[])Field(_menu, "_choiceIds");
                var labels = (Array)Field(_menu, "_choiceLabels");
                Assert.That(Text(labels.GetValue(Array.IndexOf(ids, "ride"))), Is.EqualTo("RIDE CYMBAL"));
                Assert.That(Text(labels.GetValue(Array.IndexOf(ids, "hi-hat"))), Is.EqualTo("HI-HAT CYMBAL"));
                Assert.That(Text(labels.GetValue(Array.IndexOf(ids, "kick"))), Is.EqualTo("ALL KICKS"));
                var palette = dataType.GetProperty("Colors", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                var buttons = (Button[])Field(_menu, "_choices");
                var clear = buttons[Array.IndexOf(ids, "clear")];
                var clearColor = clear.GetComponentsInParent<Component>(true)
                    .First(c => c.GetType().Name == "ColoredButton");
                var background = clearColor.GetType().GetProperty("BackgroundColor");
                Assert.That(clear.interactable, Is.False, "An empty row cannot be cleared.");
                Assert.That(background.GetValue(clearColor), Is.EqualTo(Color.gray));
                var cancel = buttons[Array.IndexOf(ids, "cancel")];
                var cancelColor = cancel.GetComponentsInParent<Component>(true)
                    .First(c => c.GetType().Name == "ColoredButton");
                Assert.That(cancelColor.GetType().GetProperty("BackgroundColor").GetValue(cancelColor),
                    Is.EqualTo(palette.GetType().GetField("NavigationYellow").GetValue(palette)));
                Call(_menu, "CancelPopup");
                var draft = (EliteDrumsLayoutPreset)_menuType.GetProperty("Draft").GetValue(_menu);
                Assert.That(draft.Slots[2].Cymbal, Is.EqualTo("left-crash"),
                    "Full assigns a cymbal at column 2; column 0 only has a drum.");
                Call(_menu, "OpenCell", 2, false);
                ids = (string[])Field(_menu, "_choiceIds");
                clear = buttons[Array.IndexOf(ids, "clear")];
                clearColor = clear.GetComponentsInParent<Component>(true)
                    .First(c => c.GetType().Name == "ColoredButton");
                Assert.That(clear.interactable, Is.True, "An assigned row can be cleared.");
                Assert.That(background.GetValue(clearColor),
                    Is.EqualTo(palette.GetType().GetField("BrightButton").GetValue(palette)));
                Call(_menu, "CancelPopup");
                Call(_menu, "OpenCell", 8, true);
                ids = (string[])Field(_menu, "_choiceIds");
                Assert.That(Text(labels.GetValue(Array.IndexOf(ids, "hi-hat"))), Is.EqualTo("HI-HAT PEDAL"));
            }
            finally
            {
                instanceProperty.GetSetMethod(true).Invoke(null, new[] { previousInstance });
            }
        }

        [Test]
        public void ProductionPrefabContainsLocalizedFixedLabelsAndTwentyCells()
        {
            Assert.That(((Array)Field(_menu, "_cells")).Length, Is.EqualTo(20));
            Assert.That(_instance.transform.Find("Header/Background"), Is.Not.Null,
                "The shared header provides its own background/divider visual.");
            Assert.That(_instance.transform.Find("Header/Button"), Is.Not.Null,
                "The shared header's authored back button must be present.");
            Assert.That(ChildText("Header/Single Header Text"), Is.EqualTo(LocalizedKey("Menu.EliteLayout.Title")));
            Assert.That(_instance.transform.Find("Cymbals"), Is.Null,
                "Large row headings are intentionally omitted; cell rows are visually self-evident.");
            Assert.That(_instance.transform.Find("Drums"), Is.Null,
                "Large row headings are intentionally omitted; cell rows are visually self-evident.");
            Assert.That(_instance.transform.Find("Discard Confirmation"), Is.Null,
                "Discard confirmation must use the shared dialog rather than a local panel.");
            Assert.That(LocalizedKey("Menu.EliteLayout.DiscardMessage"),
                Is.EqualTo("Your unsaved layout changes will be lost."));
            Assert.That(Text(Field(_menu, "_colorsNotice")), Does.Contain("Layout changes positioning only."));
            var helpBar = _helpBar;
            Canvas.ForceUpdateCanvases();
            var previewRect = (RectTransform)_instance.transform.Find("Layout Preview");
            var controlsPane = (RectTransform)_instance.transform.Find("Toolbar Panel");
            var actionPane = controlsPane;
            Assert.That(previewRect, Is.Not.Null);
            Assert.That(controlsPane, Is.Not.Null);
            Assert.That(actionPane, Is.Not.Null);
            // Compare the scene-authored footer and menu in the same normalized viewport.
            // The preview scene itself has no initialized display-sized root canvas in EditMode.
            float footerTop = ViewportBounds(helpBar).yMax;
            var actionBounds = ViewportBounds(actionPane);
            float actionBottom = actionBounds.yMin;
            float actionTop = actionBounds.yMax;
            float previewBottom = ViewportBounds(previewRect).yMin;
            Assert.That(actionBottom, Is.GreaterThan(footerTop),
                "The distinct action band must sit wholly above the real PersistentScene Help Bar.");
            Assert.That(actionBottom, Is.GreaterThan(previewBottom),
                "Management controls now occupy the toolbar above the editing surface.");
            Assert.That(previewBottom, Is.EqualTo(footerTop).Within(0.001f),
                "The preview starts at the real production Help Bar top.");
            float viewportHeight = ((RectTransform)_testCanvasObject.transform).rect.height;
            var editorBounds = ViewportBounds((RectTransform)_instance.transform);
            Assert.That((actionBottom - ViewportBounds(previewRect).yMax) * viewportHeight,
                Is.GreaterThanOrEqualTo(-1f), "The toolbar must not overlap the preview image.");
            Assert.That((actionBottom - editorBounds.yMin) * viewportHeight, Is.GreaterThan(8f),
                "Keep action controls clear of the bottom edge as a visual safe margin.");
            foreach (var actionName in new[] { "_save" })
            {
                var button = (Button)Field(_menu, actionName);
                var buttonRect = (RectTransform)button.transform.parent;
                float buttonBottom = ViewportBounds(buttonRect).yMin;
                Assert.That(buttonBottom, Is.GreaterThan(footerTop),
                    $"{actionName} must clear the real PersistentScene Help Bar.");
                AssertRectContained(buttonRect, (RectTransform)_instance.transform, actionName);
                Assert.That(ViewportBounds(buttonRect).yMax, Is.LessThan(actionBounds.yMin),
                    "Save belongs in the distinct management row below the dropdown toolbar.");
                Assert.That(ViewportBounds(buttonRect).center.y,
                    Is.EqualTo(ViewportBounds((RectTransform)_instance.transform.Find("Management Row")).center.y)
                        .Within(0.0001f));
            }
            foreach (var labelName in new[] { "Preview Color Profile Label" })
            {
                var labelRect = controlsPane.GetComponentsInChildren<RectTransform>(true)
                    .Single(rect => rect.name == labelName);
                Assert.That(labelRect, Is.Not.Null, $"Preview Controls Pane must contain {labelName}.");
                AssertRectContained(labelRect, controlsPane, labelName);
            }
            foreach (var fieldName in new[] { "_previewColor", "_previewOptions" })
            {
                var dropdown = (Component)Field(_menu, fieldName);
                var dropdownType = dropdown.GetType();
                var caption = dropdownType.GetProperty("captionText").GetValue(dropdown);
                var item = dropdownType.GetProperty("itemText").GetValue(dropdown);
                Assert.That(caption, Is.Not.Null, $"{fieldName} needs a caption text component.");
                Assert.That(item, Is.Not.Null, $"{fieldName} needs visible option text.");
                int value = (int)dropdownType.GetProperty("value").GetValue(dropdown);
                bool showsPlaceholder = value == -1;
                var shownText = showsPlaceholder
                    ? dropdownType.GetProperty("placeholder").GetValue(dropdown)
                    : caption;
                Assert.That(shownText, Is.Not.Null, $"{fieldName} needs text for its current selection state.");
                Assert.That(TmpTextType.IsInstanceOfType(shownText), Is.True,
                    $"{fieldName} displayed caption or placeholder must be TMP text.");
                Assert.That(Text(shownText), Is.Not.Empty, $"{fieldName} displayed text must be usable.");
                // The menu root is intentionally inactive in this EditMode fixture; check
                // the dropdown's local display selection rather than activeInHierarchy.
                Assert.That(((Component)shownText).gameObject.activeSelf, Is.True,
                    $"{fieldName} must enable the text it displays.");
                if (fieldName == "_previewOptions")
                {
                    Assert.That(value, Is.EqualTo(-1), "Preview options are actions, not a persistent selection.");
                    Assert.That(Text(shownText), Is.EqualTo("Preview Options"));
                }
                var noWrap = Enum.Parse(shownText.GetType().GetProperty("textWrappingMode").PropertyType, "NoWrap");
                Assert.That(shownText.GetType().GetProperty("textWrappingMode").GetValue(shownText), Is.EqualTo(noWrap));
                Assert.That(item.GetType().GetProperty("textWrappingMode").GetValue(item), Is.EqualTo(noWrap));
                var dropdownRect = dropdown.GetComponent<RectTransform>();
                AssertRectContained(dropdownRect, controlsPane, $"{fieldName} control");
                AssertRectContained(((Component)shownText).GetComponent<RectTransform>(), controlsPane,
                    $"{fieldName} displayed caption or placeholder");
                Assert.That(dropdownRect.anchorMax.x - dropdownRect.anchorMin.x, Is.GreaterThan(0.1f));
            }
            var choiceLabels = (Array)Field(_menu, "_choiceLabels");
            Assert.That(choiceLabels.Length, Is.EqualTo(((Array)Field(_menu, "_choices")).Length));
            Assert.That(choiceLabels.Cast<object>().All(label => label != null), Is.True,
                "Each popup Button needs a TMP label, which is a sibling in the production button prefab.");
            Assert.That(ButtonText((Button)Field(_menu, "_save")), Is.EqualTo("Save"));
            Assert.That(_instance.GetComponentsInChildren<Button>(true).Any(button =>
                button.name == "Cancel" || button.transform.parent.name == "Cancel"), Is.False,
                "The main management row has Save only; cancellation remains available through Back.");
            foreach (var pair in new[]
                     {
                         ("_renameLayout", "Menu.EliteLayout.Rename"),
                         ("_copyLayout", "Menu.EliteLayout.Copy"),
                         ("_importLayout", "Menu.EliteLayout.Import"),
                         ("_exportLayout", "Menu.EliteLayout.Export")
                     })
                Assert.That(ButtonText((Button)Field(_menu, pair.Item1)), Is.EqualTo(LocalizedKey(pair.Item2)));

        }

        [Test]
        public void ManagementActionsFollowPresetRowOrderAndDoNotOverlapAdjacentBands()
        {
            Canvas.ForceUpdateCanvases();
            // MountProductionPrefab clears the menu field before Awake; use its cached authored dropdown.
            Assert.That(_layoutDropdown, Is.Not.Null, "The production prefab must contain its layout dropdown.");
            var layout = ViewportBounds(_layoutDropdown.GetComponent<RectTransform>());
            var controls = ViewportBounds((RectTransform)_instance.transform.Find("Toolbar Panel"));
            Assert.That(controls.height * ((RectTransform)_instance.transform).rect.height,
                Is.EqualTo(97.5f).Within(0.1f), "The toolbar and management bands have equal reference heights.");
            var actionNames = new[] { "_save", "_renameLayout", "_copyLayout", "_deleteLayout", "_importLayout", "_exportLayout" };
            var bounds = actionNames.Select(name => ViewportBounds(
                (RectTransform)((Button)Field(_menu, name)).transform.parent)).ToArray();

            Assert.That(bounds.Select(rect => (rect.xMin + rect.xMax) * 0.5f),
                Is.Ordered.Ascending, "Management actions must follow Save, Rename, Copy, Delete, Import, Export from left to right.");
            float actionTop = bounds.Select(rect => rect.yMax).Min();
            float actionBottom = bounds.Select(rect => rect.yMin).Max();
            Assert.That(actionTop, Is.LessThan(layout.yMin),
                "The action row must be directly below and not overlap the selected-layout dropdown.");
            Assert.That((layout.yMin - actionTop) * 1080f, Is.EqualTo(37.5f).Within(0.1f),
                "The 64-unit dropdown frame and compact management row retain clear padding.");
            var management = ViewportBounds((RectTransform)_instance.transform.Find("Management Row"));
            Assert.That(management.yMax, Is.EqualTo(controls.yMin).Within(0.0001f),
                "The management row starts at the toolbar border without an extra gap.");
            Assert.That(bounds.Select(rect => rect.center.y),
                Is.All.EqualTo(management.center.y).Within(0.0001f),
                "Management actions share the dedicated row below the dropdown toolbar.");
            Assert.That(actionTop, Is.LessThan(controls.yMin),
                "The management row must not overlap the dropdown toolbar.");
            foreach (var fieldName in new[] { "_previewColor", "_previewOptions" })
                Assert.That(actionTop, Is.LessThan(ViewportBounds(
                    ((Component)Field(_menu, fieldName)).GetComponent<RectTransform>()).yMin),
                    "Management actions must sit below both toolbar dropdowns.");
            var cells = ((Array)Field(_menu, "_cells")).Cast<Component>().ToArray();
            float cymbalRowTop = cells.Take(10).Max(cell => ViewportBounds((RectTransform)cell.transform).yMax);
            Assert.That(actionTop, Is.GreaterThan(cymbalRowTop),
                "The action row must remain clear of the top assignment-cell row.");
            Assert.That(bounds.Select(rect => rect.yMax - rect.yMin).Max() -
                bounds.Select(rect => rect.yMax - rect.yMin).Min(), Is.LessThan(0.001f),
                "All management buttons must share a compact horizontal row.");

            for (int i = 1; i < bounds.Length; i++)
            {
                float gap = bounds[i].xMin - bounds[i - 1].xMax;
                Assert.That(gap, Is.GreaterThan(0f), $"{actionNames[i - 1]} and {actionNames[i]} must not overlap.");
                Assert.That(gap * 1920f, Is.EqualTo(i == 4 ? 84f : 10f).Within(1f),
                    "Import/Export are separated from the editing actions by a larger gap.");
            }
        }

        [Test]
        public void HorizontalManagementNavigationSkipsDisabledAndInactiveActions()
        {
            var objects = Enumerable.Range(0, 5).Select(index => new GameObject(
                "Management navigation test " + index, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button)))
                .ToArray();
            try
            {
                var buttons = objects.Select(item => item.GetComponent<Button>()).ToArray();
                buttons[1].interactable = false;
                objects[3].SetActive(false);
                var menuType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                    assembly.GetType("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu")).First(type => type != null);
                var findNext = menuType.GetMethod("FindNextInteractableManagementAction",
                    BindingFlags.Static | BindingFlags.NonPublic);

                Assert.That(findNext.Invoke(null, new object[] { buttons, 0, 1 }), Is.EqualTo(2),
                    "Right navigation must skip a disabled action.");
                Assert.That(findNext.Invoke(null, new object[] { buttons, 4, -1 }), Is.EqualTo(2),
                    "Left navigation must skip an inactive action.");
                buttons[1].interactable = false;
                buttons[2].interactable = false;
                buttons[4].interactable = false;
                Assert.That(findNext.Invoke(null, new object[] { buttons, 0, 1 }), Is.EqualTo(0),
                    "When no other action is available, selection must remain on the current action.");
            }
            finally
            {
                foreach (var item in objects) UnityEngine.Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void BuilderDefinesTightNumberedFretSwatchesWithoutChangingCellSizes()
        {
            var builder = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                assembly.GetType("YARG.Editor.EliteDrumsLayoutMenuPrefabBuilder")).First(type => type != null);
            float upperBottom = (float)builder.GetField("UPPER_ROW_BOTTOM", BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.Public).GetRawConstantValue();
            float upperTop = (float)builder.GetField("UPPER_ROW_TOP", BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.Public).GetRawConstantValue();
            float lowerTop = (float)builder.GetField("LOWER_ROW_TOP", BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.Public).GetRawConstantValue();
            float lowerBottom = (float)builder.GetField("LOWER_ROW_BOTTOM", BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.Public).GetRawConstantValue();
            float swatchTop = (float)builder.GetField("FRET_SWATCH_TOP", BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.Public).GetRawConstantValue();
            float swatchBottom = (float)builder.GetField("FRET_SWATCH_BOTTOM", BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.Public).GetRawConstantValue();

            Assert.That(upperTop - upperBottom, Is.EqualTo(0.09f).Within(0.0001f));
            Assert.That(lowerTop - lowerBottom, Is.EqualTo(0.09f).Within(0.0001f),
                "The revised row spacing must preserve both assignment-cell dimensions.");
            Assert.That((upperBottom - lowerTop) * 1080f, Is.EqualTo(10.8f).Within(0.01f),
                "The assignment rows retain a 10.8-reference-unit gap without overlap.");
            Assert.That(swatchTop - swatchBottom, Is.EqualTo(0.07333333f).Within(0.0001f),
                "The fret pill should be wider than its roughly 78-unit height below the drum row.");
            Assert.That((lowerBottom - swatchTop) * 1080f + 0.6f, Is.EqualTo(10.8f).Within(0.01f),
                "Include the swatch's centered -1.2 height adjustment in its actual top clearance.");
            var numberLabel = builder.GetMethod("FretNumberLabel", BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.Public);
            Assert.That(Enumerable.Range(0, 10).Select(column => (string)numberLabel.Invoke(null, new object[] { column })),
                Is.EqualTo(Enumerable.Range(1, 10).Select(number => number.ToString())),
                "Each displayed saved-slot swatch must be numbered 1 through 10.");
        }

        [Test]
        public void EnglishDictionaryTranslatesEliteDrumFretMetadataFields()
        {
            for (int slot = 1; slot <= 10; slot++)
            {
                Assert.That(LocalizedKey($"Settings.PresetSetting.ColorProfile.EliteDrums.Fret{slot}.Name"),
                    Is.EqualTo($"Fret {slot}"));
                Assert.That(LocalizedKey($"Settings.PresetSetting.ColorProfile.EliteDrums.Fret{slot}Inner.Name"),
                    Is.EqualTo($"Fret {slot} Inner"));
            }
        }

        [Test]
        public void ProductionManagementControlsMatchLayoutSupportAndCompleteness()
        {
            var folder = (Button)Field(_menu, "_openLayoutFolder");
            var rename = (Button)Field(_menu, "_renameLayout");
            var copy = (Button)Field(_menu, "_copyLayout");
            var delete = (Button)Field(_menu, "_deleteLayout");
            var export = (Button)Field(_menu, "_exportLayout");
            _menuType.GetField("_layoutDropdown", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_menu, _layoutDropdown);
            var dropdown = _layoutDropdown;
            Assert.That(folder, Is.Not.Null);
            Assert.That(rename, Is.Not.Null);
            Assert.That(copy, Is.Not.Null);
            Assert.That(delete, Is.Not.Null);
            Assert.That(export, Is.Not.Null);

            var supported = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("supported custom");
            var unsupported = new EliteDrumsLayoutPreset("unsupported complete")
            {
                Merges = new[] { new EliteDrumsLayoutMerge
                {
                    EffectiveId = "tom1+tom2", Sources = new[] { "tom1", "tom2" }
                } }
            };
            unsupported.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", null);
            unsupported.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat);
            unsupported.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1+tom2", "left-crash");
            unsupported.Slots[3] = EliteDrumsLayoutSlot.Ordinary("tom3", "ride");
            unsupported.Slots[4] = EliteDrumsLayoutSlot.Ordinary(null, "right-crash");
            var incomplete = (EliteDrumsLayoutPreset)supported.CopyWithNewName("incomplete");
            incomplete.Slots[4] = new EliteDrumsLayoutSlot();

            var layoutsField = _menuType.GetField("_layouts", BindingFlags.Instance | BindingFlags.NonPublic);
            layoutsField.SetValue(_menu, new[] { supported, unsupported, incomplete });
            dropdown.GetType().GetMethod("ClearOptions", Type.EmptyTypes).Invoke(dropdown, null);
            dropdown.GetType().GetMethod("AddOptions", new[] { typeof(List<string>) })
                .Invoke(dropdown, new object[] { new List<string> { supported.Name, unsupported.Name, incomplete.Name } });
            dropdown.GetType().GetMethod("SetValueWithoutNotify", new[] { typeof(int) })
                .Invoke(dropdown, new object[] { 0 });
            _menuType.GetMethod("UpdateManagementActions", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_menu, null);
            Assert.That(rename.interactable, Is.True);
            Assert.That(copy.interactable, Is.True);
            Assert.That(delete.interactable, Is.True);
            Assert.That(export.interactable, Is.True);

            dropdown.GetType().GetMethod("SetValueWithoutNotify", new[] { typeof(int) })
                .Invoke(dropdown, new object[] { 1 });
            _menuType.GetMethod("UpdateManagementActions", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_menu, null);
            Assert.That(rename.interactable, Is.True);
            Assert.That(copy.interactable, Is.True);
            Assert.That(delete.interactable, Is.True);
            Assert.That(export.interactable, Is.True);
            Assert.That(Text(Field(_menu, "_status")), Is.EqualTo(LocalizedKey("Menu.EliteLayout.Unsupported")));
            _menuType.GetMethod("SelectManagedLayout", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_menu, new object[] { unsupported });
            Assert.That(_menuType.GetProperty("Draft").GetValue(_menu), Is.Null,
                "Layouts with unsupported merges remain management-only.");

            dropdown.GetType().GetMethod("SetValueWithoutNotify", new[] { typeof(int) })
                .Invoke(dropdown, new object[] { 2 });
            _menuType.GetMethod("UpdateManagementActions", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_menu, null);
            Assert.That(rename.interactable, Is.False);
            Assert.That(copy.interactable, Is.False);
            Assert.That(delete.interactable, Is.True);
            Assert.That(export.interactable, Is.False);
            Assert.That(Text(Field(_menu, "_status")), Is.EqualTo(LocalizedKey("Menu.EliteLayout.IncompleteManagement")));

            _menuType.GetMethod("SelectManagedLayout", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_menu, new object[] { incomplete });
            Assert.That(_menuType.GetProperty("Draft").GetValue(_menu), Is.Not.Null,
                "Structurally valid incomplete custom layouts remain editable drafts in management.");
            Assert.That((bool)_menuType.GetProperty("CanSave").GetValue(_menu), Is.False,
                "Selecting an incomplete layout must not relax save validation.");
            Assert.That(rename.interactable, Is.False);
            Assert.That(copy.interactable, Is.False);
            Assert.That(export.interactable, Is.False);
        }

        [Test]
        public void PreviewOptionTogglesAreEditorLocalAndDoNotRequireSettingsSingleton()
        {
            var toggle = _menuType.GetMethod("OnPreviewOptionSelected", BindingFlags.Instance | BindingFlags.NonPublic);
            toggle.Invoke(_menu, new object[] { "Star Power Notes" });
            toggle.Invoke(_menu, new object[] { "Star Power Active" });
            toggle.Invoke(_menu, new object[] { "Groove" });
            toggle.Invoke(_menu, new object[] { "Lefty Flip" });
            Assert.That(Field(_menu, "_forceStarPowerNotes"), Is.EqualTo(true));
            Assert.That(Field(_menu, "_forceStarPower"), Is.EqualTo(true));
            Assert.That(Field(_menu, "_forceGroove"), Is.EqualTo(true));
            Assert.That(Field(_menu, "_leftyFlip"), Is.EqualTo(true));
            toggle.Invoke(_menu, new object[] { "Star Power Notes" });
            Assert.That(Field(_menu, "_forceStarPowerNotes"), Is.EqualTo(false));
        }

        [Test]
        public void SaveDisabledWithUnassignedExplanationUntilComplete()
        {
            var source = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("incomplete draft");
            Initialize(source);
            // Clear right-crash through the production cell popup, leaving an unassigned source.
            Call(_menu, "OpenCell", 7, false);
            var choiceIds = (string[])Field(_menu, "_choiceIds");
            int clearIndex = Array.IndexOf(choiceIds, "clear");
            Assert.That(clearIndex, Is.GreaterThanOrEqualTo(0));
            ((Button[])Field(_menu, "_choices"))[clearIndex].onClick.Invoke();
            var save = (Button)Field(_menu, "_save");
            Assert.That(save.interactable, Is.False);
            string status = Text(Field(_menu, "_status"));
            Assert.That(status, Is.Not.Empty);
            Assert.That(status, Does.Contain("Save disabled — unassigned sources:"));
            Assert.That(status, Does.Contain("Right Crash"));
        }

        [Test]
        public void NavigationCancelRestoresCellWithoutChangingSource()
        {
            var source = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("test draft");
            Initialize(source);
            var navigation = Field(_menu, "_navigation");
            Call(navigation, "SelectAt", (int?)12, Enum.Parse(AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("YARG.Menu.Navigation.SelectionOrigin"))
                .First(type => type != null), "Programmatically"));
            Call(navigation, "ConfirmSelection");
            Assert.That(((GameObject)Field(_menu, "_popup")).activeSelf, Is.True);
            var choiceLabels = (Array)Field(_menu, "_choiceLabels");
            Assert.That(Text(choiceLabels.GetValue(0)), Is.EqualTo("SNARE"));
            Call(_menu, "Back");
            Assert.That(((GameObject)Field(_menu, "_popup")).activeSelf, Is.False);
            Assert.That(navigation.GetType().GetProperty("SelectedIndex").GetValue(navigation), Is.EqualTo(12));
            Assert.That(source.Slots[2].Drum, Is.EqualTo("tom1"));
            Assert.That(_menuType.GetProperty("IsDirty").GetValue(_menu), Is.False);
        }

        [Test]
        public void NavigationEditUsesDetachedDraftAndCancelRequiresConfirmation()
        {
            var source = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("test draft");
            Initialize(source);
            var navigation = Field(_menu, "_navigation");
            // Normal initialization selects Back; step down through the real row map to the
            // assignment grid, then left to its first column, exactly as a controller user would.
            // The EditMode test assembly reaches production menu types via reflection.
            const string CELL_TYPE_NAME = "YARG.Menu.HighwayConfiguration.EliteDrumsLayoutCell";
            object selectedCell = null;
            for (int i = 0; i < 8; i++)
            {
                Call(_menu, "NavigateGrid", 0, 1);
                selectedCell = navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation);
                if (selectedCell != null && selectedCell.GetType().FullName == CELL_TYPE_NAME) break;
            }
            Assert.That(selectedCell, Is.Not.Null);
            Assert.That(selectedCell.GetType().FullName, Is.EqualTo(CELL_TYPE_NAME),
                "Down navigation must reach an assignment cell from Back.");
            for (int i = 0; i < 24; i++) Call(_menu, "NavigateGrid", -1, 0);
            selectedCell = navigation.GetType().GetProperty("SelectedBehaviour").GetValue(navigation);
            Assert.That(selectedCell.GetType().FullName, Is.EqualTo(CELL_TYPE_NAME));
            bool drumRow = (bool)selectedCell.GetType().GetProperty("Drum").GetValue(selectedCell);
            // Initial row cell: confirm opens the real assignment popup.
            Call(navigation, "ConfirmSelection");
            Call(Field(_menu, "_popupNavigation"), "ConfirmSelection");
            var draft = (EliteDrumsLayoutPreset)_menuType.GetProperty("Draft").GetValue(_menu);
            Assert.That(drumRow ? source.Slots[0].Drum : source.Slots[0].Cymbal, Is.Null);
            Assert.That(drumRow ? draft.Slots[0].Drum : draft.Slots[0].Cymbal, Is.Not.Null);
            Assert.That(_menuType.GetProperty("IsDirty").GetValue(_menu), Is.True);
            // The shared dialog is exercised in PlayMode; inspect the close guard here without
            // invoking its uninitialized DialogManager in this EditMode preview scene.
            var close = _menuType.GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic);
            var pending = (Action)(() => close.Invoke(_menu, null));
            _menuType.GetField("_pendingDiscard", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_menu, pending);
            _menuType.GetField("_discardConfirmationShowing", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_menu, true);
            Assert.That(_instance.activeSelf, Is.True);
            Assert.That(Field(_menu, "_pendingDiscard"), Is.SameAs(pending));
            Call(_menu, "Back");
            Assert.That(Field(_menu, "_pendingDiscard"), Is.Null);
            Assert.That(Field(_menu, "_discardConfirmationShowing"), Is.False);
            Assert.That(_instance.activeSelf, Is.True);
            Assert.That(_menuType.GetProperty("IsDirty").GetValue(_menu), Is.True);
            var keptDraft = (EliteDrumsLayoutPreset)_menuType.GetProperty("Draft").GetValue(_menu);
            Assert.That(keptDraft.Slots[0].Cymbal, Is.EqualTo(draft.Slots[0].Cymbal));
        }

        [Test]
        public void DirtyLayoutSwitchCanBeCanceledWithoutLosingDraft()
        {
            var current = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("current");
            var other = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Full.CopyWithNewName("other");
            Initialize(current);
            _menuType.GetField("_layouts", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_menu, new[] { current, other });
            var dropdown = _layoutDropdown;
            _menuType.GetField("_layoutDropdown", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_menu, dropdown);
            dropdown.GetType().GetMethod("ClearOptions", Type.EmptyTypes).Invoke(dropdown, null);
            dropdown.GetType().GetMethod("AddOptions", new[] { typeof(List<string>) })
                .Invoke(dropdown, new object[] { new List<string> { current.Name, other.Name } });

            Call(_menu, "OpenCell", 0, false);
            var choiceIds = (string[])Field(_menu, "_choiceIds");
            ((Button[])Field(_menu, "_choices"))[Array.IndexOf(choiceIds, "left-crash")].onClick.Invoke();
            var edited = (EliteDrumsLayoutPreset)_menuType.GetProperty("Draft").GetValue(_menu);
            var selection = _menuType.GetMethod("SelectManagedLayout", BindingFlags.Instance | BindingFlags.NonPublic);
            // Switching a dirty layout queues selection behind the shared confirmation.
            // The dialog's rendered interaction is covered with initialized services in PlayMode.
            _menuType.GetField("_discardConfirmationShowing", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_menu, true);
            _menuType.GetField("_pendingDiscard", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_menu, (Action)(() => selection.Invoke(_menu, new object[] { other })));
            Assert.That(Field(_menu, "_pendingDiscard"), Is.Not.Null);
            Call(_menu, "Back");
            Assert.That(Field(_menu, "_pendingDiscard"), Is.Null);
            Assert.That(Field(_menu, "_discardConfirmationShowing"), Is.False);
            Assert.That(((EliteDrumsLayoutPreset)_menuType.GetProperty("Draft").GetValue(_menu)).Slots[0].Cymbal,
                Is.EqualTo(edited.Slots[0].Cymbal));
            Assert.That((bool)_menuType.GetProperty("IsDirty").GetValue(_menu), Is.True);
            Assert.That(dropdown.GetType().GetProperty("value").GetValue(dropdown), Is.EqualTo(0));
        }

        [Test]
        public void ToolbarDropdownsSharePresetStyleBand()
        {
            Canvas.ForceUpdateCanvases();
            var toolbar = (RectTransform)_instance.transform.Find("Toolbar Panel");
            Assert.That(toolbar, Is.Not.Null);
            Assert.That(toolbar.GetComponent<Image>().color,
                Is.EqualTo(new Color(3f / 255f, 20f / 255f, 33f / 255f, 1f)));
            var settings = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Menu/Settings/SettingsMenu.prefab");
            var settingsHeader = settings.GetComponentsInChildren<Image>(true)
                .Single(image => image.name == "Header");
            Assert.That(toolbar.GetComponent<Image>().color, Is.EqualTo(settingsHeader.color));
            Assert.That(_instance.GetComponent<Image>().color,
                Is.EqualTo(new Color(5f / 255f, 6f / 255f, 11f / 255f, 1f)),
                "The entire lower panel matches the uniform MenuBackground1 preset artwork.");
            Assert.That(toolbar.rect.height, Is.EqualTo(97.5f).Within(0.1f));
            var dropdowns = new[] { _layoutDropdown, (Component)Field(_menu, "_previewColor"),
                (Component)Field(_menu, "_previewOptions") };
            var reference = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Menu/Settings/Presets/PresetDropdown.prefab");
            var referenceImage = reference.GetComponentsInChildren<Selectable>(true)
                .First(selectable => selectable.GetType().Name.Contains("Dropdown")).targetGraphic as Image;
            Assert.That(referenceImage, Is.Not.Null);
            var referenceDropdown = reference.GetComponentsInChildren<Selectable>(true)
                .First(selectable => selectable.GetType().Name.Contains("Dropdown"));
            var referenceCaption = (Component)referenceDropdown.GetType().GetProperty("captionText").GetValue(referenceDropdown);
            Assert.That(_instance.GetComponent<Image>().color, Is.Not.EqualTo(toolbar.GetComponent<Image>().color),
                "The full-width toolbar must remain a distinct background band.");
            foreach (var dropdown in dropdowns)
            {
                AssertRectContained(dropdown.GetComponent<RectTransform>(), toolbar, dropdown.name);
                var image = dropdown.GetComponent<Selectable>().targetGraphic as Image;
                Assert.That(image, Is.Not.Null);
                Assert.That(image.sprite, Is.EqualTo(referenceImage.sprite),
                    "Dropdowns must retain the shared preset control artwork.");
                Assert.That(image.color, Is.EqualTo(referenceImage.color));
                Assert.That(dropdown.GetComponent<RectTransform>().rect.height,
                    Is.EqualTo(referenceDropdown.GetComponent<RectTransform>().rect.height).Within(0.01f),
                    "Use the authored preset frame height, not the full toolbar row height.");
                var caption = (Component)dropdown.GetType().GetProperty("captionText").GetValue(dropdown);
                Assert.That(caption.GetType().GetProperty("fontSize").GetValue(caption),
                    Is.EqualTo(referenceCaption.GetType().GetProperty("fontSize").GetValue(referenceCaption)));
                Assert.That(caption.GetType().GetProperty("font").GetValue(caption),
                    Is.EqualTo(referenceCaption.GetType().GetProperty("font").GetValue(referenceCaption)));
                for (var ancestor = dropdown.transform.parent; ancestor != toolbar; ancestor = ancestor.parent)
                {
                    Assert.That(ancestor.GetComponentsInChildren<Image>(true)
                        .Where(decoration => !decoration.transform.IsChildOf(dropdown.transform)), Is.Empty,
                        "Copied settings rows must not retain divider or background graphics.");
                }
            }
            var bounds = dropdowns.Select(dropdown => ViewportBounds(dropdown.GetComponent<RectTransform>())).ToArray();
            Assert.That(bounds.Max(rect => rect.yMin) - bounds.Min(rect => rect.yMin), Is.LessThan(0.002f));
            Assert.That(bounds.Max(rect => rect.yMax) - bounds.Min(rect => rect.yMax), Is.LessThan(0.002f));
            Assert.That(bounds.Select(rect => rect.center.y),
                Is.All.EqualTo(ViewportBounds(toolbar).center.y).Within(0.0001f),
                "Fixed-size dropdowns stay vertically centered when the toolbar height changes.");
            var folder = (Button)Field(_menu, "_openLayoutFolder");
            var folderRect = (RectTransform)folder.transform.parent;
            Assert.That(folderRect.rect.size, Is.EqualTo(new Vector2(76f, 76f)));
            Assert.That(ViewportBounds(folderRect).center.y,
                Is.EqualTo(ViewportBounds(toolbar).center.y).Within(0.0001f));
        }

        [Test]
        public void AssignmentAndManagementGroupHasEqualPanelMarginsWithoutChangingGeometry()
        {
            Canvas.ForceUpdateCanvases();
            for (int column = 0; column < 10; column++)
            {
                float left = 0.0225f + column * 0.061f;
                foreach (var row in new[] { "Cymbal", "Drum" })
                {
                    var cell = ViewportBounds((RectTransform)_instance.transform.Find($"Column {column + 1} {row}"));
                    Assert.That(cell.xMin, Is.EqualTo(left).Within(0.00001f));
                    Assert.That(cell.width, Is.EqualTo(0.056f).Within(0.00001f));
                    Assert.That(cell.yMin, Is.EqualTo((row == "Cymbal" ? 0.50f : 0.40f) + 72f / 1080f).Within(0.00001f));
                    Assert.That(cell.height, Is.EqualTo(0.09f).Within(0.00001f));
                }
                var fret = (RectTransform)_instance.transform.Find($"Saved Slot Fret {column + 1}");
                Assert.That(fret.anchorMin.x, Is.EqualTo(left + 0.003f).Within(0.00001f));
                Assert.That(fret.anchorMax.x, Is.EqualTo(left + 0.053f).Within(0.00001f));
                Assert.That(fret.anchorMin.y, Is.EqualTo(0.31722223f + 72f / 1080f).Within(0.00001f));
                Assert.That(fret.anchorMax.y, Is.EqualTo(0.39055556f + 72f / 1080f).Within(0.00001f));
                Assert.That(fret.sizeDelta, Is.EqualTo(new Vector2(0f, -1.2f)));
            }
            var names = new[] { "_save", "_renameLayout", "_copyLayout", "_deleteLayout", "_importLayout", "_exportLayout" };
            var actions = names.Select(name => ViewportBounds((RectTransform)((Button)Field(_menu, name)).transform.parent)).ToArray();
            const float WIDTH = (0.605f * 1920f - 6f * 10f - 64f) / 6f;
            Assert.That(actions[0].xMin, Is.EqualTo(0.0225f).Within(0.00001f));
            Assert.That(0.65f - actions[5].xMax, Is.EqualTo(actions[0].xMin).Within(0.00001f));
            for (int i = 0; i < actions.Length; i++)
            {
                Assert.That(actions[i].width * 1920f, Is.EqualTo(WIDTH).Within(0.01f));
                Assert.That(actions[i].height * 1080f, Is.EqualTo(56f).Within(0.01f));
                if (i > 0)
                    Assert.That((actions[i].xMin - actions[i - 1].xMax) * 1920f,
                        Is.EqualTo(i == 4 ? 84f : 10f).Within(0.01f));
            }
        }

        [Test]
        public void ToolbarAndAssignmentBoundariesMeetAtActualPanelEdges()
        {
            Canvas.ForceUpdateCanvases();
            var toolbar = ViewportBounds((RectTransform)_instance.transform.Find("Toolbar Panel"));
            var management = ViewportBounds((RectTransform)_instance.transform.Find("Management Row"));
            var dividerRect = (RectTransform)_instance.transform.Find("Assignment Preview Divider");
            var divider = ViewportBounds(dividerRect);
            Assert.That(toolbar.xMin, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(toolbar.xMax, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(management.xMin, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(management.xMax, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(management.height * 1080f, Is.EqualTo(97.5f).Within(0.1f));
            Assert.That(toolbar.height, Is.EqualTo(management.height).Within(0.00001f));
            Assert.That((toolbar.yMax - management.yMin) * 1080f, Is.EqualTo(195f).Within(0.1f));
            var header = ViewportBounds((RectTransform)_instance.transform.Find("Header"));
            Assert.That(toolbar.yMax, Is.EqualTo(header.yMin).Within(0.0001f));
            var cymbal = ViewportBounds((RectTransform)_instance.transform.Find("Column 1 Cymbal"));
            Assert.That(management.yMin, Is.GreaterThan(cymbal.yMax),
                "The management bottom border must clear the relocated assignment row.");
            Assert.That(management.yMax, Is.EqualTo(toolbar.yMin).Within(0.0001f));
            Assert.That(divider.center.x, Is.EqualTo(management.xMax).Within(0.0001f));
            Assert.That(divider.width * 1920f, Is.EqualTo(1.5f).Within(0.01f));
            Assert.That(divider.yMax, Is.EqualTo(toolbar.yMin).Within(0.0001f));
            Assert.That(divider.yMin, Is.EqualTo(ViewportBounds(_helpBar).yMax).Within(0.0001f),
                "The static divider ends at the actual production Help Bar, not an estimated footer height.");
            foreach (var pair in new[]
                     {
                         ("Toolbar Panel/Toolbar Bottom Border", toolbar),
                         ("Management Row/Management Bottom Border", management)
                     })
            {
                var rect = (RectTransform)_instance.transform.Find(pair.Item1);
                var bounds = ViewportBounds(rect);
                Assert.That(bounds.xMin, Is.EqualTo(pair.Item2.xMin).Within(0.0001f));
                Assert.That(bounds.xMax, Is.EqualTo(pair.Item2.xMax).Within(0.0001f));
                Assert.That(bounds.yMin, Is.EqualTo(pair.Item2.yMin).Within(0.0001f));
                Assert.That(bounds.height * 1080f, Is.EqualTo(1.5f).Within(0.01f));
            }
            var separatorRect = (RectTransform)_instance.transform.Find("Toolbar Panel/Layout Colors Separator");
            var separator = ViewportBounds(separatorRect);
            Assert.That(separator.height * 1080f, Is.EqualTo(80f).Within(0.1f));
            Assert.That(separator.width * 1920f, Is.EqualTo(1.5f).Within(0.01f));
            Assert.That(separator.center.y, Is.EqualTo(toolbar.center.y).Within(0.0001f));
            var folder = (Button)Field(_menu, "_openLayoutFolder");
            var colorLabel = (Component)Field(_menu, "_previewColorLabel");
            Assert.That(separator.xMin, Is.GreaterThan(ViewportBounds((RectTransform)folder.transform.parent).xMax));
            Assert.That(separator.xMax, Is.LessThan(ViewportBounds(colorLabel.GetComponent<RectTransform>()).xMin));
            foreach (var path in new[] { "Toolbar Panel/Toolbar Bottom Border", "Management Row/Management Bottom Border",
                         "Assignment Preview Divider", "Toolbar Panel/Layout Colors Separator" })
            {
                var decoration = _instance.transform.Find(path);
                var image = decoration.GetComponent<Image>();
                var presetDivider = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Menu/Settings/Visuals/BaseSetting.prefab").transform.Find("Divider");
                Assert.That(image.color, Is.EqualTo(presetDivider.GetComponent<Image>().color), path);
                Assert.That(image.color, Is.EqualTo(new Color(16f / 255f, 28f / 255f, 43f / 255f, 1f)), path);
                Assert.That(1.5f, Is.LessThan(presetDivider.GetComponent<RectTransform>().rect.height),
                    "The finishing dividers are lighter-weight than the authored preset row divider.");
                Assert.That(image.sprite, Is.Null, path + " must be a sharp solid line");
                Assert.That(image.raycastTarget, Is.False, path);
                Assert.That(decoration.GetComponent<Selectable>(), Is.Null, path + " is not a scrollbar or selectable");
            }
            var layoutLabel = _instance.transform.Find("Toolbar Panel/Layout Selection").GetComponentsInChildren(TmpTextType, true)
                .Cast<Component>().Single(text => Text(text) == "LAYOUT");
            Assert.That(Text(colorLabel), Is.EqualTo(LocalizedKey("Menu.EliteLayout.Colors").ToUpperInvariant()));
            Assert.That(Text(colorLabel), Is.EqualTo("COLORS"), "Runtime localization must preserve the uppercase caption.");
            foreach (var property in new[] { "font", "fontSize", "fontStyle", "fontWeight", "enableAutoSizing" })
                Assert.That(colorLabel.GetType().GetProperty(property).GetValue(colorLabel),
                    Is.EqualTo(layoutLabel.GetType().GetProperty(property).GetValue(layoutLabel)), property);
        }

        [Test]
        public void LayoutFolderButtonMatchesPresetReference()
        {
            var folder = (Button)Field(_menu, "_openLayoutFolder");
            var icon = folder.transform.parent.Find("Icon").GetComponent<Image>();
            var expectedSprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Menu/Common/Icons/AssortedIcons.png")
                .OfType<Sprite>().Single(sprite => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    sprite, out string _, out long localId) && localId == -1486389601L);
            Assert.That(icon.sprite, Is.EqualTo(expectedSprite),
                "Use the preset folder sprite's asset identity; its imported name is UIIcons_4.");
            Assert.That(icon.color, Is.EqualTo(Color.black));
            Assert.That(icon.rectTransform.sizeDelta, Is.EqualTo(new Vector2(24f, 24f)));
            Assert.That(folder.GetComponent<Image>().color,
                Is.EqualTo(new Color(0.18039216f, 0.8509804f, 1f)));
            Assert.That(folder.onClick.GetPersistentEventCount(), Is.Zero,
                "The copied control must not retain the preset page callback.");
        }

        [Test]
        public void LayoutFolderButtonOpensLayoutDirectoryOnce()
        {
            Initialize(EliteDrumsLayoutPreset.Compact);
            var paths = new List<string>();
            Call(_menu, "SetFolderOpener", (Action<string>)paths.Add);
            ((Button)Field(_menu, "_openLayoutFolder")).onClick.Invoke();
            Assert.That(paths.Count, Is.EqualTo(1), "One button activation dispatches exactly once.");
            Assert.That(paths[0], Does.EndWith("eliteDrumsLayoutPresets"));
        }

        [Test]
        public void ManagementRowContainsSaveWithoutCancel()
        {
            var names = new[] { "_save", "_renameLayout", "_copyLayout", "_deleteLayout", "_importLayout", "_exportLayout" };
            var buttons = names.Select(name => (Button)Field(_menu, name)).ToArray();
            Assert.That(buttons.Select(button => ButtonText(button)),
                Is.EqualTo(new[] { "Save", "Rename", "Copy", "Delete", "Import", "Export" }));
            Assert.That(_menuType.GetField("_cancel", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
            var bounds = buttons.Select(button => ViewportBounds((RectTransform)button.transform.parent)).ToArray();
            Assert.That(bounds.Select(rect => rect.center.x), Is.Ordered.Ascending);
            Assert.That(bounds.Max(rect => rect.center.y) - bounds.Min(rect => rect.center.y), Is.LessThan(0.001f));
        }

        [Test]
        public void GridAndPreviewUseReclaimedVerticalSpace()
        {
            Canvas.ForceUpdateCanvases();
            var cells = ((Array)Field(_menu, "_cells")).Cast<Component>().ToArray();
            var preview = ViewportBounds((RectTransform)_instance.transform.Find("Layout Preview"));
            var upper = cells.Take(10).Select(cell => ViewportBounds((RectTransform)cell.transform)).ToArray();
            var lower = cells.Skip(10).Select(cell => ViewportBounds((RectTransform)cell.transform)).ToArray();
            Assert.That(preview.yMin, Is.EqualTo(ViewportBounds(_helpBar).yMax).Within(0.001f));
            Assert.That(preview.height * 1080f, Is.EqualTo(711f).Within(1f));
            Assert.That(preview.yMax, Is.GreaterThan(upper[0].yMax),
                "The tall production preview extends above the assignment rows.");
            Assert.That(preview.xMin, Is.EqualTo(0.66f).Within(0.0001f));
            Assert.That(preview.xMax, Is.EqualTo(0.99f).Within(0.0001f));
            var builtIn = ViewportBounds((RectTransform)_instance.transform.Find("Built In Notice"));
            Assert.That(builtIn.yMin * 1080f, Is.EqualTo(333.6f).Within(0.1f));
            Assert.That(builtIn.yMax * 1080f, Is.EqualTo(409.2f).Within(0.1f));
            Assert.That(builtIn.xMin, Is.EqualTo(0.04f).Within(0.0001f));
            Assert.That(builtIn.xMax, Is.EqualTo(0.63f).Within(0.0001f));
            var status = ViewportBounds((RectTransform)_instance.transform.Find("Status"));
            var colorsNotice = ViewportBounds((RectTransform)_instance.transform.Find("Colors Notice"));
            Assert.That(status.yMin * 1080f, Is.EqualTo(86.4f).Within(0.1f));
            Assert.That(status.yMax * 1080f, Is.EqualTo(151.2f).Within(0.1f));
            Assert.That(colorsNotice.yMin * 1080f, Is.EqualTo(16.2f).Within(0.1f));
            Assert.That(colorsNotice.yMax * 1080f, Is.EqualTo(75.6f).Within(0.1f));
            for (int i = 0; i < 10; i++)
            {
                Assert.That(upper[i].yMin * 1080f, Is.EqualTo(612f).Within(0.1f));
                Assert.That(upper[i].yMax * 1080f, Is.EqualTo(709.2f).Within(0.1f));
                Assert.That(lower[i].yMin * 1080f, Is.EqualTo(504f).Within(0.1f));
                Assert.That(lower[i].yMax * 1080f, Is.EqualTo(601.2f).Within(0.1f));
                var fret = ViewportBounds((RectTransform)_instance.transform.Find($"Saved Slot Fret {i + 1}"));
                Assert.That(fret.yMin * 1080f, Is.EqualTo(415.2f).Within(0.1f));
                Assert.That(fret.yMax * 1080f, Is.EqualTo(493.2f).Within(0.1f));
                Assert.That((lower[i].yMin - fret.yMax) * 1080f, Is.EqualTo(10.8f).Within(0.1f));
                Assert.That(fret.xMin, Is.EqualTo(0.0255f + i * 0.061f).Within(0.0001f));
                Assert.That(fret.width * 1920f, Is.EqualTo(96f).Within(0.1f));
                Assert.That(upper[i].xMin, Is.EqualTo(0.0225f + i * 0.061f).Within(0.0001f));
                Assert.That(upper[i].width * 1920f, Is.EqualTo(107.52f).Within(0.1f));
                Assert.That(upper[i].xMin, Is.EqualTo(lower[i].xMin).Within(0.0001f));
                Assert.That(upper[i].width, Is.EqualTo(lower[i].width).Within(0.0001f));
                Assert.That((upper[i].yMin - lower[i].yMax) * 1080f, Is.EqualTo(10.8f).Within(0.1f));
                Assert.That(lower[i].xMax, Is.LessThan(preview.xMin));
            }
        }

        [Test]
        public void AllKicksUsesCellOnlyLineBreak()
        {
            var cell = ((Array)Field(_menu, "_cells")).Cast<Component>().Last();
            Call(cell, "Render", new EliteDrumsLayoutSlot { Kind = EliteDrumsLayoutSlotKind.Kick },
                new ColorProfile("Cell label test"), false, true);
            Assert.That(Text(Field(cell, "_label")), Is.EqualTo("All\nKicks"));
            Assert.That(LocalizedKey("Menu.EliteLayout.Item.kick"), Is.EqualTo("All Kicks"),
                "Only the cell caption may introduce a line break.");
        }

        [Test]
        public void CellFillTracksRuntimeValidityAndPalette()
        {
            var cell = ((Array)Field(_menu, "_cells")).Cast<Component>().Last();
            var profile = new ColorProfile("Validity sentinel");
            profile.EliteDrums.SnareNote = System.Drawing.Color.Red;
            var slot = new EliteDrumsLayoutSlot { Drum = "snare" };
            var dark = new Color(0.055f, 0.07f, 0.09f, 1f);
            var fill = (Image)Field(cell, "_fill");
            Call(cell, "Render", slot, profile, false, true);
            Assert.That(fill.color, Is.EqualTo(Color.Lerp(dark, Color.red, 0.25f)));
            Call(cell, "Render", slot, profile, true, false);
            Assert.That(fill.color, Is.EqualTo(dark), "Unsupported layouts must remain neutral management-only surfaces.");
            Call(cell, "Render", slot, profile, true, true);
            Assert.That(fill.color, Is.EqualTo(Color.Lerp(dark, Color.red, 0.25f)),
                "Built-in read-only cells retain validity color after leaving management-only mode.");
            Assert.That(cell.GetComponent<Button>().interactable, Is.False);
            profile.EliteDrums.SnareNote = System.Drawing.Color.Blue;
            Call(cell, "Render", slot, profile, false, true);
            Assert.That(fill.color, Is.EqualTo(Color.Lerp(dark, Color.blue, 0.25f)));
            Call(cell, "Render", new EliteDrumsLayoutSlot(), profile, false, true);
            Assert.That(fill.color, Is.EqualTo(dark), "Empty slots must not retain a previously assigned tint.");
        }

        [Test]
        public void CellSelectionAndOutlineRenderWithRoundedUnclippedEdges()
        {
            foreach (var cell in ((Array)Field(_menu, "_cells")).Cast<Component>())
            {
                var rootImage = cell.GetComponent<Image>();
                var fill = (Image)Field(cell, "_fill");
                var focus = ((GameObject)Field(cell, "_focusVisual")).GetComponent<Image>();
                var label = (Component)Field(cell, "_label");
                Assert.That(cell.GetComponent<Button>().transition, Is.EqualTo(Selectable.Transition.None));
                Assert.That(Field(cell, "_outline"), Is.SameAs(cell.GetComponent<Outline>()));
                Assert.That(fill.transform, Is.Not.SameAs(cell.transform));
                Assert.That(fill.sprite, Is.SameAs(rootImage.sprite));
                Assert.That(focus.sprite, Is.SameAs(rootImage.sprite));
                Assert.That(fill.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(focus.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(fill.rectTransform.offsetMin, Is.EqualTo(new Vector2(3f, 3f)));
                Assert.That(fill.rectTransform.offsetMax, Is.EqualTo(new Vector2(-3f, -3f)));
                Assert.That(focus.transform.GetSiblingIndex(), Is.LessThan(label.transform.GetSiblingIndex()));
                Assert.That(fill.raycastTarget || focus.raycastTarget, Is.False);
                Assert.That(fill.color.a, Is.EqualTo(1f), "Validity tint must be composited to an opaque fill.");
                Assert.That(cell.GetComponentsInParent<Mask>(true), Is.Empty,
                    "A mask must not clip the source-colored outer outline.");
            }
        }

        [Test]
        public void BuiltInCellsAreInspectionOnlyAndFolderDispatchIsInjectable()
        {
            Initialize(EliteDrumsLayoutPreset.Compact);
            var cells = ((Array)Field(_menu, "_cells")).Cast<Component>().ToArray();
            Assert.That(cells.Length, Is.EqualTo(20));
            Assert.That(cells.All(cell => !cell.GetComponent<Button>().interactable), Is.True);
            Assert.That(((GameObject)Field(_menu, "_builtInNotice")).activeSelf, Is.True);
            var popup = (GameObject)Field(_menu, "_popup");
            Call(_menu, "OpenCell", 0, false);
            Assert.That(popup.activeSelf, Is.False, "Inspecting a built-in must not open the assignment picker or copy dialog.");

            string openedPath = null;
            var opener = _menuType.GetMethod("SetFolderOpener");
            opener.Invoke(_menu, new object[] { (Action<string>)(path => openedPath = path) });
            Call(_menu, "OpenLayoutFolder");
            Assert.That(openedPath, Is.Not.Null.And.Not.Empty);
            Assert.That(openedPath, Does.Contain("eliteDrumsLayoutPresets"));
        }
    }
}
