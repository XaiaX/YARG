using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteDrumsLayoutMenuPlayModeTests
    {
        private const string TOOLTIP_PATH = "/Music/Community/An intentionally long directory name/More long directory names/Updates/songs_updates.dta";

        private static Component PreparePathTooltip(SongManagerInteractionState state, out Component row)
        {
            // The shared fixture's bare container has no viewport width. The path label is
            // stretched with a -400 offset, so lay out a real positive-width row before fitting.
            var container = (RectTransform)state.Container.transform;
            container.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 800);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
            var displayType = Production("YARG.Menu.Settings.SongSourcePathDisplay");
            var display = state.Container.GetComponentsInChildren(displayType).First();
            row = display.GetComponentInParent(Production("YARG.Menu.Settings.SettingsDirectory"));
            var label = display.GetComponent(Production("TMPro.TextMeshProUGUI"));
            displayType.GetMethod("SetPath").Invoke(null, new object[] { label, TOOLTIP_PATH });
            Assert.That(((RectTransform)label.transform).rect.width, Is.GreaterThan(0),
                "Tooltip timing requires a laid-out path label with positive width.");
            Assert.That((string)label.GetType().GetProperty("text").GetValue(label), Is.Not.EqualTo(TOOLTIP_PATH),
                "Tooltip timing requires a path that does not fit; fitting paths intentionally have no popup.");
            return display;
        }

        private static GameObject PathPopup(Component display) => (GameObject)display.GetType()
            .GetField("_detail", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(display);

        private static bool PathPopupVisible(Component display) => PathPopup(display) && PathPopup(display).activeSelf;

        private static void PathSelection(Component control, bool selected, string origin) => control.GetType()
            .GetMethod("SetSelected").Invoke(control, new[] { (object)selected,
                Enum.Parse(Production("YARG.Menu.Navigation.SelectionOrigin"), origin) });

        private static void AssertPathPopupBounds(GameObject popup)
        {
            var corners = new Vector3[4];
            ((RectTransform)popup.transform).GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, corner);
                Assert.That(screen.x, Is.InRange(0f, (float)Screen.width));
                Assert.That(screen.y, Is.InRange(0f, (float)Screen.height));
            }
        }

        [UnityTest]
        public IEnumerator SourcePathTooltipDelaysCancelsUsesRoundedStyleAndClampsToScreen()
        {
            using var state = new SongManagerInteractionState(this);
            var display = PreparePathTooltip(state, out var row);
            yield return null;
            var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width - 2, 2) };
            ExecuteEvents.Execute(display.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Assert.That(PathPopupVisible(display), Is.False, "Hover must not open a popup immediately.");
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(PathPopupVisible(display), Is.False, "Hover must wait 200ms.");
            ExecuteEvents.Execute(display.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(PathPopupVisible(display), Is.False, "Leaving before the delay cancels the popup.");
            float timeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                ExecuteEvents.Execute(display.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                yield return new WaitForSecondsRealtime(0.25f);
                yield return null;
                Assert.That(PathPopupVisible(display), Is.True, "The hover delay uses unscaled time.");
                var popup = PathPopup(display);
                var background = popup.GetComponent<Image>();
                Assert.That(background.sprite, Is.Not.Null, "Use existing rounded menu artwork, not a rectangular fill.");
                Assert.That(background.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(background.raycastTarget, Is.False);
                var outline = popup.transform.Find("SelectedOutline").GetComponent<Image>();
                Assert.That(outline.sprite, Is.Not.Null);
                Assert.That(outline.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(outline.color.a, Is.InRange(0.1f, 0.4f));
                AssertPathPopupBounds(popup);
                pointer.position = new Vector2(20, Screen.height - 20);
                ExecuteEvents.Execute(display.gameObject, pointer, ExecuteEvents.pointerMoveHandler);
                yield return null;
                Vector2 anchor = RectTransformUtility.WorldToScreenPoint(null, popup.transform.position);
                Assert.That(Vector2.Distance(anchor, pointer.position), Is.LessThan(50), "Popup stays near the cursor, not the row center.");
                AssertPathPopupBounds(popup);
                ExecuteEvents.Execute(display.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                Assert.That(PathPopupVisible(display), Is.False);
                var control = row.GetComponentsInChildren(Production("YARG.Menu.Navigation.NavigatableBehaviour")).First();
                PathSelection(control, false, "Programmatically");
                PathSelection(control, true, "Mouse");
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(PathPopupVisible(display), Is.False, "Sticky mouse selection is not controller focus.");
                PathSelection(control, false, "Programmatically");
                PathSelection(control, true, "Navigation");
                Assert.That(PathPopupVisible(display), Is.False);
                yield return new WaitForSecondsRealtime(0.08f);
                PathSelection(control, false, "Programmatically");
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(PathPopupVisible(display), Is.False, "Focus loss cancels pending display.");
                PathSelection(control, true, "Navigation");
                yield return new WaitForSecondsRealtime(0.25f);
                yield return null;
                Assert.That(PathPopupVisible(display), Is.True);
                Assert.That(PathPopup(display), Is.SameAs(popup), "Hover/focus cycles reuse the same popup.");
                PathSelection(control, false, "Programmatically");
                Assert.That(PathPopupVisible(display), Is.False);
                display.gameObject.SetActive(false);
                Assert.That(PathPopupVisible(display), Is.False);
            }
            finally
            {
                Time.timeScale = timeScale;
            }
        }

        [UnityTest]
        public IEnumerator SourcePathTooltipForwardsWheelAndWaitsForScrollInertiaToSettle()
        {
            using var state = new SongManagerInteractionState(this);
            var display = PreparePathTooltip(state, out var row);
            var viewportObject = new GameObject("Tooltip scroll viewport", typeof(RectTransform), typeof(ScrollRect));
            viewportObject.transform.SetParent(state.Container.transform.parent, false);
            var viewport = (RectTransform)viewportObject.transform;
            viewport.sizeDelta = new Vector2(800, 150);
            state.Container.transform.SetParent(viewport, false);
            var content = (RectTransform)state.Container.transform;
            content.sizeDelta = new Vector2(800, 1000);
            var scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Unrestricted;
            // Binding happens after the full hierarchy exists, just as it does in SettingsMenu.
            display.GetType().GetMethod("BindNavigation").Invoke(display, new object[] { row.gameObject });
            yield return null;
            var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(30, Screen.height - 30) };
            ExecuteEvents.Execute(display.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            yield return new WaitForSecondsRealtime(0.25f);
            yield return null;
            Assert.That(PathPopupVisible(display), Is.True);
            Vector2 before = content.anchoredPosition;
            pointer.scrollDelta = new Vector2(0, -3);
            ExecuteEvents.ExecuteHierarchy(display.gameObject, pointer, ExecuteEvents.scrollHandler);
            Assert.That(PathPopupVisible(display), Is.False, "Wheel closes an already open popup immediately.");
            Assert.That(content.anchoredPosition, Is.Not.EqualTo(before), "Label must forward wheel input to its ScrollRect.");
            scroll.velocity = new Vector2(0, 300);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(PathPopupVisible(display), Is.False, "Inertia suppresses the tooltip beyond the hover deadline.");
            scroll.StopMovement();
            yield return null;
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(PathPopupVisible(display), Is.False, "Settling starts a fresh idle delay.");
            yield return new WaitForSecondsRealtime(0.2f);
            yield return null;
            Assert.That(PathPopupVisible(display), Is.True, "Still-hovered paths reappear after scroll idle.");
            content.anchoredPosition += new Vector2(0, 10);
            yield return null;
            yield return null;
            Assert.That(PathPopupVisible(display), Is.False, "Content motion without wheel input also dismisses the tooltip.");
            ExecuteEvents.Execute(display.gameObject, pointer, ExecuteEvents.pointerExitHandler);
        }
    }
}
