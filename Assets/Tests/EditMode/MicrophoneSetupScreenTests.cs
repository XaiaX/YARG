// pattern: Imperative Shell
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class MicrophoneSetupScreenTests
    {
        private const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null);

        [TestCase(GameMode.Vocals, false, true)]
        [TestCase(GameMode.PartyVocals, false, true)]
        [TestCase(GameMode.Vocals, true, false)]
        [TestCase(GameMode.PartyVocals, true, false)]
        [TestCase(GameMode.EliteDrums, false, false)]
        public void SetupAvailabilityFollowsVocalModesAndExcludesBots(GameMode mode, bool bot, bool expected)
        {
            var type = Production("YARG.Menu.ProfileList.MicrophoneSetup");
            Assert.That(type, Is.Not.Null, "The microphone setup component must exist independently of the upstream Party Vocals model.");
            var profile = new YargProfile { GameMode = mode, IsBot = bot };
            var method = type.GetMethod("CanConfigure", BindingFlags.Static | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { profile }), Is.EqualTo(expected));
        }

        [TestCase(GameMode.Vocals)]
        [TestCase(GameMode.PartyVocals)]
        public void SetupEntryIsHiddenForDisconnectedVocalProfiles(GameMode mode)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab");
            var host = new GameObject("microphone profile entry fixture");
            host.SetActive(false);
            var instance = UnityEngine.Object.Instantiate(prefab, host.transform);
            try
            {
                var sidebarType = Production("YARG.Menu.ProfileList.ProfileSidebar");
                var sidebar = instance.GetComponentInChildren(sidebarType, true);
                var profile = new YargProfile { Name = "Unjoined singer", GameMode = mode };
                sidebarType.GetMethod("UpdateSidebar").Invoke(sidebar, new object[] { profile, null });
                var row = (Component)sidebarType.GetField("_microphoneSetupRow", FLAGS).GetValue(sidebar);
                Assert.That(row, Is.Not.Null);
                Assert.That(row.gameObject.activeSelf, Is.False, "Disconnected profiles must hide configuration controls.");
                Assert.That(row.GetComponentInChildren<Button>(true).interactable, Is.False);
                Assert.DoesNotThrow(() => sidebarType.GetMethod("OpenMicrophoneSetup").Invoke(sidebar, null),
                    "A stale configuration action must not open an unjoined profile.");
                var players = Production("YARG.Player.PlayerContainer");
                Assert.That(players.GetMethod("IsProfileTaken").Invoke(null, new object[] { profile }), Is.False,
                    "Configuring a microphone must not silently join its profile.");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
