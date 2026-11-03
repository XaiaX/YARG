// pattern: Imperative Shell
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class ProfileDiagnosticsEligibilityTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [TestCase(GameMode.EliteDrums, false, true, true)]
        [TestCase(GameMode.FourLaneDrums, false, true, true)]
        [TestCase(GameMode.FiveLaneDrums, false, true, true)]
        [TestCase(GameMode.EliteDrums, true, true, false)]
        [TestCase(GameMode.FourLaneDrums, true, true, false)]
        [TestCase(GameMode.FiveLaneDrums, true, true, false)]
        [TestCase(GameMode.EliteDrums, false, false, false)]
        [TestCase(GameMode.FourLaneDrums, false, false, false)]
        [TestCase(GameMode.FiveLaneDrums, false, false, false)]
        [TestCase(GameMode.Vocals, false, true, false)]
        [TestCase(GameMode.FiveFretGuitar, false, true, false)]
        public void MidiTestRequiresAJoinedHumanDrumProfile(GameMode mode, bool bot, bool joined, bool expected)
        {
            var host = new GameObject("Profile diagnostic eligibility fixture");
            host.SetActive(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab");
            var instance = UnityEngine.Object.Instantiate(prefab, host.transform);
            var profile = new YargProfile { GameMode = mode, IsBot = bot };
            var container = Production("YARG.Player.PlayerContainer");
            var taken = (IDictionary)container.GetField("_playersByProfile", STATIC).GetValue(null);
            try
            {
                if (joined)
                {
                    var playerType = Production("YARG.Player.YargPlayer");
                    var bindings = Production("YARG.Input.ProfileBindings");
                    taken.Add(profile, playerType.GetConstructor(new[] { typeof(YargProfile), bindings })
                        .Invoke(new object[] { profile, null }));
                }
                var type = Production("YARG.Menu.ProfileList.ProfileSidebar");
                var sidebar = instance.GetComponentInChildren(type, true);
                type.GetField("_profile", INSTANCE).SetValue(sidebar, profile);
                type.GetMethod("EnableSettingsForGameMode", INSTANCE).Invoke(sidebar, null);
                var button = (Button)type.GetField("_midiTestButton", INSTANCE).GetValue(sidebar);
                Assert.That(button.transform.parent.gameObject.activeSelf, Is.EqualTo(expected));
                if (!expected)
                {
                    Assert.That(button.interactable, Is.False);
                    Assert.DoesNotThrow(() => type.GetMethod("OpenMidiTestMenu").Invoke(sidebar, null),
                        "A hidden or stale diagnostic action must not open another profile's input screen.");
                }
            }
            finally
            {
                taken.Remove(profile);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
