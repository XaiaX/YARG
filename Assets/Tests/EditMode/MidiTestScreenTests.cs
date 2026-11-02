// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;

namespace YARG.Tests.EditMode
{
    public sealed class MidiTestScreenTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [Test]
        public void PrefabHasDedicatedHeaderTwoPanelsAndFooterControls()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/MidiTestMenu.prefab");
            Assert.That(prefab, Is.Not.Null, "Dedicated MIDI Test resource");
            Assert.That(prefab.GetComponent(Production("YARG.Menu.MidiTest.MidiTestMenu")), Is.Not.Null);
            foreach (string path in new[] { "Header", "Diagnostics", "History", "Footer" })
                Assert.That(prefab.transform.Find(path), Is.Not.Null, path);
            Assert.That(prefab.transform.Find("Header/Button").GetComponent<Button>(), Is.Not.Null);
            var labels = prefab.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().FullName == "TMPro.TextMeshProUGUI")
                .Select(c => (string)c.GetType().GetProperty("text").GetValue(c)).ToArray();
            Assert.That(labels, Does.Contain("MIDI TEST"));
            Assert.That(labels, Does.Contain("RECORD LOG: OFF"));
            Assert.That(labels, Does.Contain("OPEN LOG FOLDER"));
        }

        [TestCase(GameMode.EliteDrums, true)]
        [TestCase(GameMode.FourLaneDrums, true)]
        [TestCase(GameMode.FiveLaneDrums, true)]
        [TestCase(GameMode.FiveFretGuitar, false)]
        [TestCase(GameMode.Vocals, false)]
        public void MidiTestProfileRowIsAvailableForDrumModesOnly(GameMode mode, bool expected)
        {
            var type = Production("YARG.Helpers.Extensions.GameModeExtensions");
            var method = type.GetMethod("PossibleProfileSettings");
            var entries = (IEnumerable)method.Invoke(null, new object[] {
                mode, new System.Collections.Generic.Dictionary<string, object>() });
            var names = entries.Cast<object>().Select(item =>
                (string)item.GetType().GetField("Item1").GetValue(item));
            Assert.That(names.Contains("MIDI Test"), Is.EqualTo(expected));
        }

        [Test]
        public void ProfileButtonHasNoInheritedConfigureCallbacks()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab");
            var sidebar = prefab.GetComponentInChildren(Production("YARG.Menu.ProfileList.ProfileSidebar"), true);
            var field = sidebar.GetType().GetField("_midiTestButton", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var button = (Button)field.GetValue(sidebar);
            Assert.That(button, Is.Not.Null);
            Assert.That(button.onClick.GetPersistentEventCount(), Is.Zero);
            Assert.That(button.transform.IsChildOf(sidebar.transform), Is.True);
        }

        [Test]
        public void MidiTestProfileRowFollowsAllHighwayConfigurationRows()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab");
            var sidebar = prefab.GetComponentInChildren(Production("YARG.Menu.ProfileList.ProfileSidebar"), true);
            var content = (GameObject)sidebar.GetType().GetField("_sidebarContent",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sidebar);
            var configurations = content.transform.Cast<Transform>()
                .Where(row => row.name.StartsWith("Configure ", StringComparison.Ordinal)).ToArray();
            Assert.That(configurations, Has.Length.EqualTo(4));
            Assert.That(content.transform.Find("MIDI Test").GetSiblingIndex(),
                Is.EqualTo(configurations.Max(row => row.GetSiblingIndex()) + 1));
        }

        [TestCase(1080)]
        [TestCase(823)]
        [TestCase(1440)]
        public void PanelsFollowHeaderWhenTheWindowAspectRatioChanges(int height)
        {
            var parent = new GameObject("MIDI Test size fixture", typeof(RectTransform));
            try
            {
                var parentRect = (RectTransform)parent.transform;
                parentRect.sizeDelta = new Vector2(1920, height);
                var menu = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("MidiTestMenu"), parent.transform);
                var root = (RectTransform)menu.transform;
                root.ForceUpdateRectTransforms();
                var headerCorners = new Vector3[4];
                ((RectTransform)menu.transform.Find("Header")).GetWorldCorners(headerCorners);
                float headerBottom = root.InverseTransformPoint(headerCorners[0]).y;
                foreach (string name in new[] { "Diagnostics", "History" })
                {
                    var corners = new Vector3[4];
                    ((RectTransform)menu.transform.Find(name)).GetWorldCorners(corners);
                    float top = root.InverseTransformPoint(corners[1]).y;
                    Assert.That(top, Is.EqualTo(headerBottom - 12).Within(.1f), name);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); }
        }
    }
}
