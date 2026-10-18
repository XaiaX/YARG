using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Tests.EditMode
{
    public sealed class EliteDrumsLayoutLeftyBuilderTests
    {
        private GameObject _root;

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object InvokeBuilder(string method, params object[] arguments) =>
            FindType("YARG.Editor.EliteDrumsLayoutMenuPrefabBuilder")
                .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, arguments);

        [SetUp]
        public void CreateInactiveMenu()
        {
            _root = new GameObject("Lefty builder test", typeof(RectTransform));
            _root.SetActive(false);
            ((RectTransform)_root.transform).sizeDelta = new Vector2(1920f, 1080f);
        }

        [TearDown]
        public void DestroyMenu() => UnityEngine.Object.DestroyImmediate(_root);

        [Test]
        public void CopiedToggleRetainsBothAuthoredStatesWithoutSidebarCallbacks()
        {
            var menu = _root.AddComponent(FindType("YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu"));
            InvokeBuilder("MountLeftyFlipControl", _root.transform, menu, 0.25f);
            var toggle = _root.transform.Find("Lefty Flip").GetComponentInChildren<Toggle>(true);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab");
            var sidebar = source.GetComponentInChildren(FindType("YARG.Menu.ProfileList.ProfileSidebar"), true);
            var sourceToggle = (Toggle)new SerializedObject(sidebar)
                .FindProperty("_leftyFlipToggle").objectReferenceValue;

            Assert.That(toggle.graphic, Is.Not.Null, "Sidebar decoration cleanup must retain the on-state overlay.");
            Assert.That(toggle.graphic.transform.parent, Is.SameAs(toggle.transform));
            Assert.That(((Image)toggle.graphic).sprite, Is.EqualTo(((Image)sourceToggle.graphic).sprite));
            Assert.That(((Image)toggle.targetGraphic).sprite, Is.EqualTo(((Image)sourceToggle.targetGraphic).sprite));
            Assert.That(toggle.graphic.rectTransform.rect.size, Is.EqualTo(((RectTransform)toggle.transform).rect.size));
            Assert.That(toggle.onValueChanged.GetPersistentEventCount(), Is.Zero);
            Assert.That(sourceToggle.onValueChanged.GetPersistentMethodName(0), Is.EqualTo("ChangeLeftyFlip"));
            Assert.That(toggle.GetComponentsInChildren<Animator>(true), Is.Empty,
                "The authored toggle uses Toggle's graphic transition, not a sidebar animation controller.");

            toggle.SetIsOnWithoutNotify(true);
            Assert.That(toggle.graphic.canvasRenderer.GetAlpha(), Is.EqualTo(1f).Within(0.001f));
            toggle.SetIsOnWithoutNotify(false);
            Assert.That(toggle.graphic.canvasRenderer.GetAlpha(), Is.EqualTo(0f).Within(0.001f));
            Assert.That(toggle.targetGraphic.canvasRenderer.GetAlpha(), Is.EqualTo(1f).Within(0.001f));

            var focus = toggle.transform.Find("Lefty Flip Focus").GetComponent<Image>();
            Assert.That(focus.sprite, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Menu/Common/Buttons/ButtonOutline.png")));
            Assert.That(focus.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(focus.fillCenter, Is.True);
            Assert.That(focus.raycastTarget, Is.False);
            Assert.That(focus.gameObject.activeSelf, Is.False);
            Assert.That(focus.rectTransform.offsetMin, Is.EqualTo(new Vector2(-4f, -4f)));
            Assert.That(focus.rectTransform.offsetMax, Is.EqualTo(new Vector2(4f, 4f)));
            Assert.That(focus.rectTransform.rect.width, Is.EqualTo(((RectTransform)toggle.transform).rect.width + 8f));
            Assert.That(new SerializedObject(menu).FindProperty("_leftyFlipToggle").objectReferenceValue,
                Is.SameAs(toggle));
            var divider = _root.transform.Find("Lefty Flip Divider").GetComponent<RectTransform>();
            Assert.That(divider.anchorMin.y, Is.EqualTo(0.25f));
            Assert.That(divider.anchorMax.y, Is.EqualTo(0.25f));
            Assert.That(divider.sizeDelta, Is.EqualTo(new Vector2(2f, 56f)));
        }

        [TestCase("Layout Colors Separator", true, true)]
        [TestCase("Lefty Flip Divider", true, true)]
        [TestCase("Assignment Preview Divider", true, false)]
        [TestCase("Toolbar Bottom Border", false, false)]
        [TestCase("Management Bottom Border", false, false)]
        public void BoundariesUseTwoPixelNonRaycastStroke(string name, bool vertical, bool compact)
        {
            var anchorMin = new Vector2(0.5f, 0.5f);
            var anchorMax = vertical ? anchorMin : new Vector2(1f, 0.5f);
            var min = vertical ? new Vector2(-0.75f, -40f) : Vector2.zero;
            var max = vertical ? new Vector2(0.75f, 40f) : new Vector2(0f, 1.5f);
            InvokeBuilder("AddBoundary", name, _root.transform, anchorMin, anchorMax, min, max);
            var boundary = _root.transform.Find(name).GetComponent<Image>();
            Assert.That(boundary.raycastTarget, Is.False);
            Assert.That(vertical ? boundary.rectTransform.sizeDelta.x : boundary.rectTransform.sizeDelta.y,
                Is.EqualTo(2f));
            if (compact)
            {
                Assert.That(boundary.rectTransform.sizeDelta.y, Is.EqualTo(56f));
                Assert.That(boundary.rectTransform.offsetMin.y, Is.EqualTo(-28f));
                Assert.That(boundary.rectTransform.offsetMax.y, Is.EqualTo(28f));
            }
            else if (vertical)
                Assert.That(boundary.rectTransform.sizeDelta.y, Is.EqualTo(80f),
                    "The full-height assignment divider retains its caller-supplied span.");
        }
    }
}
