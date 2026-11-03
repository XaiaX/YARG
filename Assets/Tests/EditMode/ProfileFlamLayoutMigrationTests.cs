// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class ProfileFlamLayoutMigrationTests
    {
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        private static YargProfile LegacyProfile()
        {
            var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
            var read = json.GetMethods(STATIC).Single(method => method.Name == "DeserializeObject"
                && method.IsGenericMethodDefinition && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(string));
            return (YargProfile)read.MakeGenericMethod(typeof(YargProfile)).Invoke(null,
                new object[] { "{\"Name\":\"old drum profile\",\"GameMode\":5}" });
        }

        private static int Apply(IReadOnlyList<YargProfile> profiles, DrumFlamLayout layout, Action persist)
        {
            var type = Type.GetType("YARG.Player.ProfileFlamLayoutMigration, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, "Loaded profiles need a migration pass after global settings become available.");
            return (int)type.GetMethod("Apply", STATIC).Invoke(null, new object[] { profiles, layout, persist });
        }

        [Test]
        public void OldGlobalSeedsOnlyPendingProfilesAndPersistsTheBatchOnce()
        {
            var first = LegacyProfile();
            var second = LegacyProfile();
            var explicitChoice = new YargProfile { FlamLayout = DrumFlamLayout.Bucket };
            var fresh = new YargProfile();
            var profiles = new[] { first, second, explicitChoice, fresh };
            int saves = 0;
            Assert.That(Apply(profiles, DrumFlamLayout.Parallel, () => saves++), Is.EqualTo(2));
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(first.FlamLayout, Is.EqualTo(DrumFlamLayout.Parallel));
            Assert.That(second.FlamLayout, Is.EqualTo(DrumFlamLayout.Parallel));
            Assert.That(explicitChoice.FlamLayout, Is.EqualTo(DrumFlamLayout.Bucket));
            Assert.That(fresh.FlamLayout, Is.EqualTo(DrumFlamLayout.V));
            first.FlamLayout = DrumFlamLayout.V;
            Assert.That(Apply(profiles, DrumFlamLayout.Bucket, () => saves++), Is.Zero);
            Assert.That(first.FlamLayout, Is.EqualTo(DrumFlamLayout.V));
            Assert.That(second.FlamLayout, Is.EqualTo(DrumFlamLayout.Parallel));
            Assert.That(saves, Is.EqualTo(1), "A later pass must not save or override individual preferences.");
        }

        [Test]
        public void ExperimentalMenuNoLongerExposesTheLegacyGlobalControl()
        {
            var manager = Production("YARG.Settings.SettingsManager");
            var tabs = ((System.Collections.IEnumerable)manager.GetField("AllSettingsTabs", STATIC)
                .GetValue(null)).Cast<object>().ToArray();
            var fields = tabs.Where(tab => tab.GetType().GetProperty("Settings") != null)
                .SelectMany(tab => ((System.Collections.IEnumerable)tab.GetType().GetProperty("Settings")
                    .GetValue(tab)).Cast<object>())
                .Select(item => (string)item.GetType().GetProperty("FieldName")?.GetValue(item)).ToArray();
            Assert.That(fields, Does.Contain("DataStreamEnable"), "Inspect the populated Experimental settings metadata.");
            Assert.That(fields, Does.Not.Contain("EliteFlamLayout"));
        }
    }
}
