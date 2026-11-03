// pattern: Functional Core
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

#nullable enable

namespace YARG.Input.Serialization
{
    /// <summary>Version four adds microphone-only preferences without changing profiles or replay input.</summary>
    public sealed class SerializedBindingsV4
    {
        public const int VERSION = 4;
        public int Version = VERSION;
        public Dictionary<Guid, SerializedProfileBindingsV4> Profiles = new();

        [JsonConstructor]
        public SerializedBindingsV4() { }

        public SerializedBindingsV4(SerializedBindings source)
        {
            foreach (var (id, bindings) in source.Profiles) Profiles.Add(id, new SerializedProfileBindingsV4(bindings));
        }

        public SerializedBindings Deserialize()
        {
            var result = new SerializedBindings();
            foreach (var (id, bindings) in Profiles)
                if (bindings != null) result.Profiles.Add(id, bindings.Deserialize());
            return result;
        }
    }

    // Retain the established version-three device/control conversion unchanged.
    public sealed class SerializedProfileBindingsV4 : SerializedProfileBindingsV3
    {
        public List<SerializedMicrophoneSettingsV4>? MicrophoneSettings = new();

        [JsonConstructor]
        public SerializedProfileBindingsV4() { }

        public SerializedProfileBindingsV4(SerializedProfileBindings source) : base(source)
        {
            for (int i = 0; i < source.Microphones.Count; i++)
            {
                var settings = i < source.MicrophoneSettings.Count ? source.MicrophoneSettings[i] : null;
                MicrophoneSettings!.Add(new SerializedMicrophoneSettingsV4
                {
                    NeedleId = MicrophoneSetupValues.NormalizeNeedle(settings?.NeedleId ?? 0),
                    SensitivityOffsetDb = MicrophoneSetupValues.NormalizeSensitivity(settings?.SensitivityOffsetDb ?? 0f),
                });
            }
        }

        public new SerializedProfileBindings Deserialize()
        {
            var result = base.Deserialize();
            for (int i = 0; i < result.Microphones.Count; i++)
            {
                var settings = MicrophoneSettings != null && i < MicrophoneSettings.Count ? MicrophoneSettings[i] : null;
                result.MicrophoneSettings.Add(new MicrophoneBindingSettings(settings?.NeedleId ?? 0, settings?.SensitivityOffsetDb ?? 0f));
            }
            return result;
        }
    }

    public sealed class SerializedMicrophoneSettingsV4
    {
        public int NeedleId;
        public float SensitivityOffsetDb;
    }
}
