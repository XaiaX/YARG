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
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteDrumsLayoutMenuPlayModeTests
    {
        [UnityTest]
        public IEnumerator LayoutAndColorDropdownsNavigateDisplayedOrderAfterPointerHover()
        {
#if UNITY_EDITOR
            var manager = Production("YARG.Settings.Customization.CustomContentManager");
            var layouts = manager.GetField("EliteDrumsLayouts").GetValue(null);
            var colors = manager.GetField("ColorProfiles").GetValue(null);
            var layoutContents = (IList)layouts.GetType().GetField("_content",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(layouts);
            var colorContents = (IList)colors.GetType().BaseType.GetField("Content",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(colors);
            var previousLayouts = layoutContents.Cast<object>().ToArray();
            var previousColors = colorContents.Cast<object>().ToArray();
            try
            {
                layoutContents.Clear();
                colorContents.Clear();
                foreach (string name in new[] { "Zulu navigation fixture", "Alpha navigation fixture", "Mike navigation fixture" })
                {
                    layoutContents.Add(EliteDrumsLayoutPreset.Compact.CopyWithNewName(name));
                    var defaults = (IEnumerable)colors.GetType().GetProperty("DefaultPresets").GetValue(colors);
                    var source = defaults.Cast<object>().First();
                    colorContents.Add(source.GetType().GetMethod("CopyWithNewName").Invoke(source, new object[] { name }));
                }
            MountMenuServices();
            _menuCanvasHost = new GameObject("Dropdown navigation test canvas", typeof(Canvas));
            _menuCanvasHost.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var events = new GameObject("Dropdown navigation test EventSystem", typeof(EventSystem));
            events.transform.SetParent(_menuCanvasHost.transform, false);
            var eventSystem = events.GetComponent<EventSystem>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/EliteDrumsLayoutMenu.prefab");
            Assert.That(prefab, Is.Not.Null);
            _menuHost = UnityEngine.Object.Instantiate(prefab, _menuCanvasHost.transform, false);
            _menuHost.SetActive(true);
            var menuType = Production("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu");
            var menu = _menuHost.GetComponent(menuType);
            menuType.GetMethod("Initialize", new[]
            {
                typeof(EliteDrumsLayoutPreset), typeof(Action<EliteDrumsLayoutPreset>), typeof(Action)
            }).Invoke(menu, new object[]
            {
                EliteDrumsLayoutPreset.Compact.Copy(),
                (Action<EliteDrumsLayoutPreset>)(_ => { }), (Action)(() => { })
            });
            yield return null;

            var navigatorType = Production("YARG.Menu.Navigation.Navigator");
            var navigator = navigatorType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static |
                BindingFlags.FlattenHierarchy).GetValue(null);
            var stack = (ICollection)Field(navigator, "_schemeStack");
            var open = Production("YARG.Menu.Navigation.RuntimeNavigatable").GetMethod("OpenDropdownList");
            // Exercise the same shared preset population used by PresetDropdown.Initialize,
            // without introducing a SettingsMenu singleton or changing remembered preset selection.
            var sharedDropdown = UnityEngine.Object.Instantiate((Component)Field(menu, "_previewColor"),
                _menuCanvasHost.transform, false);
            sharedDropdown.GetType().GetProperty("onValueChanged").GetValue(sharedDropdown).GetType()
                .GetMethod("RemoveAllListeners").Invoke(sharedDropdown.GetType().GetProperty("onValueChanged")
                    .GetValue(sharedDropdown), null);
            colors.GetType().GetMethod("AddOptionsToDropdown").Invoke(colors, new object[] { sharedDropdown });
            sharedDropdown.GetType().GetMethod("SetValueWithoutNotify").Invoke(sharedDropdown, new object[] { 0 });
            foreach (var field in new[] { "_layoutDropdown", "_previewColor", "shared preset dropdown" })
            {
                var dropdown = field == "shared preset dropdown" ? sharedDropdown : (Component)Field(menu, field);
                var type = dropdown.GetType();
                int originalValue = (int)type.GetProperty("value").GetValue(dropdown);
                int baseline = stack.Count;
                open.Invoke(null, new object[] { dropdown });
                yield return null;
                var list = dropdown.transform.Find("Dropdown List");
                Assert.That(list, Is.Not.Null, field);
                var toggles = list.GetComponentsInChildren<Toggle>();
                var options = (IList)type.GetProperty("options").GetValue(dropdown);
                Assert.That(toggles.Length, Is.EqualTo(options.Count), field);
                Assert.That(toggles.Length, Is.GreaterThan(2), field + " must include the seeded custom presets.");
                Canvas.ForceUpdateCanvases();
                var renderedOrder = toggles.OrderByDescending(toggle =>
                {
                    var rect = (RectTransform)toggle.transform;
                    return rect.TransformPoint(rect.rect.center).y;
                }).ToArray();
                Assert.That(renderedOrder, Is.EqualTo(toggles),
                    field + " keyboard traversal must match rendered top-to-bottom order, not a global name sort.");
                for (int i = 0; i < toggles.Length; i++)
                {
                    var text = toggles[i].GetComponentInChildren(Production("TMPro.TMP_Text"));
                    Assert.That(text.GetType().GetProperty("text").GetValue(text),
                        Is.EqualTo(options[i].GetType().GetProperty("text").GetValue(options[i])),
                        field + " navigation must retain displayed option order, including rich-text labels.");
                }
                var labels = options.Cast<object>().Select(option =>
                    (string)option.GetType().GetProperty("text").GetValue(option)).ToArray();
                foreach (string name in new[] { "Zulu navigation fixture", "Alpha navigation fixture", "Mike navigation fixture" })
                    Assert.That(labels, Does.Contain(name), field + " must use the isolated seeded collection.");
                Assert.That(stack.Count, Is.EqualTo(baseline + 1));
                var scheme = stack.Cast<object>().First();
                void Press(string action)
                {
                    var entries = (IEnumerable)scheme.GetType().GetProperty("Entries").GetValue(scheme);
                    var entry = entries.Cast<object>().Single(candidate =>
                        candidate.GetType().GetField("Action").GetValue(candidate).ToString() == action);
                    entry.GetType().GetMethod("Invoke", Type.EmptyTypes).Invoke(entry, null);
                }
                void AssertFocus(int index) => Assert.That(eventSystem.currentSelectedGameObject,
                    Is.SameAs(toggles[index].gameObject), field + " highlighted option index " + index);

                AssertFocus(originalValue);
                // Re-attaching an already-open list must not reset focus or push another scheme.
                Press("Down");
                int next = (originalValue + 1) % toggles.Length;
                open.Invoke(null, new object[] { dropdown });
                Assert.That(stack.Count, Is.EqualTo(baseline + 1));
                AssertFocus(next);
                for (int i = 0; i < toggles.Length; i++)
                {
                    Press("Down");
                    next = (next + 1) % toggles.Length;
                    AssertFocus(next);
                }
                Press("Up");
                AssertFocus((next - 1 + toggles.Length) % toggles.Length);

                // TMP hover changes focus but not dropdown.value. Keyboard must continue there.
                ExecuteEvents.Execute(toggles[1].gameObject, new PointerEventData(eventSystem),
                    ExecuteEvents.pointerEnterHandler);
                AssertFocus(1);
                Press("Down");
                AssertFocus(2);
                ExecuteEvents.Execute(toggles[1].gameObject, new PointerEventData(eventSystem),
                    ExecuteEvents.pointerEnterHandler);
                Press("Up");
                AssertFocus(0);
                Assert.That(type.GetProperty("value").GetValue(dropdown), Is.EqualTo(originalValue),
                    "Moving focus must not commit an option.");
                int chosen = (originalValue + 1) % toggles.Length;
                ExecuteEvents.Execute(toggles[chosen].gameObject, new PointerEventData(eventSystem),
                    ExecuteEvents.pointerEnterHandler);
                Press("Green");
                Assert.That(type.GetProperty("value").GetValue(dropdown), Is.EqualTo(chosen),
                    "Confirm must commit the visibly highlighted option, not the stale cursor.");
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(stack.Count, Is.EqualTo(baseline), "Closing the list must remove its scheme.");

                open.Invoke(null, new object[] { dropdown });
                yield return null;
                scheme = stack.Cast<object>().First();
                Press("Down");
                Press("Red");
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(type.GetProperty("value").GetValue(dropdown), Is.EqualTo(chosen));
                Assert.That(stack.Count, Is.EqualTo(baseline));
            }
            }
            finally
            {
                layoutContents.Clear();
                foreach (var preset in previousLayouts) layoutContents.Add(preset);
                colorContents.Clear();
                foreach (var preset in previousColors) colorContents.Add(preset);
            }
#else
            Assert.Ignore("Requires production dropdown assets from the Unity Editor asset database.");
            yield break;
#endif
        }
    }
}
