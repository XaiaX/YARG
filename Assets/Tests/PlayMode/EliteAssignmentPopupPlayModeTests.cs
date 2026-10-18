using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class EliteAssignmentPopupPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        private EliteConfigurationPointerPlayModeTests _bootstrap;
        private GameObject _canvas;
        private GameObject _services;
        private GameObject _root;
        private Component _menu;
        private Component _navigator;
        private EventSystem _events;
        private EventSystem _previousEvents;
        private bool _previousEventsEnabled;
        private IList _groups;
        private object[] _previousGroups;
        private int _writes;
        private bool _profileCurrent;
        private Camera _captureCamera;
        private RenderTexture _captureTarget;
        private static string Hierarchy(Component value) => string.Join("/",
            value.GetComponentsInParent<Transform>(true).Reverse().Select(parent => parent.name));

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static object Field(object owner, string name) => owner.GetType().GetField(name, INSTANCE).GetValue(owner);
        private static object Property(object owner, string name) => owner.GetType().GetProperty(name, INSTANCE).GetValue(owner);
        private static object Call(object owner, string name, params object[] args) =>
            owner.GetType().GetMethod(name, INSTANCE).Invoke(owner, args);
        private static object Singleton(string name) => Production(name).GetProperty("Instance", STATIC).GetValue(null);
        private GameObject Popup => (GameObject)Field(_menu, "_popup");
        private object Grid => Field(_menu, "_navigation");
        private object PopupGroup => Field(_menu, "_popupNavigation");
        private EliteDrumsLayoutPreset Draft => (EliteDrumsLayoutPreset)Property(_menu, "Draft");
        private object CurrentGroup => Production("YARG.Menu.Navigation.NavigationGroup")
            .GetProperty("CurrentNavigationGroup", STATIC).GetValue(null);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            // Never replace live menu services or send test input through a user's scheme stack.
            foreach (var type in new[] { "YARG.Menu.Navigation.Navigator", "YARG.Menu.Persistent.HelpBar",
                         "YARG.Menu.Persistent.DialogManager" })
                if (Singleton(type) != null) Assert.Ignore("Requires an isolated test scene without live " + type);
            _writes = 0;
            _profileCurrent = true;
            _bootstrap = new EliteConfigurationPointerPlayModeTests();
            yield return _bootstrap.SetUpRealTheme();
            _groups = (IList)Production("YARG.Menu.Navigation.NavigationGroup").GetField("_navGroupsStack", STATIC).GetValue(null);
            _previousGroups = _groups.Cast<object>().ToArray();
            _groups.Clear();
            _previousEvents = EventSystem.current;
            if (_previousEvents != null)
            {
                _previousEventsEnabled = _previousEvents.enabled;
                _previousEvents.enabled = false;
            }
            _canvas = new GameObject("Assignment popup test Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var events = new GameObject("Assignment popup test EventSystem", typeof(EventSystem));
            events.transform.SetParent(_canvas.transform, false);
            _events = events.GetComponent<EventSystem>();
            EventSystem.current = _events;
            MountServices();
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null);
            _root = UnityEngine.Object.Instantiate(prefab, _canvas.transform, false);
            _menu = _root.GetComponent(Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu"));
            Initialize(false);
            yield return null;
            Canvas.ForceUpdateCanvases();
#else
            Assert.Ignore("Requires production menu assets in the Unity Editor.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private void MountServices()
        {
            _services = new GameObject("Assignment popup isolated services");
            _services.SetActive(false);
            var help = _services.AddComponent(Production("YARG.Menu.Persistent.HelpBar"));
            var buttons = new GameObject("Help buttons").transform;
            buttons.SetParent(_services.transform, false);
            help.GetType().GetField("_buttonContainer", INSTANCE).SetValue(help, buttons);
            help.GetType().GetField("_buttonPrefab", INSTANCE).SetValue(help,
                UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Menu/Persistent/HelpBarButton.prefab"));
            var musicObject = new GameObject("Inactive music player");
            musicObject.transform.SetParent(_services.transform, false);
            musicObject.SetActive(false);
            var music = musicObject.AddComponent(Production("YARG.Menu.Persistent.MusicPlayer"));
            help.GetType().GetProperty("MusicPlayer").GetSetMethod(true).Invoke(help, new object[] { music });
            var dialogs = _services.AddComponent(Production("YARG.Menu.Persistent.DialogManager"));
            var dialogContainer = new GameObject("Dialog container").transform;
            dialogContainer.SetParent(_services.transform, false);
            dialogs.GetType().GetField("_dialogContainer", INSTANCE).SetValue(dialogs, dialogContainer);
            _navigator = _services.AddComponent(Production("YARG.Menu.Navigation.Navigator"));
            _services.SetActive(true);
        }
#endif

        private void Initialize(bool lefty)
        {
            // A valid, editable copy; all callbacks are in memory and no profile singleton is touched.
            var preset = (EliteDrumsLayoutPreset)EliteDrumsLayoutPreset.Compact.CopyWithNewName("Assignment popup test");
            Call(_menu, "InitializeForProfile", preset, ColorProfile.Default, lefty,
                (Action<EliteDrumsLayoutPreset>)(_ => _writes++), (Action)(() => { }),
                (Func<bool>)(() => _profileCurrent),
                (Func<EliteDrumsLayoutPreset, bool>)(_ => { _writes++; return true; }),
                (Func<ColorProfile, bool>)(_ => { _writes++; return true; }),
                (Func<bool, bool>)(_ => { _writes++; return true; }));
        }

        private Component Cell(int column, bool drum) => _root
            .GetComponentsInChildren(Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutCell"), true)
            .Cast<Component>().Single(cell => (int)Property(cell, "Column") == column && (bool)Property(cell, "Drum") == drum);
        private Button CellButton(Component cell) => (Button)Field(cell, "_button");
        private Button Choice(string id)
        {
            int index = Array.IndexOf((string[])Field(_menu, "_choiceIds"), id);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Expected production choice " + id);
            return ((Button[])Field(_menu, "_choices"))[index];
        }
        private static string Placement(EliteDrumsLayoutPreset preset) => string.Join("|", preset.Slots
            .Select(slot => $"{slot.Kind}:{slot.Drum}:{slot.Cymbal}"));
        private static void Select(Component target, string origin = "Programmatically") => Call(target, "SetSelected", true,
            Enum.Parse(Production("YARG.Menu.Navigation.SelectionOrigin"), origin));
        private void Dispatch(string action)
        {
            var context = Activator.CreateInstance(Production("YARG.Menu.Navigation.NavigationContext"),
                Enum.Parse(Production("YARG.Core.Input.MenuAction"), action), null, false);
            // Dispatch through Navigator's real scheme stack, not the menu's private action callbacks.
            Call(_navigator, "InvokeNavigationEvent", context);
        }
        private Vector2 Center(Component component)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)component.transform;
            var canvas = component.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        }
        private GameObject Raycast(Vector2 position)
        {
            var hits = new List<RaycastResult>();
            _events.RaycastAll(new PointerEventData(_events) { position = position }, hits);
            Assert.That(hits, Is.Not.Empty, "Production GraphicRaycaster must hit the requested position.");
            return hits[0].gameObject;
        }
        private void Click(Component owner)
        {
            var position = Center(owner);
            var target = Raycast(position);
            Assert.That(target == owner.gameObject || target.transform.IsChildOf(owner.transform), Is.True,
                $"Top EventSystem hit must belong to {Hierarchy(owner)}; actual={Hierarchy(target.transform)} " +
                $"point={position} ownerRect={((RectTransform)owner.transform).rect} " +
                $"ownerScale={owner.transform.lossyScale} handler={ExecuteEvents.GetEventHandler<IPointerClickHandler>(target)}");
            PointerClick(target, position);
        }
        private void PointerClick(GameObject target, Vector2 position)
        {
            var pointer = new PointerEventData(_events) { position = position, button = PointerEventData.InputButton.Left };
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler);
        }
        private IEnumerator ClickChoice(string id)
        {
            yield return null; // Let newly activated production Graphics receive canvas depth and clipping state.
            Assert.That(Popup.activeInHierarchy, Is.True, "Choice click requires an open production popup.");
            var choice = Choice(id);
            Select(choice.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable")));
            yield return null; // ScrollRect applies its bounds and mask after selection reveals a lower row.
            Assert.That(Popup.activeInHierarchy, Is.True, "Selecting a row must not dismiss the popup.");
            var group = Popup.GetComponent<CanvasGroup>();
            Assert.That(group.blocksRaycasts, Is.True);
            Assert.That(group.ignoreParentGroups, Is.True);
            Canvas.ForceUpdateCanvases();
            var backdrop = Popup.GetComponent<Image>();
            var target = choice.targetGraphic;
            Debug.Log($"[Assignment popup pointer] id={id} active={Popup.activeInHierarchy} " +
                $"backdropEnabled={backdrop.isActiveAndEnabled} backdropRaycast={backdrop.raycastTarget} " +
                $"backdropCull={backdrop.canvasRenderer.cull} targetCull={target.canvasRenderer.cull} " +
                $"targetRaycast={target.raycastTarget} targetDepth={target.depth} " +
                $"popupAlpha={group.alpha} blocks={group.blocksRaycasts} point={Center(choice)}");
            Click(choice);
        }
        private void AssertOrigin(Component cell)
        {
            Assert.That(Popup.activeSelf, Is.False);
            Assert.That(CurrentGroup, Is.SameAs(Grid));
            Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(cell), "Restore the exact saved row and column.");
            Assert.That(_root.GetComponent<CanvasGroup>().interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator AssignmentPopupModalInputIsolation()
        {
            foreach (bool lefty in new[] { false, true })
            {
                Initialize(lefty);
                yield return null;
                var origin = Cell(0, true);
                Click(CellButton(origin));
                Assert.That(Popup.activeSelf, Is.True);
                Assert.That(_root.GetComponent<CanvasGroup>().interactable, Is.False);
                Assert.That(Popup.GetComponent<CanvasGroup>().ignoreParentGroups, Is.True);
                string before = Placement(Draft);
                object selected = Property(Grid, "SelectedBehaviour");
                var background = Cell(1, false);
                var gate = (Func<bool>)Property(background, "InputAllowed");
                Assert.That(gate, Is.Not.Null);
                Assert.That(gate(), Is.False);
                Select(background, "Mouse");
                Call(background, "OnPointerMove", new PointerEventData(_events));
                Call(background, "Confirm");
                var reverse = (Button)Field(_menu, "_reverseLayout");
                var runtime = reverse.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
                Select(runtime, "Navigation");
                Call(runtime, "Confirm");
                Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(selected));
                Assert.That(Placement(Draft), Is.EqualTo(before));
                Assert.That((int)Field(_menu, "_column"), Is.Zero);
                Dispatch("Down");
                Assert.That(CurrentGroup, Is.SameAs(PopupGroup));
                Dispatch("Red");
                AssertOrigin(origin);
                Assert.That(_writes, Is.Zero);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AssignmentPopupDismissRestoresOrigin()
        {
            foreach (bool lefty in new[] { false, true })
            foreach (bool drum in new[] { false, true })
            {
                Initialize(lefty);
                yield return null;
                var origin = Cell(0, drum);
                Select(origin);
                Dispatch("Green");
                Assert.That(Popup.activeSelf, Is.True);
                Dispatch("Red");
                AssertOrigin(origin);
                Dispatch("Green");
                Assert.That(Popup.activeSelf, Is.False, "The dismissal frame cannot reopen the popup.");
                yield return null;
                Dispatch("Green");
                Assert.That(Popup.activeSelf, Is.True);
                yield return ClickChoice("cancel");
                AssertOrigin(origin);
                yield return null;
                Click(CellButton(origin));
                yield return null; // Register the newly active popup graphics before the next pointer input.
                var backdrop = (Button)Field(_menu, "_popupBackdropButton");
                // Use an actual uncovered backdrop corner, not the popup card's center.
                var rect = (RectTransform)backdrop.transform;
                var point = RectTransformUtility.WorldToScreenPoint(null,
                    rect.TransformPoint(rect.rect.min + Vector2.one * 4f));
                var hit = Raycast(point);
                Assert.That(hit == backdrop.gameObject || hit.transform.IsChildOf(backdrop.transform), Is.True);
                PointerClick(hit, point);
                AssertOrigin(origin);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AssignmentPopupLifecycleCleanup()
        {
            int schemes = (int)Property(Field(_navigator, "_schemeStack"), "Count");
            Click(CellButton(Cell(0, true)));
            _root.SetActive(false);
            Assert.That(Popup.activeSelf, Is.False);
            Assert.That(_groups.Contains(PopupGroup), Is.False);
            Assert.That(_groups.Contains(Grid), Is.False);
            Assert.That((int)Property(PopupGroup, "Count"), Is.Zero);
            Assert.That(_root.GetComponent<CanvasGroup>().interactable, Is.True);
            Assert.That((int)Property(Field(_navigator, "_schemeStack"), "Count"), Is.EqualTo(schemes - 1));
            yield return null;
            _root.SetActive(true);
            yield return null;
            Click(CellButton(Cell(0, true)));
            var popupGroup = PopupGroup;
            UnityEngine.Object.Destroy(_root);
            yield return null;
            Assert.That(_groups.Contains(popupGroup), Is.False);
            Assert.That((int)Property(Field(_navigator, "_schemeStack"), "Count"), Is.EqualTo(schemes - 1));
            Assert.That(_writes, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AssignmentPopupTopmostRaycast()
        {
            foreach (bool lefty in new[] { false, true })
            {
                Initialize(lefty);
                yield return null;
                var cell = Cell(0, true);
                Click(CellButton(cell));
                yield return null;
                Assert.That(Popup.transform.GetSiblingIndex(), Is.EqualTo(Popup.transform.parent.childCount - 1));
                var choice = Choice("snare");
                Assert.That(choice.targetGraphic.canvasRenderer.cullTransparentMesh, Is.False,
                    "A zero-alpha popup hit graphic must not be culled: GraphicRaycaster skips culled graphics.");
                Select(choice.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable")));
                var hit = Raycast(Center(choice));
                Assert.That(hit.transform.IsChildOf(choice.transform) || hit == choice.gameObject, Is.True);
                var covered = Raycast(Center(CellButton(cell)));
                Assert.That(covered.transform.IsChildOf(Popup.transform) || covered == Popup, Is.True,
                    "The modal backdrop, never the underlying cell, owns pointer hits outside the card.");
                yield return ClickChoice("cancel");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AssignmentPopupClearDisabledBehavior()
        {
            Click(CellButton(Cell(0, true)));
            Assert.That(Choice("clear").interactable, Is.True);
            yield return ClickChoice("clear");
            yield return null;
            Click(CellButton(Cell(0, true)));
            yield return null;
            var clear = Choice("clear");
            Assert.That(clear.gameObject.activeInHierarchy, Is.True);
            Assert.That(clear.interactable, Is.False);
            string before = Placement(Draft);
            var scroll = clear.GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
            }
            Click(clear);
            Assert.That(Popup.activeSelf, Is.True);
            Assert.That(Placement(Draft), Is.EqualTo(before));
            var clearNav = clear.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
            int count = (int)Property(PopupGroup, "Count");
            for (int i = 0; i < count + 2; i++)
            {
                Dispatch("Down");
                Assert.That(Property(PopupGroup, "SelectedBehaviour"), Is.Not.SameAs(clearNav));
            }
            yield return ClickChoice("cancel");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AssignmentPopupDisabledClearHoverPreservesSelection()
        {
            foreach (bool lefty in new[] { false, true })
            {
                // Finish the previous dirty draft; reinitializing an open editor requests a real discard dialog.
                Call(_menu, "Close");
                Initialize(lefty);
                yield return null;
                var origin = Cell(0, true);
                Click(CellButton(origin));
                var reusedClear = Choice("clear");
                yield return ClickChoice("clear"); // Select and use this instance before reopening it disabled.
                yield return null;
                Click(CellButton(origin));
                yield return null;
                var clear = Choice("clear");
                Assert.That(clear, Is.SameAs(reusedClear));
                Assert.That(clear.interactable, Is.False);
                var clearNav = clear.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
                Assert.That((bool)Property(clearNav, "Selected"), Is.False, "Reused rows must discard old selection.");
                var allowed = (Func<bool>)Property(clearNav, "InputAllowed");
                Assert.That(allowed, Is.Not.Null);
                Assert.That(allowed(), Is.False, "Disabled Clear must reject mouse selection, not only controller focus.");

                // Cancel is next to Clear, so both remain visible even in a constrained viewport.
                var cancel = Choice("cancel");
                var cancelNav = cancel.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
                Select(cancelNav);
                yield return null;
                string before = Placement(Draft);
                bool dirty = (bool)Property(_menu, "IsDirty");
                var position = Center(clear);
                var hit = Raycast(position);
                Assert.That(hit == clear.gameObject || hit.transform.IsChildOf(clear.transform), Is.True,
                    "The disabled row must still receive real GraphicRaycaster hits.");
                var pointer = new PointerEventData(_events) { position = position, delta = Vector2.one };
                Assert.That(ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerEnterHandler), Is.Not.Null);
                Assert.That(ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerMoveHandler), Is.Not.Null);
                Assert.That(Property(PopupGroup, "SelectedBehaviour"), Is.SameAs(cancelNav));
                Assert.That((bool)Property(cancelNav, "Selected"), Is.True);
                Assert.That((bool)Property(clearNav, "Selected"), Is.False);
                PointerClick(hit, position);
                Assert.That(Popup.activeSelf, Is.True);
                Assert.That(CurrentGroup, Is.SameAs(PopupGroup));
                Assert.That(Property(PopupGroup, "SelectedBehaviour"), Is.SameAs(cancelNav));
                Assert.That((bool)Property(clearNav, "Selected"), Is.False);
                Assert.That(Placement(Draft), Is.EqualTo(before));
                Assert.That((bool)Property(_menu, "IsDirty"), Is.EqualTo(dirty));
                Assert.That(_writes, Is.Zero);
                Dispatch("Green"); // The preserved controller selection must still activate Cancel.
                AssertOrigin(origin);
                yield return null;
            }
        }

        private RectTransform PopupPanel => (RectTransform)Popup.transform.Find("Assignment Popup Panel");
        private Component PopupTitle => PopupPanel.Find("Assignment Popup Header Band")
            .GetComponentInChildren(Production("TMPro.TMP_Text"), true);
        private Component[] VisiblePopupLabels => ((Component[])Field(_menu, "_choiceLabels"))
            .Where(label => label.transform.parent.gameObject.activeInHierarchy).ToArray();

        private static float PreferredCaptionWidth(Component label)
        {
            // Resolve the explicit overload: TMP_Text also exposes constrained-size overloads.
            var method = label.GetType().GetMethod("GetPreferredValues", INSTANCE, null,
                new[] { typeof(string), typeof(float), typeof(float) }, null);
            return ((Vector2)method.Invoke(label, new object[] { (string)Property(label, "text"),
                Mathf.Infinity, Mathf.Infinity })).x;
        }

        private void SetCanvasHeight(float height)
        {
            // Keep the actual Game view untouched; only change this isolated canvas's units per pixel.
            _canvas.GetComponent<CanvasScaler>().enabled = false;
            _canvas.GetComponent<Canvas>().scaleFactor = Screen.height / height;
            Canvas.ForceUpdateCanvases();
        }

        private void AssertPopupGeometry(bool constrained)
        {
            Canvas.ForceUpdateCanvases();
            var panel = PopupPanel;
            var canvas = (RectTransform)_canvas.transform;
            var labels = VisiblePopupLabels;
            Assert.That(labels, Is.Not.Empty);
            Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(panel.anchorMax, Is.EqualTo(panel.anchorMin));
            Assert.That(panel.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            var panelCenter = canvas.InverseTransformPoint(panel.TransformPoint(panel.rect.center));
            Assert.That(Vector2.Distance(panelCenter, canvas.rect.center), Is.LessThan(0.5f),
                "Card must remain centered in the full canvas, not positioned beside the originating cell.");
            float captionWidth = Mathf.Max(PreferredCaptionWidth(PopupTitle), labels.Max(PreferredCaptionWidth));
            Assert.That(panel.rect.width, Is.EqualTo(Mathf.Min(captionWidth + 96f, canvas.rect.width - 48f)).Within(1f),
                "Visible marked captions and title determine width, with 48 units per side.");
            var scroll = panel.GetComponent<ScrollRect>();
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.vertical, Is.EqualTo(constrained));
            Assert.That(scroll.viewport.offsetMin.y, Is.EqualTo(15f).Within(0.5f));
            Assert.That(scroll.viewport.offsetMax.y, Is.EqualTo(-80f).Within(0.5f));
            foreach (var label in labels)
            {
                Assert.That(((RectTransform)label.transform.parent).rect.height, Is.EqualTo(64f).Within(0.5f));
                var rect = (RectTransform)label.transform;
                Assert.That(rect.offsetMin.x, Is.EqualTo(48f).Within(0.5f));
                Assert.That(rect.offsetMax.x, Is.EqualTo(-48f).Within(0.5f));
                Assert.That((bool)Property(label, "enableAutoSizing"), Is.False);
                Call(label, "ForceMeshUpdate", false, false);
                Assert.That((bool)Property(label, "isTextTruncated"), Is.False);
            }
            float desiredHeight = 80f + labels.Length * 64f + 15f;
            Assert.That(panel.rect.height, Is.EqualTo(Mathf.Min(desiredHeight, canvas.rect.height - 48f)).Within(1f));
            Assert.That(scroll.content.rect.height, Is.EqualTo(labels.Length * 64f).Within(1f));
            Assert.That(scroll.viewport.rect.height, Is.EqualTo(panel.rect.height - 95f).Within(1f));
            if (constrained)
                Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            else
                Assert.That(scroll.content.rect.height, Is.EqualTo(scroll.viewport.rect.height).Within(1f));
        }

        [UnityTest]
        public IEnumerator AssignmentPopupContentSizedGeometry()
        {
            SetCanvasHeight(1080f);
            foreach (bool lefty in new[] { false, true })
            foreach (bool drum in new[] { true, false })
            {
                Call(_menu, "Close"); // Each geometry case owns a separate, possibly dirty draft.
                Initialize(lefty);
                yield return null;
                var origin = Cell(0, drum);
                Click(CellButton(origin));
                yield return null;
                AssertPopupGeometry(false);
                yield return ClickChoice(drum ? "tom3" : "right-crash");
                yield return null;
                Click(CellButton(origin));
                yield return null;
                AssertPopupCaption(drum ? "tom3" : "right-crash", drum ? "* Floor Tom *" : "* Right Crash *");
                AssertPopupGeometry(false); // Recompute after changing the marked caption on reused rows.
                yield return ClickChoice("cancel");
                AssertOrigin(origin);
                Assert.That(_writes, Is.Zero);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AssignmentPopupConstrainedViewport()
        {
            SetCanvasHeight(540f);
            foreach (bool drum in new[] { true, false })
            {
                var origin = Cell(0, drum);
                Click(CellButton(origin));
                yield return null;
                AssertPopupGeometry(true);
                var scroll = PopupPanel.GetComponent<ScrollRect>();
                var cancel = Choice("cancel");
                Select(cancel.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable")));
                yield return null;
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, cancel.transform.parent);
                Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(scroll.viewport.rect.yMin - 1f));
                Assert.That(bounds.max.y, Is.LessThanOrEqualTo(scroll.viewport.rect.yMax + 1f));
                Click(cancel); // The revealed final row must be reachable through the clipped viewport.
                AssertOrigin(origin);
                yield return null;
                Click(CellButton(origin));
                yield return null;
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1f).Within(0.01f), "Reopening resets scroll to the first choice.");
                SetCanvasHeight(1080f);
                yield return null;
                AssertPopupGeometry(false); // Resizing an open popup must remove unnecessary scrolling.
                Dispatch("Red");
                yield return null;
                SetCanvasHeight(540f);
                yield return null;
            }
            SetCanvasHeight(540f);
            yield return null;
            Assert.That(((Button)Field(_menu, "_reverseLayout")).transform.parent.gameObject.activeSelf, Is.False,
                "An impossibly narrow action section must not overlap its neighbors.");
            Call(_menu, "UpdateTransformActions");
            Assert.That(((Button)Field(_menu, "_reverseLayout")).transform.parent.gameObject.activeSelf, Is.False,
                "State refresh must preserve the constrained-layout visibility gate.");
            SetCanvasHeight(1080f);
            yield return null;
            Assert.That(((Button)Field(_menu, "_reverseLayout")).transform.parent.gameObject.activeSelf, Is.True,
                "Returning to a supported canvas restores transform actions.");
            Assert.That(_writes, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AssignmentPopupLongContentClampsWrapsAndRevealsSelectedRowAfterResize()
        {
            SetCanvasHeight(1080f);
            var origin = Cell(0, true);
            Click(CellButton(origin));
            yield return null;
            var layout = Popup.GetComponentInChildren(Production("YARG.Menu.HighwayConfiguration.AssignmentPopupLayout"), true);
            Assert.That(layout, Is.Not.Null);
            var canvas = (RectTransform)_canvas.transform;
            var longLabel = VisiblePopupLabels[0];
            string LongCaption(Component text, string phrase)
            {
                string caption = phrase;
                do
                {
                    caption += " " + phrase;
                    text.GetType().GetProperty("text", INSTANCE).SetValue(text, caption);
                } while (PreferredCaptionWidth(text) <= canvas.rect.width * 1.25f);
                return caption;
            }
            string title = LongCaption(PopupTitle, "Choose an assignment for the selected highway column");
            Call(layout, "SetTitle", title);
            LongCaption(longLabel, "A deliberately extended localized assignment caption");
            Call(layout, "Refresh", false);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var scroll = PopupPanel.GetComponent<ScrollRect>();
            Vector2 Preferred(Component text, float width) => (Vector2)text.GetType().GetMethod("GetPreferredValues", INSTANCE,
                null, new[] { typeof(string), typeof(float), typeof(float) }, null).Invoke(text,
                    new object[] { (string)Property(text, "text"), width, Mathf.Infinity });
            float ExpectedHeader() => Mathf.Max(80f, Preferred(PopupTitle, PopupPanel.rect.width - 96f).y + 24f);
            void AssertMeasuredGeometry()
            {
                Assert.That(PopupPanel.rect.width, Is.EqualTo(canvas.rect.width - 48f).Within(0.1f));
                float rowsHeight = 0f;
                foreach (var label in VisiblePopupLabels)
                {
                    float expected = Mathf.Max(64f, Preferred(label, PopupPanel.rect.width - 96f).y + 16f);
                    var row = label.transform.parent.GetComponent<LayoutElement>();
                    Assert.That(row.preferredHeight, Is.EqualTo(expected).Within(0.01f));
                    Assert.That(row.minHeight, Is.EqualTo(expected).Within(0.01f));
                    Assert.That(((RectTransform)row.transform).rect.height, Is.EqualTo(expected).Within(0.1f));
                    rowsHeight += expected;
                }
                Assert.That(PopupTitle.transform.parent.GetComponent<RectTransform>().rect.height,
                    Is.EqualTo(ExpectedHeader()).Within(0.1f));
                var titleRect = (RectTransform)PopupTitle.transform;
                Assert.That(titleRect.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(titleRect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(titleRect.offsetMin, Is.EqualTo(new Vector2(48f, 12f)));
                Assert.That(titleRect.offsetMax, Is.EqualTo(new Vector2(-48f, -12f)));
                Assert.That(titleRect.rect.width, Is.EqualTo(PopupPanel.rect.width - 96f).Within(0.1f));
                Assert.That(titleRect.rect.height, Is.EqualTo(ExpectedHeader() - 24f).Within(0.1f));
                Call(PopupTitle, "ForceMeshUpdate", false, false);
                Assert.That((int)Field(Property(PopupTitle, "textInfo"), "lineCount"), Is.GreaterThan(1));
                Assert.That((bool)Property(PopupTitle, "isTextTruncated"), Is.False);
                var titleBounds = (Bounds)Property(PopupTitle, "textBounds");
                Assert.That(titleBounds.size.x, Is.GreaterThan(0f));
                Assert.That(titleBounds.size.y, Is.GreaterThan(0f));
                Assert.That(titleBounds.min.x, Is.GreaterThanOrEqualTo(titleRect.rect.xMin - 1f));
                Assert.That(titleBounds.max.x, Is.LessThanOrEqualTo(titleRect.rect.xMax + 1f));
                Assert.That(titleBounds.min.y, Is.GreaterThanOrEqualTo(titleRect.rect.yMin - 1f));
                Assert.That(titleBounds.max.y, Is.LessThanOrEqualTo(titleRect.rect.yMax + 1f));
                Assert.That(PopupPanel.rect.height,
                    Is.EqualTo(Mathf.Min(rowsHeight + ExpectedHeader() + 15f, canvas.rect.height - 48f)).Within(0.1f));
                Assert.That(scroll.viewport.rect.height, Is.EqualTo(PopupPanel.rect.height - ExpectedHeader() - 15f).Within(0.1f));
                Assert.That(scroll.content.rect.height, Is.EqualTo(rowsHeight).Within(0.1f));
            }
            Assert.That(Preferred(PopupTitle, PopupPanel.rect.width - 96f).y,
                Is.GreaterThan(Preferred(PopupTitle, Mathf.Infinity).y), "The title must actually wrap.");
            Assert.That(longLabel.transform.parent.GetComponent<LayoutElement>().preferredHeight, Is.GreaterThan(64f),
                "The long caption must increase its row beyond the authored minimum.");
            AssertMeasuredGeometry();
            var cancel = Choice("cancel").GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
            for (int i = 0; i < VisiblePopupLabels.Length && !ReferenceEquals(Property(PopupGroup, "SelectedBehaviour"), cancel); i++)
            {
                Dispatch("Down");
                yield return null;
            }
            Assert.That(Property(PopupGroup, "SelectedBehaviour"), Is.SameAs(cancel), "Real navigation must reach the final row.");
            float previousScrollOffset = scroll.content.anchoredPosition.y;
            SetCanvasHeight(720f);
            yield return null; // AssignmentPopupLayout.LateUpdate must preserve and reveal this selection.
            Canvas.ForceUpdateCanvases();
            AssertMeasuredGeometry();
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.movementType, Is.EqualTo(ScrollRect.MovementType.Clamped));
            Assert.That(scroll.verticalNormalizedPosition, Is.InRange(-0.0001f, 1.0001f));
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(previousScrollOffset + 1f),
                "Shrinking the viewport must actively scroll to keep the selected final row visible.");
            Assert.That(Property(PopupGroup, "SelectedBehaviour"), Is.SameAs(cancel));
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, cancel.transform.parent);
            Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(scroll.viewport.rect.yMin - 0.5f));
            Assert.That(bounds.max.y, Is.LessThanOrEqualTo(scroll.viewport.rect.yMax + 0.5f));
            Dispatch("Green");
            AssertOrigin(origin);
            Assert.That(_writes, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AssignmentPopupMarkersFollowDraft()
        {
            Click(CellButton(Cell(0, true)));
            yield return ClickChoice("clear");
            yield return null;
            var cell = Cell(0, true);
            string empty = (string)Property(Field(cell, "_label"), "text");
            Click(CellButton(cell));
            yield return ClickChoice("snare");
            yield return null;
            Assert.That(Draft.Slots[0].Drum, Is.EqualTo("snare"));
            AssertExactCellCaptions();
            Assert.That((string)Property(Field(cell, "_label"), "text"), Is.Not.EqualTo(empty));
            var fill = (Image)Field(cell, "_fill");
            var assignedColor = fill.color;
            Click(CellButton(cell));
            yield return ClickChoice("clear");
            yield return null;
            Assert.That(Draft.Slots[0].Drum, Is.Null.Or.Empty);
            Assert.That((string)Property(Field(cell, "_label"), "text"), Is.EqualTo(empty));
            Assert.That(fill.color, Is.Not.EqualTo(assignedColor));
            foreach (string id in new[] { "tom1", "tom2", "tom3", "left-crash", "right-crash", "kick" })
            {
                bool drum = id.StartsWith("tom", StringComparison.Ordinal) || id == "kick";
                Click(CellButton(Cell(0, drum)));
                string popupCaption = id switch
                {
                    "tom1" => "HIGH TOM", "tom2" => "MID TOM", "tom3" => "FLOOR TOM",
                    "left-crash" => "LEFT CRASH", "right-crash" => "RIGHT CRASH", _ => "ALL KICKS"
                };
                var formatter = Production("YARG.Menu.HighwayConfiguration.EliteDrumsAssignmentChoice")
                    .GetMethod("FormatLabel", BindingFlags.Public | BindingFlags.Static);
                string markedCaption = (string)formatter.Invoke(null, new object[] { Draft, 0, drum, id, popupCaption });
                Assert.That((string)Property(((Component[])Field(_menu, "_choiceLabels"))[Array.IndexOf(
                    (string[])Field(_menu, "_choiceIds"), id)], "text"), Is.EqualTo(markedCaption));
                yield return ClickChoice(id);
                yield return null;
                var slot = Draft.Slots[0];
                if (id == "kick") Assert.That(slot.Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick));
                else Assert.That(drum ? slot.Drum : slot.Cymbal, Is.EqualTo(id), "Placement must use the saved ID, not the caption.");
                AssertExactCellCaptions();
            }
            Assert.That(_writes, Is.Zero);
        }

        private void AssertExactCellCaptions()
        {
            var captions = new Dictionary<string, string>
            {
                ["left-crash"] = "Left\nCrash", ["right-crash"] = "Right\nCrash",
                ["tom1"] = "High\nTom", ["tom2"] = "Mid\nTom", ["tom3"] = "Floor\nTom",
                ["kick"] = "All\nKicks", ["kick1"] = "Kick\n1", ["kick2"] = "Kick\n2",
                ["snare"] = "Snare", ["ride"] = "Ride\nCymbal", ["empty"] = "Empty",
                ["not-applicable"] = "N/A"
            };
            for (int column = 0; column < Draft.Slots.Length; column++)
            foreach (bool drum in new[] { false, true })
            {
                var slot = Draft.Slots[column];
                string id = slot.Kind switch
                {
                    EliteDrumsLayoutSlotKind.HiHat => "hi-hat",
                    EliteDrumsLayoutSlotKind.Kick => drum ? "kick" : "not-applicable",
                    EliteDrumsLayoutSlotKind.Kick1 => drum ? "kick1" : "not-applicable",
                    EliteDrumsLayoutSlotKind.Kick2 => drum ? "kick2" : "not-applicable",
                    _ => (drum ? slot.Drum : slot.Cymbal) ?? "empty"
                };
                string expected = id == "hi-hat" ? drum ? "Hi-Hat\nPedal" : "Hi-Hat\nCymbal" : captions[id];
                Assert.That((string)Property(Field(Cell(column, drum), "_label"), "text"), Is.EqualTo(expected),
                    $"Saved column {column}, drum={drum}, id={id} must display its exact production caption.");
            }
        }

        [UnityTest]
        public IEnumerator AssignmentMarkersDoNotChangePlacement()
        {
            foreach (bool lefty in new[] { false, true })
            {
                Initialize(lefty);
                yield return null;
                string before = Placement(Draft);
                Call(_menu, "RefreshCellIndicators");
                Call(_menu, "RefreshCellIndicators");
                AssertExactCellCaptions();
                Click(CellButton(Cell(0, true)));
                Dispatch("Down");
                Dispatch("Red");
                Assert.That(Placement(Draft), Is.EqualTo(before), "Rendering and focusing markers cannot place or move notes.");
                Assert.That((bool)Property(_menu, "IsDirty"), Is.False);
                Assert.That(_writes, Is.Zero);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator LeftyToggleVisualStateAndFocus()
        {
            var toggle = (Toggle)Field(_menu, "_leftyFlipToggle");
            var nav = toggle.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
            Assert.That(toggle.isOn, Is.False);
            Select(nav);
            Dispatch("Green");
            Assert.That(toggle.isOn, Is.True);
            Assert.That(toggle.graphic.gameObject.activeInHierarchy, Is.True);
            Assert.That((bool)Property(nav, "Selected"), Is.True);
            Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(nav));
            Assert.That(_writes, Is.EqualTo(1));
            Assert.That(Center(Cell(0, true)).x, Is.GreaterThan(Center(Cell(9, true)).x));
            yield return null;
            Click(toggle);
            Assert.That(toggle.isOn, Is.False);
            Assert.That(_writes, Is.EqualTo(2));
            Assert.That(Center(Cell(0, true)).x, Is.LessThan(Center(Cell(9, true)).x));
            AssertExactCellCaptions();
        }

        [UnityTest]
        public IEnumerator LeftyToggleRepeatedOpenSingleMutation()
        {
            for (int i = 0; i < 3; i++)
            {
                if (i != 0)
                {
                    _root.SetActive(false);
                    yield return null;
                    _root.SetActive(true);
                    Initialize(false);
                    yield return null;
                }
                int before = _writes;
                var toggle = (Toggle)Field(_menu, "_leftyFlipToggle");
                Select(toggle.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable")));
                Dispatch("Green");
                Assert.That(toggle.isOn, Is.True);
                Assert.That(_writes, Is.EqualTo(before + 1), "Reopening must not accumulate toggle callbacks.");
                Click(CellButton(Cell(0, true)));
                Dispatch("Red");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AssignmentPopupRoundedSilhouette()
        {
            foreach (int height in new[] { 720, 1080 })
            {
                ConfigureCapture(height);
                yield return null;
                foreach (bool drum in new[] { true, false })
                {
                    Click(CellButton(Cell(0, drum)));
                    yield return null;
                    AssertPopupGeometry(false);
                    AssertRoundedSilhouette();
                    CaptureState(height, false, drum ? "geometry-drum" : "geometry-cymbal");
                    Dispatch("Red");
                    yield return null;
                }
            }
        }

        private void ConfigureCapture(int height)
        {
            if (_captureCamera == null)
            {
                var cameraObject = new GameObject("Assignment acceptance capture camera", typeof(Camera));
                cameraObject.transform.SetParent(_canvas.transform, false);
                _captureCamera = cameraObject.GetComponent<Camera>();
                _captureCamera.orthographic = true;
                _captureCamera.transform.localPosition = new Vector3(0f, 0f, -100f);
                _captureCamera.clearFlags = CameraClearFlags.SolidColor;
                _captureCamera.backgroundColor = Color.black;
            }
            if (_captureTarget != null)
            {
                _captureCamera.targetTexture = null;
                _captureTarget.Release();
                UnityEngine.Object.Destroy(_captureTarget);
            }
            _captureTarget = new RenderTexture(height * 16 / 9, height, 24);
            _captureTarget.Create();
            _captureCamera.targetTexture = _captureTarget;
            _captureCamera.aspect = 16f / 9f;
            _captureCamera.pixelRect = new Rect(0f, 0f, _captureTarget.width, height);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _captureCamera;
            canvas.planeDistance = 10f;
            _canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = height / 1080f;
            Canvas.ForceUpdateCanvases();
            Assert.That(canvas.pixelRect.height, Is.EqualTo(height).Within(1f));
            Assert.That(canvas.pixelRect.width, Is.EqualTo(_captureTarget.width).Within(1f));
        }

        private void AssertRoundedSilhouette()
        {
            Canvas.ForceUpdateCanvases();
            var panel = PopupPanel;
            var samples = new[]
            {
                new Vector2(panel.rect.xMin + 1f, panel.rect.yMax - 1f),
                new Vector2(panel.rect.xMax - 1f, panel.rect.yMax - 1f),
                new Vector2(panel.rect.xMin + 1f, panel.rect.yMin + 1f),
                new Vector2(panel.rect.xMax - 1f, panel.rect.yMin + 1f),
                new Vector2(panel.rect.center.x, panel.rect.yMax - 2f),
                // Footer artwork reserves transparent shadow below its filled band.
                new Vector2(panel.rect.center.x, panel.rect.yMin + 10f)
            }.Select(point => RectTransformUtility.WorldToScreenPoint(_captureCamera,
                panel.TransformPoint(point))).ToArray();
            Texture2D withPanel = null;
            Texture2D withoutPanel = null;
            var backdrop = Popup.GetComponent<Image>();
            var originalBackdrop = backdrop.color;
            try
            {
                // A dark footer over a dark menu can be opaque without producing measurable contrast.
                // Use a controlled background to test the authored alpha silhouette, not menu colors.
                backdrop.color = Color.white;
                Canvas.ForceUpdateCanvases();
                withPanel = ReadCapture();
                panel.gameObject.SetActive(false);
                Canvas.ForceUpdateCanvases();
                withoutPanel = ReadCapture();
                for (int i = 0; i < samples.Length; i++)
                {
                    int x = Mathf.RoundToInt(samples[i].x);
                    int y = Mathf.RoundToInt(samples[i].y);
                    Assert.That(x, Is.InRange(0, withPanel.width - 1));
                    Assert.That(y, Is.InRange(0, withPanel.height - 1));
                    var difference = withPanel.GetPixel(x, y) - withoutPanel.GetPixel(x, y);
                    float change = Mathf.Max(Mathf.Abs(difference.r), Mathf.Abs(difference.g), Mathf.Abs(difference.b));
                    if (i < 4)
                        Assert.That(change, Is.LessThan(0.025f), "Rounded outer corner must expose the backdrop: sample " + i);
                    else
                        Assert.That(change, Is.GreaterThan(0.05f), "Header/footer center must remain visibly filled: sample " + i);
                }
            }
            finally
            {
                backdrop.color = originalBackdrop;
                panel.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                if (withPanel != null) UnityEngine.Object.Destroy(withPanel);
                if (withoutPanel != null) UnityEngine.Object.Destroy(withoutPanel);
            }
        }

        private static object TextMember(object owner, string name)
        {
            var field = owner.GetType().GetField(name, INSTANCE);
            return field != null ? field.GetValue(owner) : owner.GetType().GetProperty(name, INSTANCE).GetValue(owner);
        }

        private void HoverTransform(Button button)
        {
            var position = Center(button);
            var hit = Raycast(position);
            Assert.That(hit == button.gameObject || hit.transform.IsChildOf(button.transform), Is.True,
                "Even a disabled shift must receive real GraphicRaycaster pointer input.");
            var pointer = new PointerEventData(_events) { position = position, delta = Vector2.one };
            Assert.That(ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerEnterHandler), Is.Not.Null);
            Assert.That(ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerMoveHandler), Is.Not.Null);
            // Transform controls select on press, not hover. Do not click/activate the action here.
            Assert.That(ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler), Is.Not.Null);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerUpHandler);
        }

        [UnityTest]
        public IEnumerator ShiftBoundaryRetainsFocusAndNavigatesFromDisabledPointerOrigin()
        {
            var runtimeType = Production("YARG.Menu.Navigation.RuntimeNavigatable");
            foreach (bool lefty in new[] { false, true })
            foreach (bool shiftLeft in new[] { true, false })
            {
                // Close first: a dirty isolated fixture has no discard-dialog prefab to service Initialize.
                Call(_menu, "Close");
                Initialize(lefty);
                yield return null;
                var shift = (Button)Field(_menu, shiftLeft ? "_shiftLeft" : "_shiftRight");
                var opposite = (Button)Field(_menu, shiftLeft ? "_shiftRight" : "_shiftLeft");
                var reverse = (Button)Field(_menu, "_reverseLayout");
                var toggle = (Toggle)Field(_menu, "_leftyFlipToggle");
                var shiftNav = shift.GetComponent(runtimeType);
                var reverseNav = reverse.GetComponent(runtimeType);
                var toggleNav = toggle.GetComponent(runtimeType);
                string context = $"lefty={lefty}, shiftLeft={shiftLeft}";

                // Start at the opposite edge so both physical shift actions actually move columns.
                HoverTransform(opposite);
                for (int step = 0; opposite.interactable && step < EliteDrumsLayoutPreset.SLOT_COUNT; step++)
                    Dispatch("Green");
                Assert.That(opposite.interactable, Is.False, context);
                Assert.That(shift.interactable, Is.True, context);
                HoverTransform(shift);
                Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(shiftNav), context);
                Dispatch("Up");
                var enabledUp = (Component)Property(Grid, "SelectedBehaviour");
                Assert.That(enabledUp.GetType().Name, Is.EqualTo("EliteDrumsLayoutCell"), context);
                Assert.That((bool)Property(enabledUp, "Drum"), Is.True, context);
                Assert.That(CellButton(enabledUp).interactable, Is.True, context);
                HoverTransform(shift);

                int moves = 0;
                while (shift.interactable && moves < EliteDrumsLayoutPreset.SLOT_COUNT)
                {
                    string before = Placement(Draft);
                    Dispatch("Green");
                    yield return null; // Catch any deferred render-time selection changes as well.
                    Assert.That(Placement(Draft), Is.Not.EqualTo(before), context);
                    Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(shiftNav), context);
                    moves++;
                }
                Assert.That(moves, Is.GreaterThan(0), context);
                Assert.That(shift.interactable, Is.False, context);
                Assert.That((bool)Property(shiftNav, "Selected"), Is.True, context);
                string boundary = Placement(Draft);
                bool dirty = (bool)Property(_menu, "IsDirty");
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    Dispatch("Green");
                    yield return null;
                    Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(shiftNav), context);
                    Assert.That(Placement(Draft), Is.EqualTo(boundary), context);
                    Assert.That((bool)Property(_menu, "IsDirty"), Is.EqualTo(dirty), context);
                    Assert.That(_writes, Is.Zero, context);
                }

                foreach (string action in new[] { "Left", "Down", "Right", "Up" })
                {
                    // Re-enter using actual mouse input, including when the Button is disabled.
                    HoverTransform(shift);
                    Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(shiftNav), context);
                    Dispatch(action);
                    Component expected = action == "Up" ? enabledUp :
                        action == "Down" || shiftLeft && action == "Left" ? shiftNav :
                        action == "Left" || shiftLeft ? reverseNav : toggleNav;
                    Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(expected), context + ", " + action);
                    Assert.That(Placement(Draft), Is.EqualTo(boundary), context);
                    Assert.That(_writes, Is.Zero, context);
                }
                if (!shiftLeft)
                {
                    HoverTransform(reverse);
                    Dispatch("Right");
                    Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(toggleNav),
                        context + ": Swap must skip the disabled right shift when it is not the origin.");
                }
                Assert.That(CurrentGroup, Is.SameAs(Grid), context);
                Assert.That(Popup.activeSelf, Is.False, context);
            }
        }

        [UnityTest]
        public IEnumerator TransformActionsRenderedAtReferenceResolutions()
        {
            foreach (int height in new[] { 720, 1080 })
            {
                ConfigureCapture(height);
                yield return null;
                foreach (bool lefty in new[] { false, true })
                {
                    Initialize(lefty);
                    yield return null;
                    string before = Placement(Draft);
                    var labels = new[] { "_shiftLeft", "_reverseLayout", "_shiftRight" }
                        .Select(name => ((Button)Field(_menu, name)).transform.parent
                            .GetComponentInChildren(Production("TMPro.TMP_Text"), true))
                        .ToArray();
                    foreach (var label in labels)
                    {
                        Assert.That(label, Is.Not.Null);
                        Assert.That(label.gameObject.activeInHierarchy, Is.True);
                        Call(label, "ForceMeshUpdate", false, false);
                        string caption = (string)Property(label, "text");
                        Assert.That(caption, Is.Not.Empty);
                        if (label == labels[0]) Assert.That(caption, Does.StartWith("\u2190 "));
                        else if (label == labels[1])
                        {
                            Assert.That(caption, Does.StartWith("\u21C4 "));
                            Assert.That(caption, Does.EndWith(" \u21C6"));
                        }
                        else Assert.That(caption, Does.EndWith(" \u2192"));
                        Assert.That((bool)Property(label, "isTextTruncated"), Is.False);
                        var info = Property(label, "textInfo");
                        int count = (int)TextMember(info, "characterCount");
                        var characters = (Array)TextMember(info, "characterInfo");
                        var meshes = (Array)TextMember(info, "meshInfo");
                        int visible = 0;
                        for (int i = 0; i < count; i++)
                        {
                            var character = characters.GetValue(i);
                            char value = (char)TextMember(character, "character");
                            if (char.IsWhiteSpace(value)) continue;
                            Assert.That((bool)TextMember(character, "isVisible"), Is.True,
                                $"{caption}: U+{(int)value:X4} must generate a visible glyph at {height}p.");
                            var element = TextMember(character, "textElement");
                            Assert.That(element, Is.Not.Null);
                            var glyph = TextMember(element, "glyph");
                            Assert.That((uint)TextMember(glyph, "index"), Is.Not.Zero,
                                $"{caption}: U+{(int)value:X4} must not use the missing-glyph index.");
                            int material = (int)TextMember(character, "materialReferenceIndex");
                            int vertex = (int)TextMember(character, "vertexIndex");
                            var vertices = (Vector3[])TextMember(meshes.GetValue(material), "vertices");
                            Assert.That(Vector3.Distance(vertices[vertex], vertices[vertex + 2]), Is.GreaterThan(0.1f),
                                "Every transform symbol must have a nondegenerate rendered mesh quad.");
                            visible++;
                        }
                        Assert.That(visible, Is.GreaterThan(0));
                        var rect = (RectTransform)label.transform;
                        var bounds = (Bounds)Property(label, "textBounds");
                        Assert.That(bounds.size.x, Is.LessThanOrEqualTo(rect.rect.width + 1f));
                        Assert.That(bounds.size.y, Is.LessThanOrEqualTo(rect.rect.height + 1f));
                        var min = RectTransformUtility.WorldToScreenPoint(_captureCamera, rect.TransformPoint(bounds.min));
                        var max = RectTransformUtility.WorldToScreenPoint(_captureCamera, rect.TransformPoint(bounds.max));
                        Assert.That(max.y - min.y, Is.GreaterThanOrEqualTo(9f), "Transform glyphs must remain readable at both resolutions.");
                        Assert.That(min.x, Is.GreaterThanOrEqualTo(0f));
                        Assert.That(min.y, Is.GreaterThanOrEqualTo(0f));
                        Assert.That(max.x, Is.LessThanOrEqualTo(_captureTarget.width));
                        Assert.That(max.y, Is.LessThanOrEqualTo(height));
                        Texture2D shown = null;
                        Texture2D hidden = null;
                        try
                        {
                            shown = ReadCapture();
                            ((Behaviour)label).enabled = false;
                            Canvas.ForceUpdateCanvases();
                            hidden = ReadCapture();
                            int changed = 0;
                            for (int y = Mathf.Max(0, Mathf.FloorToInt(min.y)); y < Mathf.Min(height, Mathf.CeilToInt(max.y)); y++)
                            for (int x = Mathf.Max(0, Mathf.FloorToInt(min.x)); x < Mathf.Min(_captureTarget.width, Mathf.CeilToInt(max.x)); x++)
                            {
                                var delta = shown.GetPixel(x, y) - hidden.GetPixel(x, y);
                                if (Mathf.Max(Mathf.Abs(delta.r), Mathf.Abs(delta.g), Mathf.Abs(delta.b)) > 0.025f) changed++;
                            }
                            Assert.That(changed, Is.GreaterThanOrEqualTo(visible * 2),
                                $"{caption} must contribute visible framebuffer pixels, not merely populate TMP metadata ({height}p).");
                            Debug.Log($"[Transform glyph render] {height}p lefty={lefty} caption='{caption}' glyphs={visible} pixels={changed}");
                        }
                        finally
                        {
                            ((Behaviour)label).enabled = true;
                            if (shown != null) UnityEngine.Object.Destroy(shown);
                            if (hidden != null) UnityEngine.Object.Destroy(hidden);
                            Canvas.ForceUpdateCanvases();
                        }
                    }
                    CaptureState(height, lefty, "transform-glyphs");
                    Assert.That(Placement(Draft), Is.EqualTo(before));
                    Assert.That(_writes, Is.Zero, "Rendering transform controls must not invoke their actions.");
                }
            }
        }

        private Texture2D ReadCapture()
        {
            _captureCamera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(_captureTarget.width, _captureTarget.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = _captureTarget;
                image.ReadPixels(new Rect(0f, 0f, image.width, image.height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        [UnityTest]
        public IEnumerator AssignmentPopupRenderedAtReferenceResolutions()
        {
            var cameraObject = new GameObject("Assignment popup capture camera", typeof(Camera));
            cameraObject.transform.SetParent(_canvas.transform, false);
            _captureCamera = cameraObject.GetComponent<Camera>();
            _captureCamera.orthographic = true;
            _captureCamera.transform.localPosition = new Vector3(0f, 0f, -100f);
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = Color.black;
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _captureCamera;
            canvas.planeDistance = 10f;
            foreach (int height in new[] { 720, 1080 })
            {
                if (_captureTarget != null)
                {
                    _captureCamera.targetTexture = null;
                    _captureTarget.Release();
                    UnityEngine.Object.Destroy(_captureTarget);
                }
                _captureTarget = new RenderTexture(height * 16 / 9, height, 24);
                _captureTarget.Create();
                _captureCamera.targetTexture = _captureTarget;
                _captureCamera.aspect = (float)_captureTarget.width / height;
                _captureCamera.pixelRect = new Rect(0f, 0f, _captureTarget.width, height);
                _canvas.GetComponent<CanvasScaler>().enabled = false;
                canvas.scaleFactor = _captureTarget.width / 1920f;
                Canvas.ForceUpdateCanvases();
                Assert.That(canvas.worldCamera, Is.SameAs(_captureCamera));
                Assert.That(_canvas.GetComponent<GraphicRaycaster>().eventCamera, Is.SameAs(_captureCamera));
                Assert.That(canvas.pixelRect.width, Is.EqualTo(_captureTarget.width).Within(1f));
                Assert.That(canvas.pixelRect.height, Is.EqualTo(height).Within(1f));
                EventSystem.current = _events;
                foreach (bool lefty in new[] { false, true })
                {
                    // End the previous isolated capture session before starting a fresh draft.
                    Call(_menu, "Close");
                    Initialize(lefty);
                    yield return null;
                    Click(CellButton(Cell(0, true)));
                    Canvas.ForceUpdateCanvases();
                    CaptureState(height, lefty, "ordinary");
                    Dispatch("Red");
                    yield return null;

                    // Construct split kicks using only the real popup's assignment actions.
                    Click(CellButton(Cell(8, true)));
                    yield return ClickChoice("kick");
                    yield return null;
                    Click(CellButton(Cell(9, true)));
                    yield return ClickChoice("kick2");
                    yield return null;
                    Click(CellButton(Cell(0, true)));
                    yield return ClickChoice("clear");
                    yield return null;
                    Click(CellButton(Cell(1, true)));
                    if (Choice("clear").interactable) yield return ClickChoice("clear");
                    else yield return ClickChoice("cancel");
                    yield return null;
                    Click(CellButton(Cell(0, true)));
                    yield return ClickChoice("tom3");
                    yield return null;
                    Click(CellButton(Cell(0, true)));
                    yield return null;
                    Assert.That(((string[])Field(_menu, "_choiceIds")).Length, Is.EqualTo(10),
                        "Split kicks at other columns expose all ten drum choices.");
                    AssertPopupCaption("tom3", "* Floor Tom *");
                    AssertPopupCaption("snare", "[ Snare ]");
                    AssertPopupCaption("kick", "All Kicks");
                    AssertPopupCaption("kick1", "Kick 1");
                    AssertPopupCaption("kick2", "Kick 2");
                    CaptureState(height, lefty, "max10-long-marked");
                    Select(Choice("cancel").GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable")));
                    yield return null;
                    CaptureState(height, lefty, "cancel-selected");
                    yield return ClickChoice("cancel");
                    yield return null;
                    Click(CellButton(Cell(0, true)));
                    yield return null;
                    Select(Choice("clear").GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable")));
                    yield return null;
                    CaptureState(height, lefty, "clear-selected");
                    yield return ClickChoice("clear");
                    yield return null;
                    Click(CellButton(Cell(0, true)));
                    yield return null;
                    Assert.That(Choice("clear").interactable, Is.False);
                    AssertPopupCaption("tom3", "[ Floor Tom ]");
                    var scroll = Choice("clear").GetComponentInParent<ScrollRect>();
                    scroll.verticalNormalizedPosition = 0f;
                    yield return null;
                    CaptureState(height, lefty, "empty-disabled-clear");
                    yield return ClickChoice("cancel");
                    yield return null;

                    Click(CellButton(Cell(0, false)));
                    yield return ClickChoice("right-crash");
                    yield return null;
                    Click(CellButton(Cell(0, false)));
                    yield return null;
                    AssertPopupCaption("right-crash", "* Right Crash *");
                    CaptureState(height, lefty, "longest-current-crash");
                    yield return ClickChoice("cancel");
                    yield return null;

                    // Hi-hat occupies both rows, but the pedal/cymbal captions remain distinct.
                    Click(CellButton(Cell(0, true)));
                    yield return ClickChoice("hi-hat");
                    yield return null;
                    Click(CellButton(Cell(0, true)));
                    yield return null;
                    AssertPopupCaption("hi-hat", "* Hi-Hat\nPedal *");
                    CaptureState(height, lefty, "hihat-pedal-current");
                    yield return ClickChoice("cancel");
                    yield return null;
                    Click(CellButton(Cell(0, false)));
                    yield return null;
                    AssertPopupCaption("hi-hat", "* Hi-Hat\nCymbal *");
                    CaptureState(height, lefty, "hihat-cymbal-current");
                    yield return ClickChoice("cancel");
                    yield return null;
                    Click(CellButton(Cell(1, true)));
                    yield return null;
                    AssertPopupCaption("hi-hat", "Hi-Hat\nPedal");
                    AssertPopupCaption("kick1", "Kick 1");
                    AssertPopupCaption("kick2", "Kick 2");
                    yield return ClickChoice("cancel");
                    yield return null;

                    Click(CellButton(Cell(8, true)));
                    yield return null;
                    // The editor offers the other numbered kick, never the row's current number.
                    Assert.That(((string[])Field(_menu, "_choiceIds")), Does.Not.Contain("kick1"));
                    AssertPopupCaption("kick2", "Kick 2");
                    AssertPopupCaption("kick", "All Kicks");
                    CaptureState(height, lefty, "split-kick-current-row");
                    yield return ClickChoice("cancel");
                    yield return null;

                    var toggle = (Toggle)Field(_menu, "_leftyFlipToggle");
                    var toggleNav = toggle.GetComponent(Production("YARG.Menu.Navigation.RuntimeNavigatable"));
                    Select(toggleNav);
                    if (toggle.isOn) Dispatch("Green");
                    yield return null;
                    CaptureState(height, lefty, "toggle-off-focused-separator");
                    Dispatch("Green");
                    yield return null;
                    Assert.That(toggle.isOn, Is.True);
                    Assert.That(Property(Grid, "SelectedBehaviour"), Is.SameAs(toggleNav));
                    CaptureState(height, lefty, "toggle-on-focused-separator");
                }
            }
        }

        private void AssertPopupCaption(string id, string expected)
        {
            int index = Array.IndexOf((string[])Field(_menu, "_choiceIds"), id);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That((string)Property(((Component[])Field(_menu, "_choiceLabels"))[index], "text"), Is.EqualTo(expected.Replace("\n", " ").ToUpperInvariant()));
        }

        private void CaptureState(int height, bool lefty, string state)
        {
                    Canvas.ForceUpdateCanvases();
                    var texts = _root.GetComponentsInChildren(Production("TMPro.TMP_Text"), false).Cast<Component>()
                        .Where(text => Popup.activeSelf ? text.transform.IsChildOf(Popup.transform) :
                            text.transform.IsChildOf(((Toggle)Field(_menu, "_leftyFlipToggle")).transform));
                    foreach (var text in texts)
                    {
                        Call(text, "ForceMeshUpdate", false, false);
                        var rect = (RectTransform)text.transform;
                        var bounds = (Bounds)Property(text, "textBounds");
                        float glyphHeight = Vector2.Distance(
                            RectTransformUtility.WorldToScreenPoint(_captureCamera, rect.TransformPoint(bounds.min)),
                            RectTransformUtility.WorldToScreenPoint(_captureCamera,
                                rect.TransformPoint(bounds.min + Vector3.up * bounds.size.y)));
                        if (string.IsNullOrEmpty((string)Property(text, "text"))) continue;
                        Debug.Log($"[Assignment popup bounds] target={_captureTarget.width}x{height} lefty={lefty} " +
                            $"path={Hierarchy(text)} text='{Property(text, "text")}' rect={rect.rect} " +
                            $"glyphHeight={glyphHeight:F2} font={Property(text, "fontSize")}");
                        Assert.That((bool)Property(text, "isTextTruncated"), Is.False);
                        Assert.That(glyphHeight, Is.GreaterThanOrEqualTo(9f));
                        Assert.That(bounds.size.x, Is.LessThanOrEqualTo(rect.rect.width + 1f), "Caption must fit its authored width.");
                        Assert.That(bounds.size.y, Is.LessThanOrEqualTo(rect.rect.height + 1f), "Caption must fit its authored height.");
                    }
                    _captureCamera.Render();
                    var previous = RenderTexture.active;
                    var image = new Texture2D(_captureTarget.width, height, TextureFormat.RGB24, false);
                    try
                    {
                        RenderTexture.active = _captureTarget;
                        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                        image.Apply();
                        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                            $"assignment-popup-{height}-{(lefty ? "lefty" : "normal")}-{state}-{Guid.NewGuid():N}.png");
                        System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
                        Debug.Log("[Assignment popup capture] " + path);
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        UnityEngine.Object.Destroy(image);
                    }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_captureCamera != null) _captureCamera.targetTexture = null;
            if (_captureTarget != null)
            {
                _captureTarget.Release();
                UnityEngine.Object.Destroy(_captureTarget);
            }
            if (_root != null) UnityEngine.Object.Destroy(_root);
            yield return null; // Let OnDisable remove the menu's scheme while services are still alive.
            if (_services != null) UnityEngine.Object.Destroy(_services);
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
            yield return null;
            if (_groups != null && _previousGroups != null)
            {
                _groups.Clear();
                foreach (var group in _previousGroups) _groups.Add(group);
            }
            if (_previousEvents != null)
            {
                _previousEvents.enabled = _previousEventsEnabled;
                EventSystem.current = _previousEvents;
            }
            if (_bootstrap != null) yield return _bootstrap.TearDown();
        }
    }
}
