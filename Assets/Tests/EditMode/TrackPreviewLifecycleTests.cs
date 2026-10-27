using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class TrackPreviewLifecycleTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        [TestCase("ForceStarPowerNotes")]
        [TestCase("LeftyFlip")]
        public void PreviewOptionsSurviveRefreshBeforePreviewStart(string option)
        {
            var template = new GameObject("Track preview template");
            var container = new GameObject("Track preview world");
            try
            {
                var playerType = Production("YARG.Settings.Preview.FakeTrackPlayer");
                template.AddComponent(playerType);
                var builderType = Production("YARG.Settings.Metadata.TrackPreviewBuilder");
                Func<string, GameObject> load = _ => template;
                var builder = Activator.CreateInstance(builderType, new object[] { false, false, false, load });
                builderType.GetMethod("BuildPreviewWorld").Invoke(builder, new object[] { container.transform });
                var player = container.GetComponentInChildren(playerType);
                Assert.That(player, Is.Not.Null);
                Assert.That(playerType.GetProperty("ColorProfile").GetValue(player), Is.Null,
                    "A newly built preview has not run Start yet, as during consecutive preset refreshes.");

                foreach (bool value in new[] { true, false, true })
                {
                    Assert.DoesNotThrow(() => builderType.GetProperty(option).SetValue(builder, value),
                        "Synchronizing preview options must not abort the preset field rebuild before Start.");
                    Assert.That(playerType.GetProperty(option).GetValue(player), Is.EqualTo(value));
                }
                Assert.That(playerType.GetProperty("ColorProfileOverride").GetValue(player), Is.Null,
                    "Pending options must not force a default color profile over the selected preset.");

                var copied = (ColorProfile)ColorProfile.Default.CopyWithNewName("Copied test colors");
                builderType.GetMethod("RefreshColors").Invoke(builder, new object[] { copied });
                builderType.GetProperty(option).SetValue(builder, false);
                Assert.That(playerType.GetProperty("ColorProfile").GetValue(player), Is.SameAs(copied));
                Assert.That(playerType.GetProperty(option).GetValue(player), Is.False);

                UnityEngine.Object.DestroyImmediate(player.gameObject);
                Assert.DoesNotThrow(() => builderType.GetProperty(option).SetValue(builder, true),
                    "A destroyed preview must not receive an option refresh.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
                UnityEngine.Object.DestroyImmediate(template);
            }
        }
    }
}
