using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace YARG.Tests.PlayMode
{
    public sealed partial class EliteDrumsLayoutMenuPlayModeTests
    {
        [UnityTest]
        public IEnumerator AdvancedGraphicsRowsDimAndRestoreWithoutChangingSavedChoices()
        {
            using var state = new SongManagerInteractionState(this);
            var menu = PositionMenu();
            object Setting(string name) => state.Settings.GetType().GetProperty(name).GetValue(state.Settings);
            void Set(string name, bool value) => Setting(name).GetType().GetMethod("SetValueWithoutNotify")
                .Invoke(Setting(name), new object[] { value });
            bool Value(string name) => (bool)Setting(name).GetType().GetProperty("Value").GetValue(Setting(name));
            string[] names = { "FullResolutionTextures", "AnisotropicFiltering", "DetailedModels",
                "FullSkinningQuality", "AmbientOcclusion", "HighQualityShadows", "SoftShadows",
                "AdditionalLightShadows", "HighQualityLighting", "EnhancedReflections" };
            var saved = names.Concat(new[] { "LowQuality", "ShowAdvancedSettings" })
                .ToDictionary(name => name, Value);
            var graphics = ((IEnumerable)Production("YARG.Settings.SettingsManager")
                .GetField("AllSettingsTabs").GetValue(null)).Cast<object>()
                .Single(t => (string)t.GetType().GetProperty("Name").GetValue(t) == "Graphics");
            void Rebuild() => menu.GetType().GetMethod("UpdateSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(menu, new object[] { false });
            Component[] Rows() => state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.Visuals.BaseSettingVisual"));
            string Name(Component row) => (string)row.GetType().GetProperty("UnlocalizedName").GetValue(row);
            try
            {
                menu.GetType().GetProperty("CurrentTab").GetSetMethod(true).Invoke(menu, new[] { graphics });
                Set("ShowAdvancedSettings", false);
                Rebuild();
                yield return null;
                Assert.That(Rows().Any(row => names.Contains(Name(row))), Is.False);

                Set("ShowAdvancedSettings", true);
                Set("LowQuality", true);
                for (int i = 0; i < names.Length; i++) Set(names[i], i % 2 == 0);
                Rebuild();
                yield return null;
                var rows = Rows();
                foreach (var name in names.Concat(new[] { "HighwayAntiAliasing", "VenueAntiAliasing" }))
                {
                    var row = rows.Single(r => Name(r) == name);
                    Assert.That(row.GetType().GetProperty("IsEditable").GetValue(row), Is.False, name);
                    Assert.That(row.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.5f), name);
                    var label = row.GetType().GetProperty("SettingLabel").GetValue(row);
                    Assert.That((string)label.GetType().GetProperty("text").GetValue(label),
                        Does.Not.Contain("Settings."), name);
                }
                Set("LowQuality", false);
                graphics.GetType().GetMethod("OnSettingChanged").Invoke(graphics, null);
                for (int i = 0; i < names.Length; i++)
                {
                    var row = rows.Single(r => Name(r) == names[i]);
                    Assert.That(row.GetType().GetProperty("IsEditable").GetValue(row), Is.True, names[i]);
                    Assert.That(row.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f), names[i]);
                    Assert.That(Value(names[i]), Is.EqualTo(i % 2 == 0), names[i]);
                }
            }
            finally
            {
                foreach (var pair in saved) Set(pair.Key, pair.Value);
            }
        }
    }
}
