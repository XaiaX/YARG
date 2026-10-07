using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Input;
using Object = UnityEngine.Object;

namespace YARG.Tests.PlayMode
{
    /// <summary>Active production frets driven through the native player's strike routing.
    /// Does not initialize the song scene, audio, devices, or the complete player lifecycle.</summary>
    public sealed class MidiDrumRoutingPlayModeTests
    {
        private readonly List<Object> _owned = new();
        private const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var value in _owned) if (value != null) Object.Destroy(value);
            _owned.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator EliteBeginnerVisualFretScenario()
        {
            // The player remains inactive because its lifecycle requires the complete song manager.
            // Its production routing still drives active real frets and their real theme animators.
            var playerObject = Own(new GameObject("Isolated native strike routing"));
            playerObject.SetActive(false);
            var player = playerObject.AddComponent(Production("YARG.Gameplay.Player.EliteDrumsPlayer"));
            var profile = new YargProfile { GameMode = YARG.Core.GameMode.EliteDrums,
                CurrentInstrument = YARG.Core.Instrument.EliteDrums,
                CurrentDifficulty = YARG.Core.Difficulty.Beginner, UseCymbalModels = true };
            var bindings = Activator.CreateInstance(Production("YARG.Input.ProfileBindings"), profile);
            var session = Activator.CreateInstance(Production("YARG.Player.YargPlayer"), profile, bindings);
            Production("YARG.Gameplay.Player.BasePlayer").GetField("<Player>k__BackingField", FLAGS)
                .SetValue(player, session);
            var fretObject = Own(new GameObject("Active native Elite frets"));
            var array = fretObject.AddComponent(Production("YARG.Gameplay.Visuals.FretArray"));
            player.GetType().GetField("_fretArray", FLAGS).SetValue(player, array);
            var prefab = Own(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/Themes/CircularFret.prefab")));
            prefab.SetActive(false);
            var fret = prefab.AddComponent(Production("YARG.Gameplay.Visuals.Fret"));
            fret.GetType().GetProperty("ThemeBind").SetValue(fret,
                prefab.GetComponent(Production("YARG.Themes.ThemeFret")));
            var infoType = Production("YARG.Gameplay.Visuals.HighwayOrderingInfo");
            var ordering = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>)
                .MakeGenericType(typeof(int), infoType));
            var playerOrdering = (IDictionary)player.GetType().GetField("_ordering", FLAGS).GetValue(player);
            var getLane = player.GetType().GetMethod("GetLane");
            var getColor = player.GetType().GetMethod("GetColorIndex");
            for (int pad = 1; pad <= (int)EliteDrumNote.EliteDrumPad.RightCrash; pad++)
            {
                var info = Activator.CreateInstance(infoType, getLane.Invoke(null, new object[] { pad }),
                    getColor.Invoke(null, new object[] { pad }));
                ordering.Add(pad, info);
                playerOrdering.Add(pad, info);
            }
            array.GetType().GetMethods().Single(method => method.Name == "Initialize" &&
                method.GetParameters().Length == 6 && method.GetParameters()[4].ParameterType == typeof(GameObject))
                .Invoke(array, new object[] { ordering, 5, null, ColorProfile.Default.EliteDrums, prefab, false });
            yield return null;
            Assert.That(fretObject.activeInHierarchy, Is.True);
            Assert.That(fretObject.GetComponentsInChildren(Production("YARG.Gameplay.Visuals.Fret")).Length,
                Is.EqualTo(5));
            var route = player.GetType().GetMethod("AnimateUnmatchedAction", FLAGS);
            foreach (var action in new[] { EliteDrumsAction.EliteSnare, EliteDrumsAction.EliteTom1,
                EliteDrumsAction.EliteRightCrash, EliteDrumsAction.FourLaneBlueDrum,
                EliteDrumsAction.FiveLaneOrangeCymbal, EliteDrumsAction.WildcardPad })
            {
                Assert.DoesNotThrow(() => route.Invoke(player, new object[] { action }), action.ToString());
                yield return null;
            }
            Assert.That((int)EliteDrumsAction.WildcardPad, Is.EqualTo(13));
            Assert.That((int)EliteDrumNote.EliteDrumPad.Wildcard, Is.EqualTo(10));
            // Returning to neutral must also remain safe after every routing family.
            Assert.DoesNotThrow(() => array.GetType().GetMethod("ResetAll").Invoke(array, null));
            session.GetType().GetMethod("Dispose").Invoke(session, null);
        }
    }
}
