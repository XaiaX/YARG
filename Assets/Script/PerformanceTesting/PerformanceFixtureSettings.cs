// pattern: Imperative Shell
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using YARG.Settings;
using YARG.Settings.Types;

namespace YARG.PerformanceTesting
{
    /// <summary>Applies temporary fixture preferences through the normal setting callbacks.</summary>
    public static class PerformanceFixtureSettings
    {
        // Explicitly scoped to rendering and diagnostics. Never expose arbitrary settings to fixtures.
        private static readonly string[] ALLOWED_NAMES =
        {
            "VSync", "FpsCap", "VenueFpsCap", "BackgroundFpsCap", "FullscreenMode",
            "LowQuality", "FullResolutionTextures", "AnisotropicFiltering", "DetailedModels",
            "FullSkinningQuality", "AmbientOcclusion", "HighQualityShadows", "SoftShadows",
            "AdditionalLightShadows", "HighQualityLighting", "EnhancedReflections",
            "VenueRenderingQuality", "VenuePostProcessing", "HighwayAntiAliasing",
            "HighwayAntiAliasingQuality", "VenueAntiAliasing", "VenueAntiAliasingQuality",
            "VenueTemporalAntiAliasingQuality", "DisableBloom", "DisableFilmGrain",
            "ReduceFlashingLights", "StarPowerHighwayFx", "SongBackgroundOpacity",
            "StaticVocalsMode", "EnableTrackEffects", "EnableHighwayAnimation",
            "KickBounceMultiplier", "HighwayTiltMultiplier", "FpsStats", "FrameGraph",
            "TimingDiagnostics", "TargetedPerformanceChecks"
        };

        private static readonly HashSet<string> ALLOWED = new(ALLOWED_NAMES, StringComparer.Ordinal);

        public static void Apply(JObject requested) => ApplyTo(requested, CurrentSettings());

        /// <summary>
        /// Reads current retained preferences, including ones temporarily overridden by LowQuality.
        /// Effective hardware/render-pipeline state belongs in the fixture's runtime manifest.
        /// </summary>
        public static JObject Snapshot()
        {
            var result = new JObject();
            foreach (var pair in CurrentSettings())
            {
                var value = pair.Value.ValueAsObject;
                result[pair.Key] = value is Enum ? new JValue(value.ToString()) : JToken.FromObject(value);
            }
            return result;
        }

        /// <summary>Validates the complete request without changing any setting or invoking callbacks.</summary>
        public static IReadOnlyDictionary<string, object> Validate(JObject requested,
            IReadOnlyDictionary<string, ISettingType> available)
        {
            if (requested == null) throw new ArgumentNullException(nameof(requested));
            if (available == null) throw new ArgumentNullException(nameof(available));
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var property in requested.Properties())
            {
                if (!ALLOWED.Contains(property.Name))
                    throw Invalid(property.Name, "is not an allowed graphics or diagnostic setting");
                if (!available.TryGetValue(property.Name, out var setting) || setting == null)
                    throw Invalid(property.Name, "is unavailable in this player");
                values.Add(property.Name, ValidateValue(property.Name, property.Value, setting));
            }
            return values;
        }

        /// <summary>Testable application boundary; invalid batches produce no partial changes.</summary>
        public static void ApplyTo(JObject requested, IReadOnlyDictionary<string, ISettingType> available)
        {
            var values = Validate(requested, available);
            foreach (var name in ALLOWED_NAMES)
            {
                if (values.TryGetValue(name, out var value))
                {
                    // ValueAsObject uses the normal Value setter, which invokes the callback even
                    // for an unchanged value. Calling ForceInvokeCallback as well would invoke twice.
                    available[name].ValueAsObject = value;
                }
            }
        }

        private static Dictionary<string, ISettingType> CurrentSettings()
        {
            var container = SettingsManager.Settings;
            if (container == null) throw new InvalidOperationException("Settings have not been initialized.");
            var result = new Dictionary<string, ISettingType>(StringComparer.Ordinal);
            foreach (var name in ALLOWED_NAMES)
            {
                if (container.GetType().GetProperty(name)?.GetValue(container) is not ISettingType setting)
                    throw new InvalidOperationException($"Fixture setting '{name}' is missing from the settings container.");
                result.Add(name, setting);
            }
            return result;
        }

        private static object ValidateValue(string name, JToken token, ISettingType setting)
        {
            if (setting is ToggleSetting && token.Type == JTokenType.Boolean)
                return token.Value<bool>();

            if (setting is IntSetting integer && token.Type == JTokenType.Integer)
            {
                try
                {
                    var value = token.Value<long>();
                    if (value >= integer.Min && value <= integer.Max) return (int) value;
                }
                catch (Exception exception) when (exception is OverflowException || exception is InvalidCastException)
                {
                    throw Invalid(name, "must be an integer within its supported range");
                }
                throw Invalid(name, $"must be between {integer.Min} and {integer.Max}");
            }

            if (setting is SliderSetting slider &&
                (token.Type == JTokenType.Integer || token.Type == JTokenType.Float))
            {
                double value;
                try
                {
                    value = token.Value<double>();
                }
                catch (Exception exception) when (exception is OverflowException || exception is InvalidCastException)
                {
                    throw Invalid(name, "must be a finite number within its supported range");
                }
                if (!double.IsNaN(value) && !double.IsInfinity(value) &&
                    value >= slider.Min && value <= slider.Max && value >= -float.MaxValue && value <= float.MaxValue)
                    return (float) value;
                throw Invalid(name, $"must be a finite number between {slider.Min} and {slider.Max}");
            }

            if (setting is IDropdownSetting && setting is IEnumerable options && setting.ValueType.IsEnum &&
                token.Type == JTokenType.String)
            {
                var nameValue = token.Value<string>();
                if (Enum.TryParse(setting.ValueType, nameValue, false, out var value) &&
                    Enum.IsDefined(setting.ValueType, value) && Enum.GetName(setting.ValueType, value) == nameValue)
                {
                    foreach (var option in options)
                    {
                        if (Equals(option, value)) return value;
                    }
                }
                throw Invalid(name, "must name an available option on this hardware");
            }

            throw Invalid(name, $"requires a JSON value of its declared type ({setting.ValueType.Name})");
        }

        private static ArgumentException Invalid(string name, string reason) =>
            new($"Fixture setting '{name}' {reason}.");
    }
}
