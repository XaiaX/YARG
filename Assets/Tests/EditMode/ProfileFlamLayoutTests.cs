// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class ProfileFlamLayoutTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string PREFAB = "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab";

        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static object Get(object value, string name) => value.GetType().GetProperty(name, INSTANCE).GetValue(value);

        [TestCase(GameMode.EliteDrums, true)]
        [TestCase(GameMode.FourLaneDrums, true)]
        [TestCase(GameMode.FiveLaneDrums, true)]
        [TestCase(GameMode.FiveFretGuitar, false)]
        [TestCase(GameMode.Vocals, false)]
        [TestCase(GameMode.ProKeys, false)]
        public void FlamLayoutProfileRowIsAvailableForEveryDrumProfileOnly(GameMode mode, bool expected)
        {
            var entries = (IEnumerable)Production("YARG.Helpers.Extensions.GameModeExtensions")
                .GetMethod("PossibleProfileSettings").Invoke(null, new object[] { mode, new Dictionary<string, object>() });
            var names = entries.Cast<object>().Select(item => (string)item.GetType().GetField("Item1").GetValue(item));
            Assert.That(names.Contains("Flam Layout"), Is.EqualTo(expected));
        }

        [Test]
        public void AuthoredDropdownFollowsStarPowerAndInvokesOnlyItsOwnProfileCallback()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB);
            var sidebar = prefab.GetComponentInChildren(Production("YARG.Menu.ProfileList.ProfileSidebar"), true);
            var field = sidebar.GetType().GetField("_flamLayoutDropdown", INSTANCE);
            Assert.That(field, Is.Not.Null, "Profiles need an authored flam-layout dropdown.");
            var dropdown = (Component)field.GetValue(sidebar);
            Assert.That(dropdown, Is.Not.Null);
            var content = (GameObject)sidebar.GetType().GetField("_sidebarContent", INSTANCE).GetValue(sidebar);
            var row = dropdown.transform;
            while (row.parent != content.transform) row = row.parent;
            Assert.That(row.name, Is.EqualTo("Flam Layout"));
            Assert.That(row.GetSiblingIndex(), Is.EqualTo(content.transform.Find("Star Power Activation Type").GetSiblingIndex() + 1));
            var callback = (UnityEventBase)Get(dropdown, "onValueChanged");
            Assert.That(callback.GetPersistentEventCount(), Is.EqualTo(1), "The cloned star-power callback must be replaced.");
            Assert.That(callback.GetPersistentTarget(0), Is.SameAs(sidebar));
            Assert.That(callback.GetPersistentMethodName(0), Is.EqualTo("ChangeFlamLayout"));
        }

        [Test]
        public void SwitchingProfilesRefreshesTheChoiceWithoutMutatingEitherProfileOrFiringCallbacks()
        {
            var source = PrefabUtility.LoadPrefabContents(PREFAB);
            GameObject host;
            try { source.SetActive(false); host = UnityEngine.Object.Instantiate(source); }
            finally { PrefabUtility.UnloadPrefabContents(source); }
            try
            {
                var sidebar = host.GetComponentInChildren(Production("YARG.Menu.ProfileList.ProfileSidebar"), true);
                var dropdownField = sidebar.GetType().GetField("_flamLayoutDropdown", INSTANCE);
                Assert.That(dropdownField, Is.Not.Null, "Profiles need their own flam-layout selection.");
                var dropdown = (Component)dropdownField.GetValue(sidebar);
                Assert.That(dropdown, Is.Not.Null);
                var refresh = sidebar.GetType().GetMethod("RefreshFlamLayoutDropdown", INSTANCE);
                Assert.That(refresh, Is.Not.Null);
                var field = typeof(YargProfile).GetProperty("FlamLayout");
                Assert.That(field, Is.Not.Null);
                var parallel = Enum.Parse(field.PropertyType, "Parallel");
                var bucket = Enum.Parse(field.PropertyType, "Bucket");
                var first = new YargProfile { GameMode = GameMode.FourLaneDrums };
                var second = new YargProfile { GameMode = GameMode.EliteDrums };
                field.SetValue(first, parallel);
                field.SetValue(second, bucket);
                int notifications = 0;
                ((UnityEvent<int>)Get(dropdown, "onValueChanged")).AddListener(_ => notifications++);

                foreach (var profile in new[] { first, second, first, second })
                {
                    sidebar.GetType().GetField("_profile", INSTANCE).SetValue(sidebar, profile);
                    refresh.Invoke(sidebar, null);
                    Assert.That(Get(dropdown, "value"), Is.EqualTo(Convert.ToInt32(field.GetValue(profile))));
                    Assert.That(((IList)Get(dropdown, "options")).Count, Is.EqualTo(3), "Refreshing cannot duplicate options.");
                }
                Assert.That(notifications, Is.Zero);
                Assert.That(field.GetValue(first), Is.EqualTo(parallel));
                Assert.That(field.GetValue(second), Is.EqualTo(bucket));
                Assert.That(first.StarPowerActivationType, Is.EqualTo(StarPowerActivationType.RightmostNote));
                Assert.That(second.StarPowerActivationType, Is.EqualTo(StarPowerActivationType.RightmostNote));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
