// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class HiHatSustainTuningSettingsTests
    {
        private bool _ownsAudio;
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static readonly string[] Names = { "EliteHiHatWaitingOpacity", "EliteHiHatHeldOpacity",
            "EliteHiHatWaitingGlow", "EliteHiHatHeldGlow", "EliteOpenHatGhostGlow", "EliteOpenHatNeutralGlow",
            "EliteOpenHatAccentGlow", "EliteOpenHatGlowMinimum", "EliteOpenHatShadowMinimum", "EliteOpenHatShadowPeak" };
        private static object Container() => Activator.CreateInstance(Production("YARG.Settings.SettingsManager")
            .GetNestedType("SettingContainer", BindingFlags.Public));
        private static object Setting(object container, string name)
        {
            var property = container.GetType().GetProperty(name);
            Assert.That(property, Is.Not.Null, $"Experimental tuning needs {name}.");
            return property.GetValue(container);
        }
        private static float Value(object setting) => (float)setting.GetType().GetProperty("Value").GetValue(setting);
        private static void Set(object setting, float value) => setting.GetType().GetMethod("SetValueWithoutNotify")
            .Invoke(setting, new object[] { value });

        [OneTimeSetUp]
        public void InitializeAudio()
        {
            Production("YARG.Helpers.PathHelper").GetMethod("Init").Invoke(null, null);
            var audio = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audio.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == null)
            {
                audio.GetMethod("Initialize").MakeGenericMethod(Production("YARG.Audio.BASS.BassAudioManager"))
                    .Invoke(null, null);
                _ownsAudio = true;
            }
        }

        [OneTimeTearDown]
        public void CloseOwnedAudio()
        {
            if (_ownsAudio) Production("YARG.Core.Audio.GlobalAudioHandler").GetMethod("Close").Invoke(null, null);
        }

        [TestCase("EliteHiHatWaitingOpacity", 28f, 100f)]
        [TestCase("EliteHiHatHeldOpacity", 33f, 100f)]
        [TestCase("EliteHiHatWaitingGlow", 35f, 200f)]
        [TestCase("EliteHiHatHeldGlow", 70f, 200f)]
        [TestCase("EliteOpenHatGhostGlow", 40f, 200f)]
        [TestCase("EliteOpenHatNeutralGlow", 60f, 200f)]
        [TestCase("EliteOpenHatAccentGlow", 80f, 200f)]
        [TestCase("EliteOpenHatGlowMinimum", 5f, 200f)]
        [TestCase("EliteOpenHatShadowMinimum", 0.5f, 200f)]
        [TestCase("EliteOpenHatShadowPeak", 10f, 100f)]
        [TestCase("EliteCymbalFlamGlowMinimum", 35f, 100f)]
        [TestCase("EliteCymbalFlamGlowMaximum", 100f, 200f)]
        public void TemporarySlidersHaveDefaultsAndBoundedPercentages(string name, float expected, float maximum)
        {
            var setting = Setting(Container(), name);
            Assert.That(Value(setting), Is.EqualTo(expected));
            Assert.That(setting.GetType().GetProperty("Min").GetValue(setting), Is.EqualTo(0f));
            Assert.That(setting.GetType().GetProperty("Max").GetValue(setting), Is.EqualTo(maximum));
            Set(setting, -10); Assert.That(Value(setting), Is.Zero);
            Set(setting, maximum + 20); Assert.That(Value(setting), Is.EqualTo(maximum));
        }

        [Test]
        public void ExperimentalTabContainsHiHatTuningControls()
        {
            var tabs = (IEnumerable)Production("YARG.Settings.SettingsManager").GetField("AllSettingsTabs").GetValue(null);
            var experimental = tabs.Cast<object>().Single(tab => (string)tab.GetType().GetProperty("Name").GetValue(tab) == "Experimental");
            var fields = ((IEnumerable)experimental.GetType().GetProperty("Settings").GetValue(experimental))
                .Cast<object>().Select(field => field.GetType().GetProperty("FieldName")?.GetValue(field)).ToArray();
            foreach (string name in Names) Assert.That(fields, Does.Contain(name));
            var buttons = ((IEnumerable)experimental.GetType().GetProperty("Settings").GetValue(experimental))
                .Cast<object>().SelectMany(row => row.GetType().GetProperty("Buttons")?.GetValue(row) as string[]
                    ?? Array.Empty<string>());
            Assert.That(buttons, Does.Contain("ResetOpenHatGlow"));
        }

        [Test]
        public void ResetOpenHatGlowRestoresOnlyTheSixGlowControlsAndAppliesTheirShaderGains()
        {
            var container = Container();
            foreach (var name in Names) Set(Setting(container, name), 18f);
            var reset = container.GetType().GetMethod("ResetOpenHatGlow");
            Assert.That(reset, Is.Not.Null);
            const string glow = "_YargOpenHatGlowAdjustments";
            const string shadow = "_YargOpenHatShadowAdjustments";
            var oldGlow = Shader.GetGlobalVector(glow);
            var oldShadow = Shader.GetGlobalVector(shadow);
            try
            {
                Shader.SetGlobalVector(glow, Vector4.zero);
                Shader.SetGlobalVector(shadow, Vector4.zero);
                reset.Invoke(container, null);
                Assert.That(Names.Select(name => Value(Setting(container, name))),
                    Is.EqualTo(new[] { 18f, 18f, 18f, 18f, 40f, 60f, 80f, 5f, 0.5f, 10f }));
                var gains = Shader.GetGlobalVector(glow);
                var shade = Shader.GetGlobalVector(shadow);
                Assert.That(new[] { gains.x, gains.y, gains.z, gains.w, shade.x, shade.y }, Is.EqualTo(new[]
                {
                    40f / 26 - 1, 60f / 47 - 1, 80f / 72 - 1, 5f / 12 - 1, 0.5f / 12 - 1, 10f / 50 - 1
                }).Within(0.00001f));
            }
            finally
            {
                Shader.SetGlobalVector(glow, oldGlow);
                Shader.SetGlobalVector(shadow, oldShadow);
            }
        }

        [TestCase(0)]
        [TestCase(2)]
        public void PedalEffectDropdownRestoresTheChosenShaderEffect(int savedEffect)
        {
            var original = Container();
            const string NAME = "ElitePedalBeamEffect";
            const string SHADER_PROPERTY = "_YargElitePedalBeamEffect";
            var setting = Setting(original, NAME);
            var values = ((IEnumerable) setting).Cast<object>().ToArray();
            Assert.That(values.Select(value => value.ToString()),
                Is.EqualTo(new[] { "Original", "GemFlow", "SustainShimmer" }));
            Assert.That(setting.GetType().GetProperty("Value").GetValue(setting), Is.EqualTo(values[1]));
            var tabs = (IEnumerable) Production("YARG.Settings.SettingsManager").GetField("AllSettingsTabs").GetValue(null);
            var experimental = tabs.Cast<object>().Single(tab => (string) tab.GetType().GetProperty("Name").GetValue(tab) == "Experimental");
            var fields = (IEnumerable) experimental.GetType().GetProperty("Settings").GetValue(experimental);
            Assert.That(fields.Cast<object>().Select(field => field.GetType().GetProperty("FieldName")?.GetValue(field)), Does.Contain(NAME));

            float previous = Shader.GetGlobalFloat(SHADER_PROPERTY);
            try
            {
                setting.GetType().GetMethod("SetValueWithoutNotify").Invoke(setting, new[] { values[savedEffect] });
                var manager = Production("YARG.Settings.SettingsManager");
                var optionsType = Production("Newtonsoft.Json.JsonSerializerSettings");
                var options = manager.GetField("JsonSettings", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                var convert = Production("Newtonsoft.Json.JsonConvert");
                var json = convert.GetMethod("SerializeObject", new[] { typeof(object), optionsType })
                    .Invoke(null, new[] { original, options });
                var restored = convert.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type), optionsType })
                    .Invoke(null, new[] { json, original.GetType(), options });
                var restoredSetting = Setting(restored, NAME);
                restoredSetting.GetType().GetMethod("ForceInvokeCallback").Invoke(restoredSetting, null);
                Assert.That(Shader.GetGlobalFloat(SHADER_PROPERTY), Is.EqualTo((float)savedEffect));
            }
            finally { Shader.SetGlobalFloat(SHADER_PROPERTY, previous); }
        }

        [TestCase("{\"EliteOpenHatGlowMinimum\":9}", 9f)]
        [TestCase("{\"EliteOpenHatGlowMinimum\":9,\"EliteOpenHatShadowMinimum\":2}", 2f)]
        public void ExistingGlowFloorSeedsOnlyAMissingShadowFloor(string json, float expected)
        {
            var jobject = Production("Newtonsoft.Json.Linq.JObject");
            var saved = jobject.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
            var migrate = Production("YARG.Settings.SettingsMigration")
                .GetMethod("Migrate", BindingFlags.NonPublic | BindingFlags.Static);
            var migrated = migrate.Invoke(null, new object[] { saved, false });
            var settings = Production("YARG.Settings.SettingsManager").GetMethod("DeserializeSongSourceSettings")
                .Invoke(null, new[] { migrated });
            Assert.That(Value(Setting(settings, "EliteOpenHatShadowMinimum")), Is.EqualTo(expected));
        }

        [Test]
        public void TuningValuesRoundTripThroughNormalSettingsSerialization()
        {
            var original = Container();
            float[] values = { 31, 62, 81, 173, 58, 91, 120, 7, 2, 29 };
            for (int i = 0; i < Names.Length; i++) Set(Setting(original, Names[i]), values[i]);
            var manager = Production("YARG.Settings.SettingsManager");
            var optionsType = Production("Newtonsoft.Json.JsonSerializerSettings");
            var options = manager.GetField("JsonSettings", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var convert = Production("Newtonsoft.Json.JsonConvert");
            string json = (string)convert.GetMethod("SerializeObject", new[] { typeof(object), optionsType })
                .Invoke(null, new[] { original, options });
            var restored = convert.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type), optionsType })
                .Invoke(null, new object[] { json, original.GetType(), options });
            for (int i = 0; i < Names.Length; i++) Assert.That(Value(Setting(restored, Names[i])), Is.EqualTo(values[i]));
        }
    }
}
