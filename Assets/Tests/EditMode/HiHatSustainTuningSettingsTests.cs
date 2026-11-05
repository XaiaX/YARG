// pattern: Imperative Shell
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class HiHatSustainTuningSettingsTests
    {
        private bool _ownsAudio;
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static readonly string[] Names = { "EliteHiHatWaitingOpacity", "EliteHiHatHeldOpacity",
            "EliteHiHatWaitingGlow", "EliteHiHatHeldGlow" };
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
        public void ExperimentalTabContainsAllFourTemporaryControls()
        {
            var tabs = (IEnumerable)Production("YARG.Settings.SettingsManager").GetField("AllSettingsTabs").GetValue(null);
            var experimental = tabs.Cast<object>().Single(tab => (string)tab.GetType().GetProperty("Name").GetValue(tab) == "Experimental");
            var fields = ((IEnumerable)experimental.GetType().GetProperty("Settings").GetValue(experimental))
                .Cast<object>().Select(field => field.GetType().GetProperty("FieldName")?.GetValue(field)).ToArray();
            foreach (string name in Names) Assert.That(fields, Does.Contain(name));
        }

        [Test]
        public void TuningValuesRoundTripThroughNormalSettingsSerialization()
        {
            var original = Container();
            float[] values = { 31, 62, 81, 173 };
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
