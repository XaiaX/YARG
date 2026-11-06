using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using YARG.Settings;
using Object = UnityEngine.Object;
using ShadowQuality = UnityEngine.ShadowQuality;
using ShadowResolution = UnityEngine.ShadowResolution;

namespace YARG
{
    /// <summary>
    /// Applies saved graphics preferences to private runtime copies of the rendering assets.
    /// Low Quality overrides the effective values without writing back to the preferences.
    /// </summary>
    public sealed class GraphicsQualitySettings : IDisposable
    {
        private readonly UniversalRenderPipelineAsset _high;
        private readonly UniversalRenderPipelineAsset _low;
        private readonly PipelineFields _highFields;
        private readonly PipelineFields _lowFields;
        private readonly ScriptableRendererData[] _renderers;
        private readonly List<ScriptableRendererFeature> _features = new();
        private readonly Dictionary<int, QualitySnapshot> _originalQuality = new();
        private int _lastPipelineConfiguration = -1;
        private bool _disposed;

        public UniversalRenderPipelineAsset Pipeline { get; }

        public GraphicsQualitySettings(UniversalRenderPipelineAsset high, UniversalRenderPipelineAsset low)
        {
            _high = high != null ? high : throw new ArgumentNullException(nameof(high));
            _low = low != null ? low : throw new ArgumentNullException(nameof(low));
            _highFields = JsonUtility.FromJson<PipelineFields>(JsonUtility.ToJson(high));
            _lowFields = JsonUtility.FromJson<PipelineFields>(JsonUtility.ToJson(low));
            Pipeline = Clone(high);

            // Instantiate does not deep-copy referenced renderer assets or their features.
            // Replace those references before this pipeline is assigned to QualitySettings.
            _renderers = new ScriptableRendererData[high.rendererDataList.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                var source = high.rendererDataList[i];
                if (source == null)
                    continue;

                var renderer = Clone(source);
                renderer.rendererFeatures.Clear();
                foreach (var feature in source.rendererFeatures)
                {
                    var copy = feature != null ? Clone(feature) : null;
                    renderer.rendererFeatures.Add(copy);
                    if (copy != null)
                        _features.Add(copy);
                }
                _renderers[i] = renderer;
                renderer.SetDirty();
            }

            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new RendererReferences
            {
                m_RendererDataList = _renderers
            }), Pipeline);
        }

        public void Apply(SettingsManager.SettingContainer settings, int msaaSamples)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GraphicsQualitySettings));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            int quality = QualitySettings.GetQualityLevel();
            if (!_originalQuality.ContainsKey(quality))
                _originalQuality.Add(quality, QualitySnapshot.Capture());

            bool custom = !settings.LowQuality.Value;
            bool textures = custom && settings.FullResolutionTextures.Value;
            bool filtering = custom && settings.AnisotropicFiltering.Value;
            bool models = custom && settings.DetailedModels.Value;
            bool skinning = custom && settings.FullSkinningQuality.Value;
            bool lighting = custom && settings.HighQualityLighting.Value;
            bool shadows = custom && settings.HighQualityShadows.Value;
            bool reflections = custom && settings.EnhancedReflections.Value;
            int samples = custom ? msaaSamples : 1;

            QualitySettings.globalTextureMipmapLimit = textures ? 0 : 1;
            QualitySettings.anisotropicFiltering = filtering ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Disable;
            QualitySettings.lodBias = models ? 2f : 0.4f;
            QualitySettings.softVegetation = models;
            QualitySettings.billboardsFaceCameraPosition = models;
            QualitySettings.skinWeights = skinning ? SkinWeights.Unlimited : SkinWeights.TwoBones;
            QualitySettings.pixelLightCount = lighting ? 2 : 0;
            QualitySettings.particleRaycastBudget = lighting ? 2048 : 1;
            QualitySettings.shadows = shadows ? ShadowQuality.All : ShadowQuality.Disable;
            QualitySettings.shadowResolution = shadows ? ShadowResolution.Medium : ShadowResolution.Low;
            QualitySettings.shadowCascades = shadows ? 2 : 1;
            QualitySettings.shadowDistance = shadows ? 40f : 20f;
            QualitySettings.shadowmaskMode = shadows ? ShadowmaskMode.DistanceShadowmask : ShadowmaskMode.Shadowmask;
            QualitySettings.realtimeReflectionProbes = reflections;
            QualitySettings.antiAliasing = samples > 1 ? samples : 0;

            bool softShadows = custom && settings.SoftShadows.Value;
            bool additionalShadows = custom && settings.AdditionalLightShadows.Value;
            bool occlusion = custom && settings.AmbientOcclusion.Value;
            int configuration = (shadows ? 1 : 0) | (lighting ? 2 : 0) | (reflections ? 4 : 0) |
                (softShadows ? 8 : 0) | (additionalShadows ? 16 : 0) | (occlusion ? 32 : 0) | (samples << 6);
            if (_lastPipelineConfiguration == configuration)
                return;

            var shadowSource = shadows ? _high : _low;
            var shadowFields = shadows ? _highFields : _lowFields;
            var lightingFields = lighting ? _highFields : _lowFields;
            var reflectionFields = reflections ? _highFields : _lowFields;
            Pipeline.msaaSampleCount = samples;
            Pipeline.mainLightShadowmapResolution = shadowSource.mainLightShadowmapResolution;
            Pipeline.additionalLightsShadowmapResolution = shadowSource.additionalLightsShadowmapResolution;
            Pipeline.shadowDistance = shadowSource.shadowDistance;
            Pipeline.shadowCascadeCount = shadowSource.shadowCascadeCount;
            Pipeline.cascadeBorder = shadowSource.cascadeBorder;
            Pipeline.conservativeEnclosingSphere = shadowSource.conservativeEnclosingSphere;
            Pipeline.maxAdditionalLightsCount = lighting ? _high.maxAdditionalLightsCount : _low.maxAdditionalLightsCount;

            // URP exposes these serialized fields with internal setters. A narrow Unity
            // serialization patch configures only our clone; no package reflection or
            // shared asset mutation is required. Names match the pinned URP package.
            var fields = new PipelineFields
            {
                m_SoftShadowsSupported = softShadows && _highFields.m_SoftShadowsSupported,
                m_AdditionalLightShadowsSupported = additionalShadows &&
                    _highFields.m_AdditionalLightShadowsSupported,
                m_AdditionalLightsShadowResolutionTierLow = shadowFields.m_AdditionalLightsShadowResolutionTierLow,
                m_AdditionalLightsShadowResolutionTierMedium = shadowFields.m_AdditionalLightsShadowResolutionTierMedium,
                m_AdditionalLightsShadowResolutionTierHigh = shadowFields.m_AdditionalLightsShadowResolutionTierHigh,
                m_ReflectionProbeBlending = reflectionFields.m_ReflectionProbeBlending,
                m_ReflectionProbeBoxProjection = reflectionFields.m_ReflectionProbeBoxProjection,
                m_ReflectionProbeAtlas = reflectionFields.m_ReflectionProbeAtlas,
                m_AdditionalLightsCookieResolution = lightingFields.m_AdditionalLightsCookieResolution,
                m_AdditionalLightsCookieFormat = lightingFields.m_AdditionalLightsCookieFormat,
            };
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fields), Pipeline);

            foreach (var feature in _features)
            {
                if (feature.GetType().Name == "ScreenSpaceAmbientOcclusion")
                    feature.SetActive(occlusion);
            }
            foreach (var renderer in _renderers)
                renderer?.SetDirty();
            _lastPipelineConfiguration = configuration;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Unity can persist quality-level runtime changes when leaving the Editor.
            // Restore every level we touched, not just the one currently selected.
            int current = QualitySettings.GetQualityLevel();
            foreach (var pair in _originalQuality)
            {
                QualitySettings.SetQualityLevel(pair.Key, false);
                pair.Value.Restore();
            }
            QualitySettings.SetQualityLevel(current, false);

            Destroy(Pipeline);
            foreach (var renderer in _renderers)
                Destroy(renderer);
            foreach (var feature in _features)
            {
                // A cloned feature may have allocated during OnEnable even when its
                // renderer was never used. Feature disposal is also safe after URP cleanup.
                feature.Dispose();
                Destroy(feature);
            }
        }

        private static T Clone<T>(T source) where T : Object
        {
            var clone = Object.Instantiate(source);
            clone.name = source.name + " (Runtime)";
            clone.hideFlags = HideFlags.HideAndDontSave;
            return clone;
        }

        private static void Destroy(Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(value);
            else
                Object.DestroyImmediate(value);
        }

        [Serializable]
        private sealed class RendererReferences
        {
            public ScriptableRendererData[] m_RendererDataList;
        }

        [Serializable]
        private sealed class PipelineFields
        {
            public bool m_SoftShadowsSupported;
            public bool m_AdditionalLightShadowsSupported;
            public int m_AdditionalLightsShadowResolutionTierLow;
            public int m_AdditionalLightsShadowResolutionTierMedium;
            public int m_AdditionalLightsShadowResolutionTierHigh;
            public bool m_ReflectionProbeBlending;
            public bool m_ReflectionProbeBoxProjection;
            public bool m_ReflectionProbeAtlas;
            public int m_AdditionalLightsCookieResolution;
            public int m_AdditionalLightsCookieFormat;
        }

        private readonly struct QualitySnapshot
        {
            private readonly int _mipLimit, _pixelLights, _particleBudget, _shadowCascades, _antialiasing;
            private readonly AnisotropicFiltering _filtering;
            private readonly SkinWeights _skinning;
            private readonly float _lodBias, _shadowDistance;
            private readonly bool _vegetation, _billboards, _reflections;
            private readonly ShadowQuality _shadows;
            private readonly ShadowResolution _shadowResolution;
            private readonly ShadowmaskMode _shadowmask;

            private QualitySnapshot(bool capture)
            {
                _mipLimit = QualitySettings.globalTextureMipmapLimit;
                _filtering = QualitySettings.anisotropicFiltering;
                _lodBias = QualitySettings.lodBias;
                _vegetation = QualitySettings.softVegetation;
                _billboards = QualitySettings.billboardsFaceCameraPosition;
                _skinning = QualitySettings.skinWeights;
                _pixelLights = QualitySettings.pixelLightCount;
                _particleBudget = QualitySettings.particleRaycastBudget;
                _shadows = QualitySettings.shadows;
                _shadowResolution = QualitySettings.shadowResolution;
                _shadowCascades = QualitySettings.shadowCascades;
                _shadowDistance = QualitySettings.shadowDistance;
                _shadowmask = QualitySettings.shadowmaskMode;
                _reflections = QualitySettings.realtimeReflectionProbes;
                _antialiasing = QualitySettings.antiAliasing;
            }

            public static QualitySnapshot Capture() => new(true);

            public void Restore()
            {
                QualitySettings.globalTextureMipmapLimit = _mipLimit;
                QualitySettings.anisotropicFiltering = _filtering;
                QualitySettings.lodBias = _lodBias;
                QualitySettings.softVegetation = _vegetation;
                QualitySettings.billboardsFaceCameraPosition = _billboards;
                QualitySettings.skinWeights = _skinning;
                QualitySettings.pixelLightCount = _pixelLights;
                QualitySettings.particleRaycastBudget = _particleBudget;
                QualitySettings.shadows = _shadows;
                QualitySettings.shadowResolution = _shadowResolution;
                QualitySettings.shadowCascades = _shadowCascades;
                QualitySettings.shadowDistance = _shadowDistance;
                QualitySettings.shadowmaskMode = _shadowmask;
                QualitySettings.realtimeReflectionProbes = _reflections;
                QualitySettings.antiAliasing = _antialiasing;
            }
        }
    }
}
