// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;
using YARG.Audio;
using YARG.Audio.BASS;
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
        public int MicrophoneCap => Profile.GameMode == GameMode.PartyVocals ? MICROPHONE_CAP : 1;
        private static readonly ConditionalWeakTable<MicDevice, ProfileBindings> _microphoneOwners = new();
        private static readonly object _microphoneOwnerLock = new();
        private readonly List<MicrophoneBinding> _microphoneBindings = new();
        public IReadOnlyList<MicrophoneBinding> MicrophoneBindings => _microphoneBindings.AsReadOnly();
        private readonly List<MicDevice> _microphones = new();

        /// <summary>
        ///     The first microphone, for gameplay that only supports one at a time.
        /// </summary>
        public MicDevice Microphone => _microphones.Count > 0 ? _microphones[0] : null;

        /// <summary>
        ///     Every microphone assigned to this profile.
        /// </summary>
        public List<MicDevice> Microphones => _microphones;

        public List<InputDevice> InputDevices => _devices;

        private readonly List<SerializedInputDevice> _unresolvedDevices = new();
        private readonly List<InputDevice> _devices = new();

        private readonly Dictionary<GameMode, BindingCollection> _bindsByGameMode = new();
        public readonly BindingCollection MenuBindings;

        private readonly List<PhysicalAliasSubscription> _physicalAliasSubscriptions = new();

        private sealed class PhysicalAliasSubscription : IDisposable
        {
            internal readonly GameMode Mode;
            internal readonly GameInputProcessed Callback;
            private readonly List<(ControlBinding Binding, GameInputProcessed Handler)> _handlers = new();
            private readonly ConditionalWeakTable<ControlBinding.PhysicalInputOrigin, HashSet<int>> _seen = new();

            internal PhysicalAliasSubscription(GameMode mode, BindingCollection bindings,
                GameInputProcessed callback, Func<int, int?> aliasGroup)
            {
                Mode = mode;
                Callback = callback;
                foreach (var binding in bindings)
                {
                    void Handle(ref YARG.Core.Input.GameInput input)
                    {
                        int? group = aliasGroup(input.Action);
                        var origin = binding.PhysicalOrigin;
                        if (group.HasValue && origin != null && !_seen.GetOrCreateValue(origin).Add(group.Value))
                            return;
                        callback(ref input);
                    }
                    GameInputProcessed handler = Handle;
                    binding.InputProcessed += handler;
                    _handlers.Add((binding, handler));
                }
            }

            public void Dispose()
            {
                foreach (var (binding, handler) in _handlers) binding.InputProcessed -= handler;
                _handlers.Clear();
            }
        }

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

            if (bindings.Microphones.Count > 0)
            {
                for (int i = 0; i < bindings.Microphones.Count; i++)
                {
                    var mic = bindings.Microphones[i];
                    if (mic is not null)
                    {
                        _microphoneBindings.Add(new MicrophoneBinding(mic,
                            i < bindings.MicrophoneSettings.Count ? bindings.MicrophoneSettings[i] : null));
                    }
                }
            }
            else if (bindings.Microphone is not null)
            {
                // Legacy files (v0-v2) only had a single microphone
                _microphoneBindings.Add(new MicrophoneBinding(bindings.Microphone));
            }

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

            foreach (var binding in _microphoneBindings)
            {
                serialized.Microphones.Add(binding.Identity);
                serialized.MicrophoneSettings.Add(binding.Settings());
            }

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

            ResolveMicrophones();
        }

        public bool ResolveMicrophones()
        {
            if (_microphoneBindings.Count == 0) return false;
            Func<MicDevice, bool> isConnected = device => device is not BassMicDevice bass || bass.IsConnected;
            bool disconnected = DisconnectUnavailableMicrophones(isConnected);
            var available = GlobalAudioHandler.GetAllInputDevices();
            var saved = _microphoneBindings.Select(binding => binding.Identity).ToArray();
            return ResolveMicrophones(mic =>
            {
                var candidate = MicrophoneSetupValues.SelectInput(mic, saved, available);
                if (!candidate.HasValue) return null;
                var device = GlobalAudioHandler.CreateInputDevice(candidate.Value);
                if (device != null) available.Remove(candidate.Value);
                return device;
            }, isConnected) || disconnected;
        }

        internal bool ResolveMicrophones(Func<SerializedMic, MicDevice> resolve, Func<MicDevice, bool> isConnected)
        {
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));
            if (isConnected == null) throw new ArgumentNullException(nameof(isConnected));
            bool changed = DisconnectUnavailableMicrophones(isConnected);
            foreach (var binding in _microphoneBindings)
            {
                if (binding.Device != null || _microphones.Count >= MicrophoneCap) continue;
                var device = resolve(binding.Identity);
                if (device == null) continue;
                if (AttachMicrophone(binding, device))
                {
                    changed = true;
                    RefreshMicrophoneView();
                }
                else
                {
                    lock (_microphoneOwnerLock)
                    {
                        if (!_microphoneOwners.TryGetValue(device, out _)) device.Dispose();
                    }
                }
            }
            RefreshMicrophoneView();
            return changed;
        }

        private bool DisconnectUnavailableMicrophones(Func<MicDevice, bool> isConnected)
        {
            bool changed = false;
            foreach (var binding in _microphoneBindings)
            {
                if (binding.Device == null || isConnected(binding.Device)) continue;
                ReleaseMicrophone(binding);
                changed = true;
            }
            if (changed) RefreshMicrophoneView();
            return changed;
        }

        public void ReleaseMicrophones()
        {
            foreach (var binding in _microphoneBindings) ReleaseMicrophone(binding);
            RefreshMicrophoneView();
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

        /// <summary>Filters only aliases emitted by the same physical control event,
        /// before the gameplay callback records replay inputs. Raw diagnostics stay untouched.</summary>
        public void SubscribeToGameplayInputsWithPhysicalAliases(GameMode mode,
            GameInputProcessed onInputProcessed, Func<int, int?> aliasGroup)
        {
            if (onInputProcessed == null) throw new ArgumentNullException(nameof(onInputProcessed));
            if (aliasGroup == null) throw new ArgumentNullException(nameof(aliasGroup));
            _physicalAliasSubscriptions.Add(new PhysicalAliasSubscription(mode, _bindsByGameMode[mode],
                onInputProcessed, aliasGroup));
        }

        public void UnsubscribeFromGameplayInputs(GameMode mode, GameInputProcessed onInputProcessed)
        {
            // Match ordinary event semantics: remove the last matching subscription.
            for (int i = _physicalAliasSubscriptions.Count - 1; i >= 0; i--)
            {
                var subscription = _physicalAliasSubscriptions[i];
                if (subscription.Mode != mode || subscription.Callback != onInputProcessed) continue;
                subscription.Dispose();
                _physicalAliasSubscriptions.RemoveAt(i);
                return;
            }
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
            if (microphone == null) return false;
            var serialized = microphone.Serialize();
            if (_microphones.Count >= MicrophoneCap || _microphones.Any(m =>
                m.StableId == microphone.StableId && m.Serialize().Channel == serialized.Channel)) return false;
            var binding = _microphoneBindings.FirstOrDefault(row => row.Device == null &&
                row.Identity.Channel == serialized.Channel && row.Identity.StableId == serialized.StableId &&
                !string.IsNullOrEmpty(serialized.StableId));
            binding ??= _microphoneBindings.FirstOrDefault(row => row.Device == null &&
                row.Identity.Channel == serialized.Channel && string.IsNullOrEmpty(row.Identity.StableId) &&
                row.Identity.BaseName == serialized.BaseName);
            bool newBinding = binding == null;
            if (newBinding && _microphoneBindings.Count >= MicrophoneCap) return false;
            binding ??= new MicrophoneBinding(serialized);
            if (!AttachMicrophone(binding, microphone)) return false;
            if (newBinding) _microphoneBindings.Add(binding);
            RefreshMicrophoneView();
            return true;
        }

        public bool RemoveMicrophone(MicDevice microphone)
        {
            int index = _microphoneBindings.FindIndex(binding => ReferenceEquals(binding.Device, microphone));
            if (index < 0) return false;
            RemoveMicrophoneAt(index);
            return true;
        }

        public void RemoveMicrophoneAt(int index)
        {
            if (index < 0 || index >= _microphoneBindings.Count) return;
            ReleaseMicrophone(_microphoneBindings[index]);
            _microphoneBindings.RemoveAt(index);
            RefreshMicrophoneView();
        }

        public void MoveMicrophone(int from, int to)
        {
            if (from < 0 || to < 0 || from >= _microphoneBindings.Count || to >= _microphoneBindings.Count) return;
            var binding = _microphoneBindings[from];
            _microphoneBindings.RemoveAt(from);
            _microphoneBindings.Insert(to, binding);
            RefreshMicrophoneView();
        }

        public int GetMicrophoneNeedleId(int index)
        {
            if (index < 0 || index >= _microphoneBindings.Count) return 1;
            return MicrophoneSetupValues.ResolveNeedles(_microphoneBindings.Select(b => b.NeedleId).ToArray())[index];
        }

        public int GetMicrophoneNeedleIdForDevice(MicDevice device, int? automaticFallback = null)
        {
            int index = _microphoneBindings.FindIndex(binding => ReferenceEquals(binding.Device, device));
            if (index < 0) return automaticFallback ?? 1;
            if (_microphoneBindings[index].NeedleId == 0 && automaticFallback.HasValue) return automaticFallback.Value;
            return GetMicrophoneNeedleId(index);
        }

        public void SetMicrophoneNeedle(int index, int needleId)
        {
            if (index < 0 || index >= _microphoneBindings.Count) return;
            _microphoneBindings[index].NeedleId = MicrophoneSetupValues.NormalizeNeedle(needleId);
        }

        public void SetMicrophoneSensitivity(int index, float offset)
        {
            if (index < 0 || index >= _microphoneBindings.Count) return;
            var binding = _microphoneBindings[index];
            binding.SensitivityOffsetDb = MicrophoneSetupValues.NormalizeSensitivity(offset);
            lock (_microphoneOwnerLock)
            {
                if (binding.Device is BassMicDevice device && _microphoneOwners.TryGetValue(device, out var owner) && ReferenceEquals(owner, this))
                    device.SensitivityOffsetDb = binding.SensitivityOffsetDb;
            }
        }

        private bool AttachMicrophone(MicrophoneBinding binding, MicDevice device)
        {
            lock (_microphoneOwnerLock)
            {
                if (_microphoneOwners.TryGetValue(device, out _)) return false;
                if (_microphones.Any(existing => existing.StableId == device.StableId && existing.Serialize().Channel == device.Serialize().Channel)) return false;
                _microphoneOwners.Add(device, this);
                binding.Device = device;
                binding.Identity = device.Serialize();
                if (device is BassMicDevice bass) bass.SensitivityOffsetDb = binding.SensitivityOffsetDb;
            }
            return true;
        }

        private void ReleaseMicrophone(MicrophoneBinding binding)
        {
            var device = binding.Device;
            binding.Device = null;
            if (device == null) return;
            lock (_microphoneOwnerLock)
            {
                if (_microphoneOwners.TryGetValue(device, out var owner) && ReferenceEquals(owner, this))
                {
                    _microphoneOwners.Remove(device);
                    device.Dispose();
                }
            }
        }

        private void RefreshMicrophoneView()
        {
            _microphones.Clear();
            foreach (var binding in _microphoneBindings)
                if (binding.Device != null) _microphones.Add(binding.Device);
        }

        public void RemoveAllMicrophones()
        {
            ReleaseMicrophones();
            _microphoneBindings.Clear();
        }

        public void Dispose()
        {
            foreach (var subscription in _physicalAliasSubscriptions) subscription.Dispose();
            _physicalAliasSubscriptions.Clear();
            foreach (var device in InputSystem.devices)
            {
                OnDeviceRemoved(device);
            }

            ReleaseMicrophones();
        }
    }
}
