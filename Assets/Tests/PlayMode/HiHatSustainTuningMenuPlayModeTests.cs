// pattern: Imperative Shell
using System;
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
        public IEnumerator ExperimentalBoundaryDotSpacingDropdownLoadsAndPersistsEveryChoice()
        {
            using var state = new SongManagerInteractionState(this);
            const string settingName = "EliteFretBoundaryDotSpacing";
            var manager = Production("YARG.Settings.SettingsManager");
            var experimental = ((IEnumerable)manager.GetField("AllSettingsTabs").GetValue(null)).Cast<object>()
                .Single(tab => (string)tab.GetType().GetProperty("Name").GetValue(tab) == "Experimental");
            var menu = PositionMenu();
            menu.GetType().GetProperty("CurrentTab").GetSetMethod(true).Invoke(menu, new[] { experimental });
            menu.GetType().GetMethod("UpdateSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(menu, new object[] { false });
            yield return null;

            var rows = state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.Visuals.DropdownSettingVisual"));
            var row = rows.SingleOrDefault(candidate =>
                (string)candidate.GetType().GetProperty("UnlocalizedName").GetValue(candidate) == settingName);
            Assert.That(row, Is.Not.Null, "Experimental needs a dot-spacing dropdown.");
            var label = row.GetType().GetProperty("SettingLabel").GetValue(row);
            Assert.That(label.GetType().GetProperty("text").GetValue(label), Is.EqualTo("Fret Boundary Dot Spacing"));
            var dropdown = row.GetType().GetField("_dropdown", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(row);
            var choices = (IList)dropdown.GetType().GetProperty("options").GetValue(dropdown);
            var selection = dropdown.GetType().GetProperty("value");
            Assert.That(choices.Cast<object>().Select(option => option.GetType().GetProperty("text").GetValue(option)),
                Is.EqualTo(new[] { "1/8", "1/16", "1/32", "1/64", "1/128" }));

            var jsonType = Production("Newtonsoft.Json.Linq.JObject");
            var parse = jsonType.GetMethod("Parse", new[] { typeof(string) });
            var deserialize = manager.GetMethod("DeserializeSongSourceSettings");
            var older = deserialize.Invoke(null, new[] { parse.Invoke(null, new object[] { "{}" }) });
            Assert.That(Denominator(older), Is.EqualTo(32));
            var optionsType = Production("Newtonsoft.Json.JsonSerializerSettings");
            var options = manager.GetField("JsonSettings", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var serialize = Production("Newtonsoft.Json.JsonConvert")
                .GetMethod("SerializeObject", new[] { typeof(object), optionsType });
            var setting = state.Settings.GetType().GetProperty(settingName).GetValue(state.Settings);
            var previous = setting.GetType().GetProperty("Value").GetValue(setting);
            Assert.That(selection.GetValue(dropdown), Is.EqualTo(Array.IndexOf(new[] { 8, 16, 32, 64, 128 },
                Convert.ToInt32(previous))));
            try
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    selection.SetValue(dropdown, i);
                    int expected = 8 << i;
                    Assert.That(Denominator(state.Settings), Is.EqualTo(expected));
                    var json = serialize.Invoke(null, new[] { state.Settings, options });
                    var restored = deserialize.Invoke(null, new[] { parse.Invoke(null, new[] { json }) });
                    Assert.That(Denominator(restored), Is.EqualTo(expected));
                }
            }
            finally { setting.GetType().GetMethod("SetValueWithoutNotify").Invoke(setting, new[] { previous }); }

            int Denominator(object container)
            {
                var setting = container.GetType().GetProperty(settingName).GetValue(container);
                return Convert.ToInt32(setting.GetType().GetProperty("Value").GetValue(setting));
            }
        }

        [UnityTest]
        public IEnumerator ExperimentalAlwaysShowBoundaryDotsTogglePersistsAndDefaultsOff() =>
            CheckFretDotToggle("AlwaysShowEliteFretBoundaryDots", "Always Show Fret Boundary Dots");

        [UnityTest]
        public IEnumerator ExperimentalColorHeldFretCentersTogglePersistsAndDefaultsOff() =>
            CheckFretDotToggle("ColorEliteHeldFretCenters", "Use Fret-Colored Center Dots");

        [UnityTest]
        public IEnumerator ExperimentalColorAllFretCentersTogglePersistsAndDefaultsOff() =>
            CheckFretDotToggle("ColorEliteAllFretCenters", "Use Fret-Colored Center Dots on All Frets");

        private IEnumerator CheckFretDotToggle(string name, string expectedLabel)
        {
            using var state = new SongManagerInteractionState(this);
            var manager = Production("YARG.Settings.SettingsManager");
            var experimental = ((IEnumerable)manager.GetField("AllSettingsTabs").GetValue(null)).Cast<object>()
                .Single(tab => (string)tab.GetType().GetProperty("Name").GetValue(tab) == "Experimental");
            var menu = PositionMenu();
            menu.GetType().GetProperty("CurrentTab").GetSetMethod(true).Invoke(menu, new[] { experimental });
            menu.GetType().GetMethod("UpdateSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(menu, new object[] { false });
            yield return null;

            var rows = state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.Visuals.ToggleSettingVisual"));
            var row = rows.SingleOrDefault(candidate =>
                (string)candidate.GetType().GetProperty("UnlocalizedName").GetValue(candidate) == name);
            Assert.That(row, Is.Not.Null, $"Experimental needs the {name} toggle.");
            var label = row.GetType().GetProperty("SettingLabel").GetValue(row);
            Assert.That(label.GetType().GetProperty("text").GetValue(label), Is.EqualTo(expectedLabel));
            var toggle = row.GetType().GetField("_toggle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(row);
            var selection = toggle.GetType().GetProperty("isOn");
            var jsonType = Production("Newtonsoft.Json.Linq.JObject");
            var parse = jsonType.GetMethod("Parse", new[] { typeof(string) });
            var deserialize = manager.GetMethod("DeserializeSongSourceSettings");
            var older = deserialize.Invoke(null, new[] { parse.Invoke(null, new object[] { "{}" }) });
            Assert.That(Value(older), Is.False);
            var optionsType = Production("Newtonsoft.Json.JsonSerializerSettings");
            var options = manager.GetField("JsonSettings", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var serialize = Production("Newtonsoft.Json.JsonConvert").GetMethod("SerializeObject", new[] { typeof(object), optionsType });
            var setting = state.Settings.GetType().GetProperty(name).GetValue(state.Settings);
            var previous = Value(state.Settings);
            try
            {
                foreach (bool enabled in new[] { true, false })
                {
                    selection.SetValue(toggle, enabled);
                    Assert.That(Value(state.Settings), Is.EqualTo(enabled));
                    var json = serialize.Invoke(null, new[] { state.Settings, options });
                    var restored = deserialize.Invoke(null, new[] { parse.Invoke(null, new[] { json }) });
                    Assert.That(Value(restored), Is.EqualTo(enabled));
                }
            }
            finally { setting.GetType().GetMethod("SetValueWithoutNotify").Invoke(setting, new object[] { previous }); }

            bool Value(object container)
            {
                var option = container.GetType().GetProperty(name).GetValue(container);
                return (bool)option.GetType().GetProperty("Value").GetValue(option);
            }
        }

        [UnityTest]
        public IEnumerator ExperimentalHiHatControlsBuildWithLocalizedLabelsAndEditRealValues()
        {
            using var state = new SongManagerInteractionState(this);
            var manager = Production("YARG.Settings.SettingsManager");
            var experimental = ((IEnumerable)manager.GetField("AllSettingsTabs").GetValue(null)).Cast<object>()
                .Single(tab => (string)tab.GetType().GetProperty("Name").GetValue(tab) == "Experimental");
            var menu = PositionMenu();
            menu.GetType().GetProperty("CurrentTab").GetSetMethod(true).Invoke(menu, new[] { experimental });
            menu.GetType().GetMethod("UpdateSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(menu, new object[] { false });
            yield return null;
            var rows = state.Container.GetComponentsInChildren(Production("YARG.Menu.Settings.Visuals.SliderSettingVisual"));
            string[] names = { "EliteHiHatWaitingOpacity", "EliteHiHatHeldOpacity", "EliteHiHatWaitingGlow",
                "EliteHiHatHeldGlow", "EliteCymbalFlamGlowMinimum", "EliteCymbalFlamGlowMaximum" };
            var gains = Shader.GetGlobalVector("_YargCymbalFlamGlowAdjustments");
            float[] saved = new float[names.Length];
            object[] settings = new object[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                settings[i] = state.Settings.GetType().GetProperty(names[i]).GetValue(state.Settings);
                saved[i] = (float)settings[i].GetType().GetProperty("Value").GetValue(settings[i]);
            }
            try
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var row = rows.Single(candidate => (string)candidate.GetType().GetProperty("UnlocalizedName").GetValue(candidate) == names[i]);
                    var label = row.GetType().GetProperty("SettingLabel").GetValue(row);
                    string text = (string)label.GetType().GetProperty("text").GetValue(label);
                    Assert.That(text, Does.Contain(names[i].StartsWith("EliteCymbalFlam") ? "Cymbal Flam Pulse" : "Hi-Hat Sustain"));
                    Assert.That(text, Does.Contain("%"));
                    Assert.That((bool)row.GetType().GetProperty("IsEditable").GetValue(row), Is.True);
                    var slider = row.GetType().GetField("_slider", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(row);
                    float value = names[i] == "EliteCymbalFlamGlowMaximum" ? 150f : 37f + i;
                    slider.GetType().GetProperty("Value").SetValue(slider, value);
                    Assert.That(settings[i].GetType().GetProperty("Value").GetValue(settings[i]), Is.EqualTo(value));
                }
            }
            finally
            {
                for (int i = 0; i < names.Length; i++)
                    settings[i].GetType().GetMethod("SetValueWithoutNotify").Invoke(settings[i], new object[] { saved[i] });
                Shader.SetGlobalVector("_YargCymbalFlamGlowAdjustments", gains);
            }
        }
    }
}
