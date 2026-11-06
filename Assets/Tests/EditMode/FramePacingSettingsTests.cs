// pattern: Imperative Shell
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class FramePacingSettingsTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).First(t => t != null);
        private object _settings;
        private object _savedSettings;
        private int _quality, _vsync, _fps;
        private int[] _vsyncByQuality;
        private bool _ownsAudio;

        [SetUp]
        public void SetUp()
        {
            Production("YARG.Helpers.PathHelper").GetMethod("Init").Invoke(null, null);
            var audio = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audio.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null)
            {
                audio.GetMethod("Initialize").MakeGenericMethod(Production("YARG.Audio.BASS.BassAudioManager")).Invoke(null, null);
                _ownsAudio = true;
            }
            var manager = Production("YARG.Settings.SettingsManager");
            _savedSettings = manager.GetProperty("Settings").GetValue(null);
            _settings = Activator.CreateInstance(manager.GetNestedType("SettingContainer"));
            manager.GetProperty("Settings").GetSetMethod(true).Invoke(null, new[] { _settings });
            _quality = QualitySettings.GetQualityLevel();
            _vsync = QualitySettings.vSyncCount;
            _fps = Application.targetFrameRate;
            // VSync belongs to each quality level; restoring only the active
            // level leaves modified presets serialized after the test run.
            _vsyncByQuality = new int[QualitySettings.names.Length];
            for (int i = 0; i < _vsyncByQuality.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                _vsyncByQuality[i] = QualitySettings.vSyncCount;
            }
            QualitySettings.SetQualityLevel(_quality, false);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _vsyncByQuality.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.vSyncCount = _vsyncByQuality[i];
            }
            QualitySettings.SetQualityLevel(_quality, true);
            QualitySettings.vSyncCount = _vsync;
            Application.targetFrameRate = _fps;
            Production("YARG.Settings.SettingsManager").GetProperty("Settings").GetSetMethod(true)
                .Invoke(null, new[] { _savedSettings });
            if (_ownsAudio) Production("YARG.Core.Audio.GlobalAudioHandler").GetMethod("Close").Invoke(null, null);
            _ownsAudio = false;
        }

        private object Setting(string name)
        {
            var property = _settings.GetType().GetProperty(name);
            Assert.That(property, Is.Not.Null, $"Missing setting {name}");
            return property.GetValue(_settings);
        }
        private void Set(string name, object value) => Setting(name).GetType()
            .GetMethod("SetValueWithoutNotify").Invoke(Setting(name), new[] { value });

        [TestCase(true)]
        [TestCase(false)]
        public void QualityChangePreservesSelectedVSync(bool low)
        {
            Set("VSync", true);
            Set("BackgroundFpsCap", 0);
            Set("LowQuality", low);
            QualitySettings.SetQualityLevel(low ? 1 : 0, true);
            QualitySettings.vSyncCount = 1;
            var host = new GameObject("quality-test");
            host.SetActive(false);
            var graphics = host.AddComponent(Production("YARG.GraphicsManager"));
            try
            {
                graphics.GetType().GetMethod("ApplyGraphicsSettings").Invoke(graphics, null);
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
            }
            finally
            {
                graphics.GetType().GetMethod("RestoreQualityAssets", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(graphics, null);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void FpsCapIsEditableOnlyWithoutVSync()
        {
            Set("VSync", true);
            Assert.That(Setting("FpsCap").GetType().GetProperty("IsEditable").GetValue(Setting("FpsCap")), Is.False);
            Set("VSync", false);
            Assert.That(Setting("FpsCap").GetType().GetProperty("IsEditable").GetValue(Setting("FpsCap")), Is.True);
        }

        [Test]
        public void CsvCaptureIsSeparateAndDefaultsOff()
        {
            Set("FrameGraph", true);
            var capture = Setting("TimingDiagnostics");
            Assert.That(capture.GetType().GetProperty("Value").GetValue(capture), Is.False);
        }

        [TestCase("FullResolutionTextures")]
        [TestCase("AnisotropicFiltering")]
        [TestCase("DetailedModels")]
        [TestCase("FullSkinningQuality")]
        [TestCase("AmbientOcclusion")]
        [TestCase("HighQualityShadows")]
        [TestCase("SoftShadows")]
        [TestCase("AdditionalLightShadows")]
        [TestCase("HighQualityLighting")]
        [TestCase("EnhancedReflections")]
        public void LowQualityLocksFeatureControlsWithoutReplacingPreferences(string name)
        {
            var setting = Setting(name);
            Assert.That(setting.GetType().GetProperty("Value").GetValue(setting), Is.True);
            foreach (bool preference in new[] { false, true })
            {
                Set(name, preference);
                Set("LowQuality", true);
                Assert.That(setting.GetType().GetProperty("IsEditable").GetValue(setting), Is.False);
                Assert.That(setting.GetType().GetProperty("Value").GetValue(setting), Is.EqualTo(preference));
                Set("LowQuality", false);
                Assert.That(setting.GetType().GetProperty("IsEditable").GetValue(setting), Is.True);
                Assert.That(setting.GetType().GetProperty("Value").GetValue(setting), Is.EqualTo(preference));
            }
        }

        [TestCase("HighwayAntiAliasing")]
        [TestCase("VenueAntiAliasing")]
        public void LowQualityPreservesAntialiasingPreferences(string name)
        {
            var setting = Setting(name);
            var valueProperty = setting.GetType().GetProperty("Value");
            var msaa = Enum.ToObject(valueProperty.PropertyType, 2);
            Set(name, msaa);
            Set("LowQuality", true);
            Assert.That(setting.GetType().GetProperty("IsEditable").GetValue(setting), Is.False);
            Assert.That(valueProperty.GetValue(setting), Is.EqualTo(msaa));
            Assert.That(Production("YARG.GraphicsManager").GetMethod("GetPipelineMsaaSamples")
                .Invoke(null, null), Is.EqualTo(1));
            Set("LowQuality", false);
            Assert.That(setting.GetType().GetProperty("IsEditable").GetValue(setting), Is.True);
            Assert.That(valueProperty.GetValue(setting), Is.EqualTo(msaa));
        }

        [Test]
        public void ExistingAntialiasingAndNewOverridesSurviveSettingsRoundTrip()
        {
            var json = Production("Newtonsoft.Json.Linq.JObject");
            var parse = json.GetMethod("Parse", new[] { typeof(string) });
            var deserialize = Production("YARG.Settings.SettingsManager").GetMethod("DeserializeSongSourceSettings");
            var input = parse.Invoke(null, new object[]
                { "{\"LowQuality\":true,\"VenueAntiAliasing\":2,\"FullResolutionTextures\":false}" });
            var settings = deserialize.Invoke(null, new[] { input });
            var serialization = Production("YARG.Settings.SettingsManager")
                .GetField("JsonSettings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var serialize = Production("Newtonsoft.Json.JsonConvert").GetMethod("SerializeObject",
                new[] { typeof(object), serialization.GetType() });
            string text = (string)serialize.Invoke(null, new[] { settings, serialization });
            settings = deserialize.Invoke(null, new[] { parse.Invoke(null, new object[] { text }) });
            object Value(string name)
            {
                var setting = settings.GetType().GetProperty(name).GetValue(settings);
                return setting.GetType().GetProperty("Value").GetValue(setting);
            }
            Assert.That(Value("LowQuality"), Is.True);
            Assert.That(Value("FullResolutionTextures"), Is.False);
            Assert.That(Convert.ToInt32(Value("VenueAntiAliasing")), Is.EqualTo(2));
            Assert.That(Convert.ToInt32(Value("HighwayAntiAliasing")), Is.EqualTo(4), "New settings keep the SMAA default.");
        }

        [TestCase(true, 120, 10, true, 1, 120)]
        [TestCase(false, 120, 10, true, 0, 120)]
        [TestCase(true, 120, 10, false, 0, 10)]
        [TestCase(false, 0, 10, true, 0, -1)]
        [TestCase(true, 120, 0, false, 1, 120)]
        public void FramePacingResolvesForegroundBackgroundAndUnlimited(bool vsync, int fps, int background,
            bool focused, int expectedVsync, int expectedFps)
        {
            Set("VSync", vsync); Set("FpsCap", fps); Set("BackgroundFpsCap", background);
            Production("YARG.FramePacing").GetMethod("Apply", new[] { _settings.GetType(), typeof(bool) })
                .Invoke(null, new object[] { _settings, focused });
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(expectedVsync));
            Assert.That(Application.targetFrameRate, Is.EqualTo(expectedFps));
        }

        [Test]
        public void FocusReturnRestoresCurrentSettingsAndDisableBackgroundCapAppliesImmediately()
        {
            var host = new GameObject("focus-test");
            var handler = host.AddComponent(Production("YARG.FocusHandler"));
            var focus = handler.GetType().GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic);
            Set("MuteOnFocusLoss", false); Set("VSync", true); Set("FpsCap", 90); Set("BackgroundFpsCap", 10);
            try
            {
                focus.Invoke(handler, new object[] { false });
                Assert.That(Application.targetFrameRate, Is.EqualTo(10));
                Setting("FpsCap").GetType().GetProperty("Value").SetValue(Setting("FpsCap"), 120);
                Assert.That(Application.targetFrameRate, Is.EqualTo(10), "Editing foreground cap must not release background limit.");
                Setting("BackgroundFpsCap").GetType().GetProperty("Value").SetValue(Setting("BackgroundFpsCap"), 0);
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
                Assert.That(Application.targetFrameRate, Is.EqualTo(120));
                Set("BackgroundFpsCap", 10);
                focus.Invoke(handler, new object[] { false });
                focus.Invoke(handler, new object[] { true });
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
                Assert.That(Application.targetFrameRate, Is.EqualTo(120));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
