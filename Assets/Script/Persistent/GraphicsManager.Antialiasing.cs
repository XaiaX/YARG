// pattern: Imperative Shell
using UnityEngine;
using UnityEngine.Rendering.Universal;
using YARG.Gameplay;
using YARG.Gameplay.Visuals;
using YARG.Settings;

namespace YARG
{
    // Keep existing values stable for saved settings. MSAA was the original 4x choice.
    public enum VenueAntiAliasingMethod
    {
        None = 0,
        FXAA = 1,
        MSAA = 2,
        TAA = 3,
        SMAA = 4,
        MSAA2x = 5,
        MSAA8x = 6,
    }

    public partial class GraphicsManager
    {
        public static int GetRequestedMsaaSamples(VenueAntiAliasingMethod method) => method switch
        {
            VenueAntiAliasingMethod.MSAA2x => 2,
            VenueAntiAliasingMethod.MSAA => 4,
            VenueAntiAliasingMethod.MSAA8x => 8,
            _ => 1,
        };

        public static VenueAntiAliasingMethod GetEffectiveAntialiasing(bool venue)
        {
            var settings = SettingsManager.Settings;
            if (settings == null)
            {
                return venue ? VenueAntiAliasingMethod.None : VenueAntiAliasingMethod.SMAA;
            }
            if (settings.LowQuality.Value)
            {
                return VenueAntiAliasingMethod.None;
            }

            var method = venue ? settings.VenueAntiAliasing.Value : settings.HighwayAntiAliasing.Value;
            if (!venue && method == VenueAntiAliasingMethod.TAA)
            {
                // Highway shaders use custom projection matrices without matching temporal motion vectors.
                method = VenueAntiAliasingMethod.SMAA;
            }
            if (venue && !settings.VenuePostProcessing.Value && GetRequestedMsaaSamples(method) == 1)
            {
                return VenueAntiAliasingMethod.None;
            }
            return method;
        }

        public static int GetPipelineMsaaSamples() => Mathf.Max(
            GetRequestedMsaaSamples(GetEffectiveAntialiasing(false)),
            GetRequestedMsaaSamples(GetEffectiveAntialiasing(true)));

        public static RenderTextureDescriptor ConfigureAntialiasingDescriptor(
            RenderTextureDescriptor descriptor, VenueAntiAliasingMethod method)
        {
            descriptor.msaaSamples = GetRequestedMsaaSamples(method);
            if (descriptor.msaaSamples > 1)
            {
                descriptor.msaaSamples = SystemInfo.supportsStoreAndResolveAction
                    ? Mathf.Max(1, SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor)) : 1;
            }
            descriptor.bindMS = false;
            return descriptor;
        }

        public static bool IsAntialiasingSupported(VenueAntiAliasingMethod method, bool venue)
        {
            if (method == VenueAntiAliasingMethod.TAA)
            {
                return venue;
            }
            int requested = GetRequestedMsaaSamples(method);
            if (requested == 1)
            {
                return true;
            }
            var descriptor = new RenderTextureDescriptor(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height),
                RenderTextureFormat.DefaultHDR, venue ? 16 : 32);
            return ConfigureAntialiasingDescriptor(descriptor, method).msaaSamples == requested;
        }

        public static void ConfigureCameraAntialiasing(Camera camera, VenueAntiAliasingMethod method,
            AntialiasingQuality quality, TemporalAAQuality temporalQuality, bool temporalAllowed)
        {
            if (camera == null)
            {
                return;
            }
            var data = camera.GetUniversalAdditionalCameraData();
            var previousMode = data.antialiasing;
            data.antialiasing = AntialiasingMode.None;
            data.antialiasingQuality = quality;
            data.taaSettings.quality = temporalQuality;
            camera.allowMSAA = GetRequestedMsaaSamples(method) > 1;

            if (data.renderPostProcessing)
            {
                switch (method)
                {
                    case VenueAntiAliasingMethod.FXAA:
                        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                        break;
                    case VenueAntiAliasingMethod.SMAA:
                        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                        break;
                    case VenueAntiAliasingMethod.TAA:
                        bool compatible = temporalAllowed && !camera.allowDynamicResolution &&
                            data.renderType == CameraRenderType.Base &&
                            (data.cameraStack == null || data.cameraStack.Count == 0);
                        data.antialiasing = compatible
                            ? AntialiasingMode.TemporalAntiAliasing
                            : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                        break;
                }
            }
            if (data.antialiasing != previousMode)
            {
                data.resetHistory = true;
            }
        }

        public static void ApplyCameraAntialiasing(Camera camera, bool venue)
        {
            var settings = SettingsManager.Settings;
            ConfigureCameraAntialiasing(camera, GetEffectiveAntialiasing(venue),
                venue ? settings?.VenueAntiAliasingQuality.Value ?? AntialiasingQuality.High
                    : settings?.HighwayAntiAliasingQuality.Value ?? AntialiasingQuality.Low,
                settings?.VenueTemporalAntiAliasingQuality.Value ?? TemporalAAQuality.High, venue);
        }

        public void ApplyAntialiasingSettings()
        {
            foreach (var renderer in FindObjectsByType<HighwayCameraRendering>(FindObjectsSortMode.None))
            {
                renderer.RefreshAntialiasing();
            }
            foreach (var renderer in FindObjectsByType<VenueCameraRenderer>(FindObjectsSortMode.None))
            {
                renderer.RefreshAntialiasing();
            }
            foreach (var positioner in FindObjectsByType<CameraPositioner>(FindObjectsSortMode.None))
            {
                positioner.RefreshAntialiasing();
            }
        }
    }
}
