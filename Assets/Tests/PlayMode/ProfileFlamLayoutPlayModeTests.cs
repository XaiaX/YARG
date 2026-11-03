// pattern: Imperative Shell
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.PlayMode
{
    public sealed class ProfileFlamLayoutPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator ActualDropdownCallbacksUpdateOnlyTheCurrentlySelectedProfile()
        {
            var source = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab");
            GameObject host;
            try { source.SetActive(false); host = UnityEngine.Object.Instantiate(source); }
            finally { PrefabUtility.UnloadPrefabContents(source); }
            try
            {
                var sidebarType = Type.GetType("YARG.Menu.ProfileList.ProfileSidebar, Assembly-CSharp", true);
                var sidebar = host.GetComponentInChildren(sidebarType, true);
                var dropdownField = sidebarType.GetField("_flamLayoutDropdown", INSTANCE);
                Assert.That(dropdownField, Is.Not.Null, "Every drum profile needs an independent flam dropdown.");
                var dropdown = (Component)dropdownField.GetValue(sidebar);
                Assert.That(dropdown, Is.Not.Null);
                var refresh = sidebarType.GetMethod("RefreshFlamLayoutDropdown", INSTANCE);
                Assert.That(refresh, Is.Not.Null);
                var layout = typeof(YargProfile).GetProperty("FlamLayout");
                Assert.That(layout, Is.Not.Null);
                var first = new YargProfile { GameMode = GameMode.FourLaneDrums };
                var second = new YargProfile { GameMode = GameMode.FiveLaneDrums };
                var selection = dropdown.GetType().GetProperty("value");
                sidebarType.GetField("_profile", INSTANCE).SetValue(sidebar, first);
                refresh.Invoke(sidebar, null);
                selection.SetValue(dropdown, 2);
                Assert.That(layout.GetValue(first).ToString(), Is.EqualTo("Parallel"));
                Assert.That(layout.GetValue(second).ToString(), Is.EqualTo("V"));

                sidebarType.GetField("_profile", INSTANCE).SetValue(sidebar, second);
                refresh.Invoke(sidebar, null);
                selection.SetValue(dropdown, 1);
                Assert.That(layout.GetValue(first).ToString(), Is.EqualTo("Parallel"));
                Assert.That(layout.GetValue(second).ToString(), Is.EqualTo("Bucket"));
                Assert.That(first.StarPowerActivationType, Is.EqualTo(StarPowerActivationType.RightmostNote));
                Assert.That(second.StarPowerActivationType, Is.EqualTo(StarPowerActivationType.RightmostNote));
                yield return null;
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
