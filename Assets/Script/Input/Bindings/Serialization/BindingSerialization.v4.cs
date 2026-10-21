using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Logging;

#nullable enable

namespace YARG.Input.Serialization
{
    // Version 4: Stores a needle appearance and sensitivity offset alongside each ordered microphone binding.

    // Unchanged data types
    using SerializedInputDeviceV4 = SerializedInputDeviceV0;

    public class SerializedBindingsV4
    {
        public const int VERSION = 4;

        public int Version = VERSION;
        public Dictionary<Guid, SerializedProfileBindingsV4> Profiles = new();

        [JsonConstructor]
        public SerializedBindingsV4() { }

        public SerializedBindingsV4(SerializedBindings serialized)
        {
            foreach (var (id, bind) in serialized.Profiles)
            {
                Profiles[id] = new SerializedProfileBindingsV4(bind);
            }
        }

        public SerializedBindings Deserialize()
        {
            var deserialized = new SerializedBindings();
            foreach (var (id, bind) in Profiles)
            {
                deserialized.Profiles[id] = bind.Deserialize();
            }

            return deserialized;
        }
    }

    public class SerializedProfileBindingsV4
    {
        public List<SerializedInputDeviceV4> Devices = new();
        public List<SerializedMicV4> Microphones = new();

        public Dictionary<GameMode, SerializedBindingCollectionV4> ModeMappings = new();
        public SerializedBindingCollectionV4? MenuMappings;

        [JsonConstructor]
        public SerializedProfileBindingsV4() { }

        public SerializedProfileBindingsV4(SerializedProfileBindings serialized)
        {
            Devices.AddRange(serialized.Devices.Select((device) => new SerializedInputDeviceV4(device)));

            for (int i = 0; i < serialized.Microphones.Count; i++)
            {
                var mic = serialized.Microphones[i];
                if (mic is not null)
                {
                    int needleId = i < serialized.MicrophoneNeedleIds.Count ? serialized.MicrophoneNeedleIds[i] : 0;
                    float offset = i < serialized.MicrophoneSensitivityOffsets.Count
                        ? serialized.MicrophoneSensitivityOffsets[i] : 0f;
                    Microphones.Add(new SerializedMicV4(mic, needleId, offset));
                }
            }

            foreach (var (gameMode, bindings) in serialized.ModeMappings)
            {
                ModeMappings[gameMode] = new SerializedBindingCollectionV4(this, bindings);
            }

            if (serialized.MenuMappings is not null)
                MenuMappings = new SerializedBindingCollectionV4(this, serialized.MenuMappings);
        }

        public SerializedProfileBindings Deserialize()
        {
            var deserialized = new SerializedProfileBindings();

            foreach (var mic in Microphones)
            {
                if (mic is not null)
                {
                    deserialized.Microphones.Add(mic.Deserialize());
                    deserialized.MicrophoneNeedleIds.Add(mic.ValidNeedleId);
                    deserialized.MicrophoneSensitivityOffsets.Add(mic.ValidSensitivityOffsetDb);
                }
            }

            deserialized.Devices.AddRange(Devices.Select((device) => device.Deserialize()));

            foreach (var (gameMode, bindings) in ModeMappings)
            {
                deserialized.ModeMappings[gameMode] = bindings.Deserialize(this);
            }

            if (MenuMappings is not null)
                deserialized.MenuMappings = MenuMappings.Deserialize(this);

            return deserialized;
        }
    }

    public class SerializedBindingCollectionV4
    {
        public Dictionary<string, SerializedControlBindingV4> Bindings = new();

        [JsonConstructor]
        public SerializedBindingCollectionV4() { }

        public SerializedBindingCollectionV4(SerializedProfileBindingsV4 binds, SerializedBindingCollection serialized)
        {
            foreach (var (id, serializedBinds) in serialized.Bindings)
            {
                Bindings[id] = new SerializedControlBindingV4(binds, serializedBinds);
            }
        }

        public SerializedBindingCollection Deserialize(SerializedProfileBindingsV4 binds)
        {
            var converted = new SerializedBindingCollection();
            foreach (var (id, serializedBinds) in Bindings)
            {
                converted.Bindings[id] = serializedBinds.Deserialize(binds);
            }

            return converted;
        }
    }

    public class SerializedControlBindingV4
    {
        public Dictionary<string, string> Parameters = new();
        public List<SerializedInputControlV4> Controls = new();

        [JsonConstructor]
        public SerializedControlBindingV4() { }

        public SerializedControlBindingV4(SerializedProfileBindingsV4 binds, SerializedControlBinding serialized)
        {
            foreach (var (name, value) in serialized.Parameters)
            {
                Parameters.Add(name, value);
            }

            Controls.AddRange(serialized.Controls.Select((bind) => new SerializedInputControlV4(binds, bind)));
        }

        public SerializedControlBinding Deserialize(SerializedProfileBindingsV4 binds)
        {
            var control = new SerializedControlBinding();

            foreach (var (name, value) in Parameters)
            {
                control.Parameters.Add(name, value);
            }

            foreach (var bind in Controls)
            {
                var deserialized = bind.Deserialize(binds);
                if (deserialized is null)
                    continue;

                control.Controls.Add(deserialized);
            }

            return control;
        }

        public bool ShouldSerializeParameters() => Parameters.Count > 0;
    }

    public class SerializedInputControlV4
    {
        public int DeviceIndex = -1;
        public SerializedInputDeviceV4? Device;

        public string ControlPath;
        public Dictionary<string, string> Parameters = new();

        [JsonConstructor]
        public SerializedInputControlV4()
        {
            ControlPath = string.Empty;
        }

        public SerializedInputControlV4(SerializedProfileBindingsV4 binds, SerializedInputControl serialized)
        {
            int deviceIndex = binds.Devices.FindIndex(
                (device) => device.Layout == serialized.Device.Layout && device.Hash == serialized.Device.Hash);
            if (deviceIndex < 0)
                Device = new(serialized.Device);
            else
                DeviceIndex = deviceIndex;

            ControlPath = serialized.ControlPath;
            Parameters = serialized.Parameters;
        }

        public SerializedInputControl? Deserialize(SerializedProfileBindingsV4 binds)
        {
            if (DeviceIndex >= 0)
            {
                if (DeviceIndex >= binds.Devices.Count)
                {
                    YargLogger.LogFormatWarning("Device at list index {0} is not present!", DeviceIndex);
                    return null;
                }

                Device = binds.Devices[DeviceIndex];
            }
            else if (Device is null)
            {
                YargLogger.LogFormatWarning("No device specified for binding '{0}'!", ControlPath);
                return null;
            }

            return new(Device.Deserialize(), ControlPath)
            {
                Parameters = Parameters,
            };
        }

        // For conditional serialization
        public bool ShouldSerializeDeviceIndex() => DeviceIndex >= 0;
        public bool ShouldSerializeDevice() => !ShouldSerializeDeviceIndex();
        public bool ShouldSerializeParameters() => Parameters.Count > 0;
    }

    public class SerializedMicV4
    {
        public string BaseName;
        public int Channel;
        public int NeedleId;
        public float SensitivityOffsetDb;

        [JsonIgnore]
        public float ValidSensitivityOffsetDb => float.IsNaN(SensitivityOffsetDb) || float.IsInfinity(SensitivityOffsetDb)
            ? 0f : Math.Clamp(SensitivityOffsetDb, -50f, 50f);

        [JsonIgnore]
        public int ValidNeedleId => NeedleId >= 1 && NeedleId <= 7 ? NeedleId : 0;

        public string DisplayName;

        [JsonConstructor]
        public SerializedMicV4()
        {
            BaseName = string.Empty;
            DisplayName = string.Empty;
        }

        public SerializedMicV4(SerializedMic serialized, int needleId, float sensitivityOffsetDb = 0f)
        {
            BaseName = serialized.BaseName;
            Channel = serialized.Channel;
            SensitivityOffsetDb = sensitivityOffsetDb;
            SensitivityOffsetDb = ValidSensitivityOffsetDb;
            NeedleId = needleId >= 1 && needleId <= 7 ? needleId : 0;
            DisplayName = string.Empty;
        }

        public SerializedMic Deserialize()
        {
            if (!string.IsNullOrEmpty(BaseName))
            {
                return new SerializedMic(BaseName, Channel);
            }

            if (!string.IsNullOrEmpty(DisplayName))
            {
                if (InputDeviceInfo.TryParseDisplayName(DisplayName, out var parsedBaseName, out var parsedChannel))
                {
                    return new SerializedMic(parsedBaseName, parsedChannel);
                }

                return new SerializedMic(DisplayName, 0);
            }

            return new SerializedMic(BaseName ?? string.Empty, Channel);
        }

        public bool ShouldSerializeDisplayName() => string.IsNullOrEmpty(BaseName);
        public bool ShouldSerializeBaseName() => !string.IsNullOrEmpty(BaseName);
        public bool ShouldSerializeChannel() => !string.IsNullOrEmpty(BaseName);
    }

    public static partial class BindingSerialization
    {
        private static SerializedBindingsV4 SerializeBindingsV4(SerializedBindings serialized)
        {
            return new SerializedBindingsV4(serialized);
        }

        private static SerializedBindings? DeserializeBindingsV4(JObject obj)
        {
            var serialized = obj.ToObject<SerializedBindingsV4>();
            if (serialized is null || serialized.Version != SerializedBindingsV4.VERSION)
                return null;

            return serialized.Deserialize();
        }
    }
}
