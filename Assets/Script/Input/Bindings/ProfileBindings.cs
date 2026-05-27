using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using YARG.Audio;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Input.Serialization;
using YARG.Player;

namespace YARG.Input
{
    public class ProfileBindings : IDisposable
    {
        public YargProfile Profile { get; }

        private const int MICROPHONE_CAP = 7;

        private readonly List<MicDevice> _microphones = new();
        private readonly List<SerializedMic> _unresolvedMics = new();
        public IReadOnlyList<MicDevice> Microphones => _microphones;

        /// <summary>
        /// Maximum number of microphones this profile may bind, derived from its
        /// GameMode. PartyVocals = 7; everything else (Solo Vocals / Harmony) = 1.
        /// Single source of truth — both <see cref="AddMicrophone"/> and any UI that
        /// gates "Add" buttons should consult this rather than re-deriving the cap.
        /// </summary>
        public int MicrophoneCap => Profile.GameMode == GameMode.PartyVocals ? MICROPHONE_CAP : 1;

        /// <summary>
        /// First-microphone accessor preserved for single-mic readers (Vocals, Harmony, Free profiles).
        /// Returns null if no microphones are bound. Setter is intentionally not provided; use AddMicrophone / RemoveMicrophone.
        /// </summary>
        public MicDevice Microphone => _microphones.Count > 0 ? _microphones[0] : null;

        public List<InputDevice> InputDevices => _devices;

        private readonly List<SerializedInputDevice> _unresolvedDevices = new();
        private readonly List<InputDevice> _devices = new();

        private readonly Dictionary<GameMode, BindingCollection> _bindsByGameMode = new();
        public readonly BindingCollection MenuBindings;

        public bool HasDeviceAssigned => _devices.Count > 0;
        public bool Empty => !HasDeviceAssigned && _microphones.Count == 0;

        public BindingCollection this[GameMode mode] => _bindsByGameMode[mode];

        public event Action<InputDevice> DeviceAdded;
        public event Action<InputDevice> DeviceRemoved;

        public event Action BindingsChanged
        {
            add
            {
                foreach (var bindings in _bindsByGameMode.Values)
                {
                    bindings.BindingsChanged += value;
                }

                MenuBindings.BindingsChanged += value;
            }
            remove
            {
                foreach (var bindings in _bindsByGameMode.Values)
                {
                    bindings.BindingsChanged -= value;
                }

                MenuBindings.BindingsChanged -= value;
            }
        }

        public event GameInputProcessed MenuInputProcessed
        {
            add    => MenuBindings.InputProcessed += value;
            remove => MenuBindings.InputProcessed -= value;
        }

        public ProfileBindings(YargProfile profile)
        {
            Profile = profile;

            foreach (var mode in EnumExtensions<GameMode>.Values)
            {
                _bindsByGameMode.Add(mode, BindingCollection.CreateGameplayBindings(mode));
            }

            MenuBindings = BindingCollection.CreateMenuBindings();
        }

#nullable enable
        public ProfileBindings(YargProfile profile, SerializedProfileBindings? bindings)
            : this(profile)
        {
            if (bindings is null)
                return;

            if (bindings.Devices is not null)
            {
                foreach (var device in bindings.Devices)
                {
                    if (device is null || string.IsNullOrEmpty(device.Layout) || string.IsNullOrEmpty(device.Hash))
                    {
                        YargLogger.LogFormatWarning("Encountered invalid device entry in bindings for profile {0}!", profile.Name);
                        continue;
                    }

                    // Devices will be resolved later
                    _unresolvedDevices.Add(device);
                }
            }

            _unresolvedMics.AddRange(bindings.Microphones ?? new List<SerializedMic>());

            if (bindings.ModeMappings is not null)
            {
                foreach (var (mode, serializedBinds) in bindings.ModeMappings)
                {
                    if (!_bindsByGameMode.TryGetValue(mode, out var modeBindings))
                    {
                        YargLogger.LogFormatWarning("Encountered invalid game mode {0} in bindings for profile {1}!", mode, item2: profile.Name);
                        continue;
                    }

                    modeBindings.Deserialize(serializedBinds);
                }
            }

            MenuBindings.Deserialize(bindings.MenuMappings);
        }

        public SerializedProfileBindings Serialize()
        {
            var serialized = new SerializedProfileBindings();

            foreach (var device in _devices)
            {
                serialized.Devices.Add(device.Serialize());
            }

            foreach (var device in _unresolvedDevices)
            {
                serialized.Devices.Add(device);
            }

            serialized.Microphones.AddRange(_unresolvedMics);

            foreach (var (mode, bindings) in _bindsByGameMode)
            {
                var serializedBinds = bindings.Serialize();
                if (serializedBinds is null)
                    continue;

                serialized.ModeMappings.Add(mode, serializedBinds);
            }

            serialized.MenuMappings = MenuBindings.Serialize();

            return serialized;
        }

        public static ProfileBindings Deserialize(YargProfile profile, SerializedProfileBindings? serialized)
        {
            return new(profile, serialized);
        }
#nullable disable

        public void ResolveDevices()
        {
            foreach (var device in InputSystem.devices)
            {
                if (!PlayerContainer.IsDeviceTaken(device))
                    OnDeviceAdded(device);
            }

            // Two-pass mic resolver.
            // Pass 1: exact StableId match. Pass 2: name match against unmatched devices in slot order.

            var remainingUnresolved = _unresolvedMics.ToList();
            var available = GlobalAudioHandler.GetAllInputDevices();

            // Pass 1: StableId exact match (match on string, only create device on hit).
            for (int i = remainingUnresolved.Count - 1; i >= 0; i--)
            {
                var unresolved = remainingUnresolved[i];
                if (string.IsNullOrEmpty(unresolved.StableId)) continue;

                int matchIdx = -1;
                for (int j = 0; j < available.Count; j++)
                {
                    if (MicDevice.ComputeStableId(available[j].id, available[j].name) == unresolved.StableId)
                    {
                        matchIdx = j;
                        break;
                    }
                }

                if (matchIdx >= 0)
                {
                    var (id, name) = available[matchIdx];
                    var device = GlobalAudioHandler.CreateInputDevice(id, name);
                    if (device != null)
                    {
                        var result = TryAddMicrophoneInternal(device);
                        if (result == MicAddResult.Added)
                        {
                            available.RemoveAt(matchIdx);
                            _unresolvedMics.Remove(unresolved);
                            remainingUnresolved.RemoveAt(i);
                        }
                        else
                        {
                            _unresolvedMics.Remove(unresolved);
                            remainingUnresolved.RemoveAt(i);
                        }
                    }
                }
            }

            // Pass 2: Name match against still-available devices, in original slot order.
            foreach (var unresolved in remainingUnresolved.ToList())
            {
                int matchIdx = -1;
                for (int j = 0; j < available.Count; j++)
                {
                    if (available[j].name == unresolved.Name)
                    {
                        matchIdx = j;
                        break;
                    }
                }

                if (matchIdx >= 0)
                {
                    var (id, name) = available[matchIdx];
                    var device = GlobalAudioHandler.CreateInputDevice(id, name);
                    if (device != null)
                    {
                        var result = TryAddMicrophoneInternal(device);
                        if (result == MicAddResult.Added)
                        {
                            available.RemoveAt(matchIdx);
                            _unresolvedMics.Remove(unresolved);
                        }
                        else
                        {
                            _unresolvedMics.Remove(unresolved);
                        }
                    }
                }
            }

            // Pass 3: still-unmatched entries stay in _unresolvedMics for later OnDeviceAdded events.
        }

        public void EnableInputs()
        {
            foreach (var bindings in _bindsByGameMode.Values)
            {
                bindings.EnableInputs();
            }

            MenuBindings.EnableInputs();
        }

        public void DisableInputs()
        {
            foreach (var bindings in _bindsByGameMode.Values)
            {
                bindings.DisableInputs();
            }

            MenuBindings.DisableInputs();
        }

        public void SubscribeToGameplayInputs(GameMode mode, GameInputProcessed onInputProcessed)
        {
            _bindsByGameMode[mode].InputProcessed += onInputProcessed;
        }

        public void UnsubscribeFromGameplayInputs(GameMode mode, GameInputProcessed onInputProcessed)
        {
            _bindsByGameMode[mode].InputProcessed -= onInputProcessed;
        }

        public bool AddDevice(InputDevice device)
        {
            // Ignore already-added devices
            if (ContainsDevice(device))
                return false;

            // Remove corresponding serialized entry
            int index = FindSerializedIndex(device);
            if (index >= 0)
                _unresolvedDevices.RemoveAt(index);

            // Add device to bindings
            _devices.Add(device);
            NotifyDeviceAdded(device);

            return true;
        }

        public bool RemoveDevice(InputDevice device)
        {
            // Remove without serializing
            if (!_devices.Remove(device))
                return false;

            NotifyDeviceRemoved(device);
            return true;
        }

        public bool ContainsDevice(InputDevice device)
        {
            return _devices.Contains(device);
        }

        public List<T> GetDevicesByType<T>()
        {
            var interfaces = new List<T>();
            foreach (var device in _devices)
            {
                if (device is T iface)
                {
                    interfaces.Add(iface);
                }
            }

            return interfaces;
        }

        private int FindSerializedIndex(InputDevice device)
        {
            return _unresolvedDevices.FindIndex((dev) => dev.MatchesDevice(device));
        }

        public bool MatchesDevice(InputDevice device)
        {
            return _unresolvedDevices.Any(dev => dev.MatchesDevice(device));
        }

        public bool ContainsBindingsForDevice(InputDevice device)
        {
            foreach (var bindings in _bindsByGameMode.Values)
            {
                if (bindings.ContainsBindingsForDevice(device))
                    return true;
            }

            return MenuBindings.ContainsBindingsForDevice(device);
        }

        public void ClearBindingsForDevice(InputDevice device, bool clearMenuBindings = true)
        {
            foreach (var bindings in _bindsByGameMode.Values)
            {
                bindings.ClearBindingsForDevice(device);
            }

            if (clearMenuBindings)
            {
                MenuBindings.ClearBindingsForDevice(device);
            }
        }

        public void ClearAllBindings()
        {
            foreach (var bindings in _bindsByGameMode.Values)
            {
                bindings.ClearAllBindings();
            }

            MenuBindings.ClearAllBindings();
        }

        public bool SetDefaultBinds(InputDevice device)
        {
            if (!ContainsDevice(device))
            {
                return false;
            }

            foreach (var bindings in _bindsByGameMode.Values)
            {
                bindings.SetDefaultBindings(device);
            }

            MenuBindings.SetDefaultBindings(device);

            return true;
        }

        public bool SetDefaultBinds(Gamepad gamepad, GamepadBindingMode mode)
        {
            if (!ContainsDevice(gamepad))
            {
                return false;
            }

            foreach (var bindings in _bindsByGameMode.Values)
            {
                bindings.SetDefaultBindings(gamepad, mode);
            }

            MenuBindings.SetDefaultBindings(gamepad, mode);

            return true;
        }

        public void OnDeviceAdded(InputDevice device)
        {
            // Ignore already-added devices
            if (ContainsDevice(device))
                return;

            // Ignore devices not registered to this profile
            int serializedIndex = FindSerializedIndex(device);
            if (serializedIndex < 0)
                return;

            _unresolvedDevices.RemoveAt(serializedIndex);
            _devices.Add(device);
            NotifyDeviceAdded(device);
        }

        public void OnDeviceRemoved(InputDevice device)
        {
            // Ignore devices not registered to this profile
            if (!ContainsDevice(device))
                return;

            // Ensure devices aren't serialized twice
            int serializedIndex = FindSerializedIndex(device);
            if (serializedIndex >= 0)
                return;

            _devices.Remove(device);
            _unresolvedDevices.Add(device.Serialize());
            NotifyDeviceRemoved(device);
        }

        private void NotifyDeviceAdded(InputDevice device)
        {
            foreach (var bindings in _bindsByGameMode.Values)
            {
                bindings.OnDeviceAdded(device);
            }

            MenuBindings.OnDeviceAdded(device);

            DeviceAdded?.Invoke(device);
        }

        private void NotifyDeviceRemoved(InputDevice device)
        {
            foreach (var bindings in _bindsByGameMode.Values)
            {
                bindings.OnDeviceRemoved(device);
            }

            MenuBindings.OnDeviceRemoved(device);

            DeviceRemoved?.Invoke(device);
        }

        public void UpdateBindingsForFrame(double updateTime)
        {
            foreach (var bindings in _bindsByGameMode.Values)
            {
                bindings.UpdateBindingsForFrame(updateTime);
            }

            MenuBindings.UpdateBindingsForFrame(updateTime);
        }

        public bool AddMicrophone(MicDevice microphone)
        {
            return TryAddMicrophoneInternal(microphone) == MicAddResult.Added;
        }

        private enum MicAddResult { Added, CapExceeded, DuplicateId }

        private MicAddResult TryAddMicrophoneInternal(MicDevice microphone)
        {
            if (_microphones.Count >= MicrophoneCap)
            {
                microphone.Dispose();
                return MicAddResult.CapExceeded;
            }

            var stableId = microphone.StableId;
            if (_microphones.Any(m => m.StableId == stableId))
            {
                microphone.Dispose();
                return MicAddResult.DuplicateId;
            }

            _microphones.Add(microphone);
            _unresolvedMics.Add(microphone.Serialize());

            return MicAddResult.Added;
        }

        public bool RemoveMicrophone(MicDevice microphone)
        {
            // Remove by reference equality
            int index = _microphones.IndexOf(microphone);
            if (index >= 0)
            {
                YargLogger.LogFormatDebug("PV-binds RemoveMicrophone profile={0} stableId={1} stack={2}",
                    Profile.Name, microphone.StableId, System.Environment.StackTrace);
                _microphones.RemoveAt(index);
                var micStableId = microphone.StableId;
                // Guard against null match-all: if either side is null (e.g. a stale
                // pre-resolve entry that never got its StableId populated), skip the
                // unresolved-list cleanup rather than purging every null-StableId entry.
                if (!string.IsNullOrEmpty(micStableId))
                {
                    _unresolvedMics.RemoveAll(m => m.StableId == micStableId);
                }
                return true;
            }

            return false;
        }

        public void RemoveAllMicrophones()
        {
            YargLogger.LogFormatDebug("PV-binds RemoveAllMicrophones profile={0} count={1} stack={2}",
                Profile.Name, _microphones.Count, System.Environment.StackTrace);
            foreach (var microphone in _microphones)
            {
                microphone.Dispose();
            }
            _microphones.Clear();
            _unresolvedMics.Clear();
        }

        public void Dispose()
        {
            YargLogger.LogFormatDebug("PV-binds Dispose profile={0} micCount={1} unresolvedCount={2} stack={3}",
                Profile.Name, _microphones.Count, _unresolvedMics.Count, System.Environment.StackTrace);
            foreach (var device in InputSystem.devices)
            {
                OnDeviceRemoved(device);
            }

            foreach (var microphone in _microphones)
            {
                microphone.Dispose();
            }
            _microphones.Clear();
            _unresolvedMics.Clear();
        }
    }
}