using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using YARG.Audio;
using YARG.Core;
using YARG.Core.Logging;
using YARG.Core.Audio;

#nullable enable

namespace YARG.Input.Serialization
{
    // These classes are what the bindings will use for serialization/deserialization.
    // They are *not* what will get written to the bindings file in the end, however,
    // since bindings are versioned. These are used to separate the written format and actual loaded format,
    // and to make adding things easier, since each layer of the serialization has its own type.
    //
    // When making changes to the bindings format, create a copy of the current `BindingsVersion.vX.cs` file
    // and make your changes to that. **Do not modify the existing version files!**
    // Next, make changes to the classes below, if needed, e.g. new data needs to be stored/loaded.
    // Finally, update SerializeBindings/DeserializeBindings here to reflect the new version:
    // - Make SerializeBindings serialize to the new version of the format.
    // - Add a new case branch to DeserializeBindings for the new version.

    public class SerializedBindings
    {
        public Dictionary<Guid, SerializedProfileBindings> Profiles = new();
    }

    public class SerializedProfileBindings
    {
        public List<SerializedInputDevice> Devices = new();
        public List<SerializedMic> Microphones = new();

        public Dictionary<GameMode, SerializedBindingCollection> ModeMappings = new();
        public SerializedBindingCollection? MenuMappings;

        /// <summary>
        /// First-microphone accessor preserved for single-mic readers (Vocals, Harmony, Free profiles).
        /// Returns null if no microphones are bound. Setter is intentionally not provided.
        /// </summary>
        [JsonIgnore]
        public SerializedMic? Microphone => Microphones.Count > 0 ? Microphones[0] : null;
    }

    public class SerializedBindingCollection
    {
        public Dictionary<string, SerializedControlBinding> Bindings = new();
    }

    public class SerializedControlBinding
    {
        public Dictionary<string, string> Parameters = new();
        public List<SerializedInputControl> Controls = new();
    }

    public class SerializedInputDevice
    {
        public string Layout;
        public string Hash;

        // SerializedInputDevice has two constructors, so Newtonsoft can't pick one on its
        // own — without this attribute deserialization throws "Unable to find a constructor
        // to use", which aborts loading the ENTIRE bindings file (so all device bindings are
        // silently dropped on every launch). Param names map to the Layout/Hash properties.
        [JsonConstructor]
        public SerializedInputDevice(string layout, string hash)
        {
            Layout = layout;
            Hash = hash;
        }

        public SerializedInputDevice(InputDevice device)
        {
            Layout = device.layout;
            Hash = device.GetHash();
        }

        public bool MatchesDevice(InputDevice device)
        {
            return Layout == device.layout && Hash == device.GetHash();
        }
    }

    public class SerializedInputControl
    {
        public SerializedInputDevice Device;
        public string ControlPath;
        public Dictionary<string, string> Parameters = new();

        public SerializedInputControl(SerializedInputDevice device, string path)
        {
            Device = device;
            ControlPath = path;
        }
    }

    public static partial class BindingSerialization
    {
        private static readonly SHA1 _hashAlgorithm = SHA1.Create();
        private static readonly Regex _xinputUserIndexRegex = new(@"\\""userIndex\\"":\s*\d,");

        private static readonly Dictionary<InputDevice, string> _hashCache = new();

        public static SerializedInputDevice Serialize(this InputDevice device)
        {
            return new(device);
        }

        public static string GetHash(this InputDevice device)
        {
            // Check if we have a calculated hash cached already
            if (_hashCache.TryGetValue(device, out string hash))
                return hash;

            var description = device.description;
            string descriptionJson = description.ToJson();
            // Exclude user index on XInput devices
            if (description.interfaceName == "XInput")
                descriptionJson = _xinputUserIndexRegex.Replace(descriptionJson, "");

            // Calculate the hash
            var descriptionBytes = Encoding.Default.GetBytes(descriptionJson);
            var hashBytes = _hashAlgorithm.ComputeHash(descriptionBytes);
            hash = BitConverter.ToString(hashBytes).Replace("-", "");

            // [KBD-HASH-DIAG] Log the keyboard's description + hash so we can diff it across a
            // restart: if the keyboard device isn't persisting, the description JSON (and thus
            // this hash) is differing between launches. Diagnostic only.
            if (device.layout == "Keyboard")
            {
                YargLogger.LogInfo($"[KBD-HASH-DIAG] Keyboard hash={hash} desc={descriptionJson}");
            }

            // Cache the calculated hash
            _hashCache.Add(device, hash);

            return hash;
        }

        public static void SerializeBindings(SerializedBindings bindings, string bindingsPath)
        {
            try
            {
                var serialized = SerializeBindingsV4(bindings);
                string bindingsJson = JsonConvert.SerializeObject(serialized, Formatting.Indented);
                File.WriteAllText(bindingsPath, bindingsJson);
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "Error while saving bindings!");
            }
        }

        public static SerializedBindings? DeserializeBindings(string bindingsPath)
        {
            try
            {
                if (!File.Exists(bindingsPath))
                    return null;

                string bindingsJson = File.ReadAllText(bindingsPath);
                var jObject = JObject.Parse(bindingsJson);

                int version = jObject["Version"] switch
                {
                    null => 0,
                    { Type: JTokenType.Integer } versionToken => (int) versionToken,
                    {} unhandled => throw new InvalidDataException($"Invalid bindings version! Expected JSON type {JTokenType.Integer}, got {unhandled.Type}")
                };

                var bindings = version switch
                {
                    0 => DeserializeBindingsV0(jObject),
                    1 => DeserializeBindingsV1(jObject),
                    2 => DeserializeBindingsV2(jObject),
                    3 => DeserializeBindingsV3(jObject),
                    4 => DeserializeBindingsV4(jObject),
                    _ => throw new NotImplementedException($"Unhandled bindings version {version}!")
                };

                return bindings;
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "Error while loading bindings!");
                return null;
            }
        }

        private static SerializedBindingsV4 SerializeBindingsV4(SerializedBindings serialized)
        {
            var serializedV4 = new SerializedBindingsV4();
            foreach (var (id, bind) in serialized.Profiles)
            {
                serializedV4.Profiles[id] = new SerializedProfileBindingsV4(bind);
            }
            return serializedV4;
        }

        private static SerializedBindings? DeserializeBindingsV3(JObject obj)
        {
            var serialized = obj.ToObject<SerializedBindingsV3>();
            if (serialized is null || serialized.Version != SerializedBindingsV3.VERSION)
                return null;

            return SerializedBindingsV3.MigrateToCurrent(serialized);
        }

        private static SerializedBindings? DeserializeBindingsV4(JObject obj)
        {
            var serialized = obj.ToObject<SerializedBindingsV4>();
            if (serialized is null || serialized.Version != SerializedBindingsV4.VERSION)
                return null;

            return serialized.Deserialize();
        }
    }

    // Version 4: Convert single Microphone to List<SerializedMic>
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
        public List<SerializedInputDevice> Devices = new();
        public List<SerializedMic> Microphones = new();

        public Dictionary<GameMode, SerializedBindingCollection> ModeMappings = new();
        public SerializedBindingCollection? MenuMappings;

        [JsonConstructor]
        public SerializedProfileBindingsV4() { }

        public SerializedProfileBindingsV4(SerializedProfileBindings serialized)
        {
            Devices.AddRange(serialized.Devices);

            Microphones.AddRange(serialized.Microphones);

            foreach (var (gameMode, bindings) in serialized.ModeMappings)
            {
                ModeMappings[gameMode] = bindings;
            }

            if (serialized.MenuMappings is not null)
                MenuMappings = serialized.MenuMappings;
        }

        public SerializedProfileBindings Deserialize()
        {
            var deserialized = new SerializedProfileBindings();

            deserialized.Devices.AddRange(Devices);

            deserialized.Microphones.AddRange(Microphones);

            foreach (var (gameMode, bindings) in ModeMappings)
            {
                deserialized.ModeMappings[gameMode] = bindings;
            }

            if (MenuMappings is not null)
                deserialized.MenuMappings = MenuMappings;

            return deserialized;
        }
    }
}