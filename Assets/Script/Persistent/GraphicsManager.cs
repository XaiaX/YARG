// pattern: Imperative Shell
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using YARG.Core.Logging;
using YARG.Settings;

namespace YARG
{
    public enum StarPowerHighwayFxMode
    {
        On,
        Reduced,
        Off
    }

    public partial class GraphicsManager : MonoSingleton<GraphicsManager>
    {
        [SerializeField]
        private VolumeProfile postProcessingProfile = null;

        private Bloom bloom = null;
        private FilmGrain filmGrain = null;
        private GraphicsQualitySettings _qualityOverrides;
        private RenderPipelineAsset[] _originalPipelines;
        private int[] _originalVsync;
        private int _originalQuality;

        public float VenueRenderScale = 1.0f;

        public bool BloomEnabled
        {
            set => bloom.active = value;
            get => bloom.active;
        }

        public bool FilmGrainEnabled
        {
            set => filmGrain.active = value;
            get => filmGrain.active;
        }

        public void ApplyGraphicsSettings()
        {
            var settings = SettingsManager.Settings;
            if (settings == null) return;

            InitializeQualityOverrides();

            int quality = settings.LowQuality.Value ? 0 : 1;
            if (QualitySettings.GetQualityLevel() != quality)
                QualitySettings.SetQualityLevel(quality, true);

            _qualityOverrides.Apply(settings, GetPipelineMsaaSamples());
            QualitySettings.renderPipeline = _qualityOverrides.Pipeline;
            ApplyAntialiasingSettings();
            FramePacing.Apply();
        }

        private void InitializeQualityOverrides()
        {
            if (_qualityOverrides == null)
            {
                _originalQuality = QualitySettings.GetQualityLevel();
                _originalPipelines = new RenderPipelineAsset[QualitySettings.names.Length];
                _originalVsync = new int[_originalPipelines.Length];
                for (int i = 0; i < _originalPipelines.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    _originalPipelines[i] = QualitySettings.renderPipeline;
                    _originalVsync[i] = QualitySettings.vSyncCount;
                }
                QualitySettings.SetQualityLevel(_originalQuality, false);
                _qualityOverrides = new GraphicsQualitySettings(
                    (UniversalRenderPipelineAsset) _originalPipelines[1],
                    (UniversalRenderPipelineAsset) _originalPipelines[0]);
            }
        }

        private void RestoreQualityAssets()
        {
            if (_qualityOverrides == null) return;
            // Unity can persist quality settings in the Editor. Runtime overrides
            // must never leave cloned pipelines or modified presets behind.
            for (int i = 0; i < _originalPipelines.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = _originalPipelines[i];
                QualitySettings.vSyncCount = _originalVsync[i];
            }
            _qualityOverrides.Dispose();
            _qualityOverrides = null;
            QualitySettings.SetQualityLevel(_originalQuality, true);
        }

        protected override void SingletonDestroy()
        {
            RestoreQualityAssets();
        }

        protected override void SingletonAwake()
        {
            // Capture authored presets before settings callbacks can override VSync.
            InitializeQualityOverrides();
            if (!postProcessingProfile.TryGet(out bloom))
            {
                YargLogger.LogError("Could not find bloom component in the post process volume.");
            }
            if (!postProcessingProfile.TryGet(out filmGrain))
            {
                YargLogger.LogError("Could not find film grain component in the post process volume.");
            }
        }

        private void OnApplicationQuit()
        {
            RestoreQualityAssets();
#if UNITY_EDITOR

            // For some reason, UNITY EDITOR SAVES THIS IN THE VOLUME PROFILE!!!!
            BloomEnabled = true;
            FilmGrainEnabled = true;

#endif
        }
    }
}
