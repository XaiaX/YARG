// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core.Diagnostics;
using YARG.Core.Engine;

namespace YARG.Tests.PlayMode
{
    public sealed class UnisonActivationPlayModeTests
    {
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [UnityTest]
        public IEnumerator FiveDeferredIconsReuseTheirDisplayContext() => Exercise(true);

        [UnityTest]
        public IEnumerator StandaloneIconsKeepTheirSceneLookupFallback() => Exercise(false);

        [UnityTest]
        public IEnumerator DeferredContainersReuseTheirDisplayContext() => Exercise(true, true);

        [UnityTest]
        public IEnumerator StandaloneContainersKeepTheirSceneLookupFallback() => Exercise(false, true);

        private static IEnumerator Exercise(bool hasDisplay, bool includeContainers = false)
        {
            var managerObject = new GameObject("Unison test manager");
            managerObject.SetActive(false);
            var owner = new GameObject("Unison owner");
            owner.SetActive(false);
            try
            {
                var managerType = Production("YARG.Gameplay.GameManager");
                var manager = (Behaviour)managerObject.AddComponent(managerType);
                managerType.GetMethod("InitializeRuntime").Invoke(manager, new object[]
                {
                    null, null, null, Array.CreateInstance(Production("YARG.Player.YargPlayer"), 0),
                    new EngineManager(), null, null, false, false
                });
                manager.enabled = false;
                managerObject.SetActive(true);
                if (hasDisplay) owner.AddComponent(Production("YARG.Gameplay.HUD.UnisonDisplay"));
                owner.SetActive(true);

                // Match the HUD: prefab instances exist under a hidden parent before
                // their first visible phrase, so Awake is deferred until activation.
                var hidden = new GameObject("Hidden icons");
                hidden.transform.SetParent(owner.transform);
                hidden.SetActive(false);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Gameplay/HUD/UnisonBonus/Unison Icon.prefab");
                Assert.That(prefab.activeSelf, Is.False);
                var icons = new List<Component>();
                var iconType = Production("YARG.Gameplay.HUD.UnisonIcon");
                for (int i = 0; i < 5; i++)
                {
                    var clone = UnityEngine.Object.Instantiate(prefab, hidden.transform);
                    clone.SetActive(true);
                    icons.Add(clone.GetComponent(iconType));
                }
                var containers = new List<Component>();
                if (includeContainers)
                {
                    var group = new GameObject("Icon group");
                    group.transform.SetParent(hidden.transform);
                    containers.Add(group.AddComponent(Production("YARG.Gameplay.HUD.UnisonIconGroup")));
                    var barPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefabs/Gameplay/HUD/UnisonBonus/Unison Bar.prefab");
                    var bar = UnityEngine.Object.Instantiate(barPrefab, hidden.transform);
                    bar.SetActive(true);
                    containers.Add(bar.GetComponent(Production("YARG.Gameplay.HUD.UnisonBar")));
                    foreach (var container in containers)
                        container.GetType().GetMethod("Initialize").Invoke(container, new object[] { 5 });
                }
                int count = 5 + containers.Count;
                TargetedPerformance.SetEnabled(true);
                TargetedPerformance.TakeSnapshot();
                hidden.SetActive(true);
                var first = TargetedPerformance.TakeSnapshot();
                Assert.That(first.GameplayAwake.Calls, Is.EqualTo(count));
                Assert.That(first.GameplayManagerDiscovery.Calls, Is.EqualTo(hasDisplay ? 0 : count));
                var context = Production("YARG.Gameplay.GameplayBehaviour").GetProperty("GameplayContext", INSTANCE);
                foreach (var icon in icons)
                {
                    Assert.That(context.GetValue(icon), Is.SameAs(manager));
                    Assert.That(iconType.GetField("_seekTween", INSTANCE).GetValue(icon), Is.Not.Null);
                    Assert.That(iconType.GetField("_completeSequence", INSTANCE).GetValue(icon), Is.Not.Null);
                    iconType.GetMethod("SetProgress").Invoke(icon, new object[] { .5f });
                }
                foreach (var container in containers)
                {
                    Assert.That(context.GetValue(container), Is.SameAs(manager));
                    if (container.GetType().Name == "UnisonIconGroup")
                        ((IDictionary)container.GetType().GetField("_icons", INSTANCE).GetValue(container))
                            .Add(0, icons[0]);
                    container.GetType().GetMethod("AddParticipant").Invoke(container, new object[] { 0, 4 });
                    container.GetType().GetMethod("SetNotesHit").Invoke(container, new object[] { 0, 2 });
                    container.GetType().GetMethod("ResetState").Invoke(container, null);
                }
                hidden.SetActive(false);
                hidden.SetActive(true);
                var reused = TargetedPerformance.TakeSnapshot();
                Assert.That(reused.GameplayAwake.Calls, Is.Zero);
                Assert.That(reused.GameplayManagerDiscovery.Calls, Is.Zero);
            }
            finally
            {
                TargetedPerformance.SetEnabled(false);
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
            yield return null;
        }
    }
}
