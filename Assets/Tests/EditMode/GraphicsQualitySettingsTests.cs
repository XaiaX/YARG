using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class GraphicsQualitySettingsTests
    {
        private object _settings;
        private object _runtime;
        private bool _ownsAudio;
        private ScriptableObject _high;
        private ScriptableObject _low;

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

        [SetUp]
        public void SetUp()
        {
            var runtimeType = Production("YARG.GraphicsQualitySettings");
            Assert.That(runtimeType, Is.Not.Null, "Runtime quality overrides have not been implemented.");
            Production("YARG.Helpers.PathHelper").GetMethod("Init").Invoke(null, null);
            var audio = Production("YARG.Core.Audio.GlobalAudioHandler");
            if (audio.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null)
            {
                audio.GetMethod("Initialize").MakeGenericMethod(Production("YARG.Audio.BASS.BassAudioManager"))
                    .Invoke(null, null);
                _ownsAudio = true;
            }
            _settings = Activator.CreateInstance(Production("YARG.Settings.SettingsManager+SettingContainer"));
            _high = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/High.asset");
            _low = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/Low.asset");
            _runtime = Activator.CreateInstance(runtimeType, _high, _low);
        }

        [TearDown]
        public void TearDown()
        {
            (_runtime as IDisposable)?.Dispose();
            _runtime = null;
            if (_ownsAudio)
                Production("YARG.Core.Audio.GlobalAudioHandler").GetMethod("Close").Invoke(null, null);
            _ownsAudio = false;
        }

        private object Setting(string name)
        {
            var property = _settings.GetType().GetProperty(name);
            Assert.That(property, Is.Not.Null, $"Missing setting {name}");
            return property.GetValue(_settings);
        }

        private void Set(string name, bool value) => Setting(name).GetType()
            .GetMethod("SetValueWithoutNotify").Invoke(Setting(name), new object[] { value });

        private void Apply(int samples = 4) => _runtime.GetType().GetMethod("Apply")
            .Invoke(_runtime, new[] { _settings, (object) samples });

        private ScriptableObject Pipeline => (ScriptableObject) _runtime.GetType().GetProperty("Pipeline")
            .GetValue(_runtime);

        private static object Property(object owner, string name) => owner.GetType().GetProperty(name).GetValue(owner);

        [Test]
        public void LowQualityOverridesWithoutDiscardingPreferences()
        {
            Set("LowQuality", false);
            Set("FullResolutionTextures", true);
            Set("SoftShadows", true);
            Set("EnhancedReflections", false);
            Apply();
            Assert.That(QualitySettings.globalTextureMipmapLimit, Is.Zero);
            Assert.That(Property(Pipeline, "supportsSoftShadows"), Is.True);
            Assert.That(Property(Pipeline, "reflectionProbeBlending"), Is.False);

            Set("LowQuality", true);
            Apply();
            Assert.That(QualitySettings.globalTextureMipmapLimit, Is.EqualTo(1));
            Assert.That(Property(Pipeline, "supportsSoftShadows"), Is.False);
            Assert.That(Property(Pipeline, "msaaSampleCount"), Is.EqualTo(1));
            Assert.That(Property(Setting("SoftShadows"), "Value"), Is.True);

            Set("LowQuality", false);
            Apply(8);
            Assert.That(QualitySettings.globalTextureMipmapLimit, Is.Zero);
            Assert.That(Property(Pipeline, "supportsSoftShadows"), Is.True);
            Assert.That(Property(Pipeline, "reflectionProbeBlending"), Is.False);
            Assert.That(Property(Pipeline, "msaaSampleCount"), Is.EqualTo(8));
        }

        [Test]
        public void PipelineRendererAndFeaturesArePrivateClones()
        {
            string originalPipeline = EditorJsonUtility.ToJson(_high);
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/High_Renderer.asset");
            string originalRenderer = EditorJsonUtility.ToJson(renderer);
            var sourceFeatures = (IList) Property(renderer, "rendererFeatures");
            var originalFeatures = sourceFeatures.Cast<ScriptableObject>().Select(feature => EditorJsonUtility.ToJson(feature)).ToArray();
            Set("AmbientOcclusion", false);
            Apply();

            Assert.That(Pipeline, Is.Not.SameAs(_high));
            var serialized = new SerializedObject(Pipeline);
            var cloneRenderer = serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
            Assert.That(cloneRenderer, Is.Not.SameAs(renderer));
            var cloneFeatures = (IList) Property(cloneRenderer, "rendererFeatures");
            for (int i = 0; i < sourceFeatures.Count; i++)
                Assert.That(cloneFeatures[i], Is.Not.SameAs(sourceFeatures[i]));
            Assert.That(EditorJsonUtility.ToJson(_high), Is.EqualTo(originalPipeline));
            Assert.That(EditorJsonUtility.ToJson(renderer), Is.EqualTo(originalRenderer));
            Assert.That(sourceFeatures.Cast<ScriptableObject>().Select(feature => EditorJsonUtility.ToJson(feature)), Is.EqualTo(originalFeatures));

            var ambient = cloneFeatures.Cast<ScriptableObject>().Single(feature => feature.GetType().Name == "ScreenSpaceAmbientOcclusion");
            Assert.That(Property(ambient, "isActive"), Is.False);
            Set("AmbientOcclusion", true);
            Apply();
            Assert.That(Property(ambient, "isActive"), Is.True);
        }

        [Test]
        public void DisposeRestoresQualityFieldsAndDoesNotChangeVSync()
        {
            int mipLimit = QualitySettings.globalTextureMipmapLimit;
            var anisotropy = QualitySettings.anisotropicFiltering;
            var skinWeights = QualitySettings.skinWeights;
            float lodBias = QualitySettings.lodBias;
            int vsync = QualitySettings.vSyncCount;
            Set("FullResolutionTextures", false);
            Set("AnisotropicFiltering", false);
            Set("DetailedModels", false);
            Set("FullSkinningQuality", false);
            Apply();
            ((IDisposable) _runtime).Dispose();
            _runtime = null;
            Assert.That(QualitySettings.globalTextureMipmapLimit, Is.EqualTo(mipLimit));
            Assert.That(QualitySettings.anisotropicFiltering, Is.EqualTo(anisotropy));
            Assert.That(QualitySettings.skinWeights, Is.EqualTo(skinWeights));
            Assert.That(QualitySettings.lodBias, Is.EqualTo(lodBias));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(vsync));
        }

        [Test]
        public void ManagerRestoresBothPresetsAfterRepeatedOverrides()
        {
            var manager = Production("YARG.Settings.SettingsManager");
            var settingsProperty = manager.GetProperty("Settings");
            var savedSettings = settingsProperty.GetValue(null);
            int originalQuality = QualitySettings.GetQualityLevel();
            int originalFps = Application.targetFrameRate;
            var qualityProperties = new[]
            {
                "globalTextureMipmapLimit", "anisotropicFiltering", "lodBias", "softVegetation",
                "billboardsFaceCameraPosition", "skinWeights", "pixelLightCount", "particleRaycastBudget",
                "shadows", "shadowResolution", "shadowCascades", "shadowDistance", "shadowmaskMode",
                "realtimeReflectionProbes", "antiAliasing", "vSyncCount", "renderPipeline"
            }.Select(name => typeof(QualitySettings).GetProperty(name)).ToArray();
            var originals = new object[QualitySettings.names.Length][];
            for (int i = 0; i < originals.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                originals[i] = qualityProperties.Select(property => property.GetValue(null)).ToArray();
            }
            QualitySettings.SetQualityLevel(originalQuality, false);
            string highJson = EditorJsonUtility.ToJson(_high);
            string lowJson = EditorJsonUtility.ToJson(_low);
            var host = new GameObject("graphics-quality-integration-test");
            host.SetActive(false);
            Component graphics = null;
            try
            {
                settingsProperty.GetSetMethod(true).Invoke(null, new[] { _settings });
                graphics = host.AddComponent(Production("YARG.GraphicsManager"));
                var apply = graphics.GetType().GetMethod("ApplyGraphicsSettings");
                Set("FullResolutionTextures", false);
                Set("HighQualityShadows", false);
                Set("AmbientOcclusion", false);
                Set("LowQuality", false);
                apply.Invoke(graphics, null);
                var firstRuntime = QualitySettings.renderPipeline;
                apply.Invoke(graphics, null);
                Assert.That(QualitySettings.renderPipeline, Is.SameAs(firstRuntime), "Repeated callbacks reuse the runtime pipeline.");
                Set("LowQuality", true);
                apply.Invoke(graphics, null);
                Set("LowQuality", false);
                apply.Invoke(graphics, null);
                Assert.That(QualitySettings.globalTextureMipmapLimit, Is.EqualTo(1), "Returning from Low Quality restores the saved texture preference.");
                graphics.GetType().GetMethod("SingletonDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(graphics, null);

                for (int i = 0; i < originals.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    for (int field = 0; field < qualityProperties.Length; field++)
                        Assert.That(qualityProperties[field].GetValue(null), Is.EqualTo(originals[i][field]),
                            $"Quality {i}: {qualityProperties[field].Name} was not restored.");
                }
                Assert.That(EditorJsonUtility.ToJson(_high), Is.EqualTo(highJson));
                Assert.That(EditorJsonUtility.ToJson(_low), Is.EqualTo(lowJson));
            }
            finally
            {
                if (graphics != null)
                    graphics.GetType().GetMethod("SingletonDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(graphics, null);
                UnityEngine.Object.DestroyImmediate(host);
                for (int i = 0; i < originals.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    for (int field = 0; field < qualityProperties.Length; field++)
                        qualityProperties[field].SetValue(null, originals[i][field]);
                }
                QualitySettings.SetQualityLevel(originalQuality, false);
                Application.targetFrameRate = originalFps;
                settingsProperty.GetSetMethod(true).Invoke(null, new[] { savedSettings });
            }
        }
    }
}
