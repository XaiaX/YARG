using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core.Input;

namespace YARG.Tests.EditMode
{
    public sealed class NativeEliteContractsEditModeTests
    {
        [Test]
        public void Bindings()
        {
            // EditMode tests do not directly reference Assembly-CSharp.
            var type = Type.GetType("YARG.Input.BindingCollection, Assembly-CSharp");
            Assert.That(type, Is.Not.Null);
            var bindings = (System.Collections.IEnumerable) type.GetMethod("CreateEliteDrumsBindings")
                .Invoke(null, null);
            var actions = bindings.Cast<object>().Select(binding =>
                (int) binding.GetType().GetProperty("Action").GetValue(binding)).ToArray();
            foreach (var action in new[]
            {
                EliteDrumsAction.Kick, EliteDrumsAction.EliteStomp, EliteDrumsAction.EliteSplash,
                EliteDrumsAction.EliteSnare, EliteDrumsAction.EliteClosedHiHat,
                EliteDrumsAction.EliteSizzleHiHat, EliteDrumsAction.EliteOpenHiHat,
                EliteDrumsAction.EliteLeftCrash, EliteDrumsAction.EliteTom1,
                EliteDrumsAction.EliteTom2, EliteDrumsAction.EliteTom3,
                EliteDrumsAction.EliteRide, EliteDrumsAction.EliteRightCrash,
                EliteDrumsAction.FourLaneRedDrum, EliteDrumsAction.FiveLaneRedDrum
            })
            {
                Assert.That(actions, Does.Contain((int) action),
                    $"Missing native or legacy compatibility action {action}");
            }
            Assert.That(actions.Distinct().Count(), Is.EqualTo(actions.Length),
                "Action IDs must not collide.");
        }

        [Test]
        public void MenuFallbackLabel()
        {
            var rules = Type.GetType("YARG.Menu.Maestro.MaestroSelectionRules, Assembly-CSharp");
            Assert.That(rules, Is.Not.Null);
            var method = rules.GetMethod("OrderNativeInstruments");
            Assert.That(method, Is.Not.Null);
            var available = new[] { YARG.Core.Instrument.FiveLaneDrums,
                YARG.Core.Instrument.FourLaneDrums, YARG.Core.Instrument.ProDrums };
            var ordered = (System.Collections.Generic.IReadOnlyList<YARG.Core.Instrument>)
                method.Invoke(null, new object[] { YARG.Core.GameMode.EliteDrums, available });
            Assert.That(ordered[0], Is.EqualTo(YARG.Core.Instrument.ProDrums));
        }

        [Test]
        public void ProfileGameModeSelectsNativeElitePreference()
        {
            var sidebar = Type.GetType("YARG.Menu.ProfileList.ProfileSidebar, Assembly-CSharp");
            Assert.That(sidebar, Is.Not.Null);
            var select = sidebar.GetMethod("ApplyGameModeSelection",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(select, Is.Not.Null);

            var profile = new YARG.Core.Game.YargProfile
            {
                GameMode = YARG.Core.GameMode.FourLaneDrums,
                CurrentInstrument = YARG.Core.Instrument.ProDrums,
                PreferredInstrument = YARG.Core.Instrument.ProDrums,
                EliteDrumsDownchartTarget = YARG.Core.Instrument.FiveLaneDrums,
            };
            select.Invoke(null, new object[] { profile, YARG.Core.GameMode.EliteDrums });
            Assert.That(profile.GameMode, Is.EqualTo(YARG.Core.GameMode.EliteDrums));
            Assert.That(profile.CurrentInstrument, Is.EqualTo(YARG.Core.Instrument.EliteDrums));
            Assert.That(profile.PreferredInstrument, Is.EqualTo(YARG.Core.Instrument.EliteDrums));
            Assert.That(profile.EliteDrumsDownchartTarget, Is.Null);

            select.Invoke(null, new object[] { profile, YARG.Core.GameMode.FourLaneDrums });
            Assert.That(profile.CurrentInstrument, Is.EqualTo(YARG.Core.Instrument.FourLaneDrums));
            Assert.That(profile.PreferredInstrument, Is.EqualTo(profile.CurrentInstrument));
        }

        [Test]
        public void ReplayEntryPoint()
        {
            // Replay dispatch must use the recorded CurrentInstrument and native typed analyzer.
            var type = typeof(YARG.Core.Replays.Analyzer.ReplayAnalyzer);
            Assert.That(type.GetMethod("AnalyzeReplay", System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static), Is.Not.Null);
            Assert.That(Type.GetType("YARG.Gameplay.Player.EliteDrumsPlayer, Assembly-CSharp"), Is.Not.Null);
        }

        [Test]
        public void VisualPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/FiveLaneDrumsVisual.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
        }
    }
}
