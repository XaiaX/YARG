// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Minis;
using UnityEngine.InputSystem;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Input.Diagnostics;
using YARG.Helpers;
using YARG.Localization;
using YARG.Player;

namespace YARG.Input.Diagnostics
{
    /// <summary>Read-only, profile-scoped bridge from physical MIDI receipt to the test screen.</summary>
    public sealed class MidiTestCapture : IDisposable
    {
        private readonly object _sync = new();
        private readonly ProfileBindings _bindings;
        private readonly MidiTestSession _session = new(MidiTestConfiguration.Empty, 200);
        private readonly long _clockOrigin = Stopwatch.GetTimestamp();
        private MidiTestLogWriter _writer;
        private string _logPath;
        private string _captureError;
        private string _recordingError;
        private long _completedWriterDrops;
        private long _captureDrops;
        private bool _disposed;

        public YargProfile Profile => _bindings.Profile;
        public string LogDirectory { get; }
        public MidiTestConfiguration Configuration { get { lock (_sync) return _session.Configuration; } }
        public int ConfiguredDeviceCount { get { lock (_sync) return _session.Configuration.Endpoints.Count; } }
        public MidiTestRecord[] History { get { lock (_sync) return _session.History; } }
        public MidiTestRecord[] RecordHistory => History;
        public MidiTestRecord? LatestNote { get { lock (_sync) return _session.LatestNote; } }
        public MidiTestRecord? LatestRecord { get { lock (_sync) return _session.LatestRecord; } }
        public MidiTestRecord? LastControlChange { get { lock (_sync) return _session.LastControlChange; } }
        public MidiTestAddress? CurrentPedalAddress { get { lock (_sync) return _session.PedalController; } }
        public int? CurrentPedalValue { get { lock (_sync) return _session.CurrentPedalValue; } }
        public bool? CurrentPedalClosed { get { lock (_sync) return _session.CurrentPedalClosed; } }
        public MidiTestAddress[] ObservedControllers { get { lock (_sync) return _session.ObservedControllers; } }
        public bool IsRecording { get { lock (_sync) return _writer?.IsRecording == true; } }
        public string LogPath { get { lock (_sync) return _logPath; } }
        public string Error { get { lock (_sync) return _captureError ?? _writer?.Error ?? _recordingError; } }
        public long DroppedRecords
        {
            get { lock (_sync) return _captureDrops + _completedWriterDrops + (_writer?.DroppedRecords ?? 0); }
        }

        public MidiTestCapture(ProfileBindings bindings)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            if (string.IsNullOrEmpty(PathHelper.PersistentDataPath))
                throw new InvalidOperationException("YARG's data directory has not been initialized");
            LogDirectory = Path.Combine(PathHelper.PersistentDataPath, "midi-tests");
            // Inactive profiles are not forwarded device notifications by PlayerContainer.
            // Resolve their existing assignments without joining a player or enabling inputs.
            foreach (var device in InputSystem.devices.OfType<MidiDevice>()) ResolveAssignedDevice(device);
            RefreshConfiguration();
            _bindings.DeviceAdded += OnAssignedDeviceChanged;
            _bindings.DeviceRemoved += OnAssignedDeviceChanged;
            _bindings.BindingsChanged += RefreshConfiguration;
            InputManager.DeviceAdded += OnDeviceAdded;
            InputManager.DeviceRemoved += OnDeviceRemoved;
            MidiDevice.SourceReady += OnSourceReady;
            RawMidiEvents.DetailedMessageReceived += OnMessage;
            RawMidiEvents.DetailedMessagesDropped += OnStagingDropped;
        }

        public void SelectPedal(MidiTestAddress? address)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _session.PedalController = address;
                _writer?.TryEnqueue(new { type = "pedal_selection", controller = address });
            }
        }

        public void ClearHistory()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _session.ClearHistory();
            }
        }

        public string SetRecording(bool recording)
        {
            if (!recording) return StopRecording();
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_writer?.IsRecording == true) return _logPath;
            }
            StopRecording();
            try
            {
                Directory.CreateDirectory(LogDirectory);
                string path = Path.Combine(LogDirectory,
                    $"midi-test-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Profile.Id:N}-{Guid.NewGuid():N}.jsonl");
                object metadata;
                lock (_sync) metadata = new {
                    type = "metadata", format_version = 2, started_utc = DateTime.UtcNow,
                    message_kind_names = Enum.GetNames(typeof(MidiTestMessageKind)),
                    message_data = "DataLength excludes status; Data2 is unused zero padding for channel aftertouch. Value is velocity, CC value or pressure according to Kind; pressure is not a calibrated pedal position.",
                    profile_id = Profile.Id, profile_name = Profile.Name, game_mode = Profile.GameMode.ToString(),
                    configuration = _session.Configuration, pedal_controller = _session.PedalController,
                    clock = "ReceiptMilliseconds: monotonic milliseconds since this screen opened; DriverDeltaSeconds: RtMidi per-port delta since its previous message, including messages outside this capture. Channels are zero-based; CC values are position/controller levels, not velocity."
                };
                var writer = new MidiTestLogWriter(path, metadata);
                lock (_sync)
                {
                    _writer = writer;
                    _logPath = path;
                    _recordingError = null;
                }
            }
            catch (Exception exception)
            {
                lock (_sync) _recordingError = "failed to start MIDI test recording: " + exception.Message;
            }
            return LogPath;
        }

        private string StopRecording()
        {
            MidiTestLogWriter writer;
            lock (_sync)
            {
                writer = _writer;
                _writer = null;
            }
            // Disk I/O and joining the worker are never done while holding the capture
            // lock; physical callbacks may continue updating the screen during the drain.
            if (writer != null)
            {
                writer.Stop();
                lock (_sync)
                {
                    _completedWriterDrops += writer.DroppedRecords;
                    _recordingError = writer.Error;
                }
            }
            return LogPath;
        }

        private void OnMessage(RawMidiMessage message)
        {
            // This callback may run on RtMidi's thread. Every value it uses is managed and
            // immutable; never access Unity devices, profiles, bindings, or localization here.
            lock (_sync)
            {
                if (_disposed) return;
                try
                {
                    double elapsed = (message.ReceivedTimestamp - _clockOrigin) * 1000d / Stopwatch.Frequency;
                    // A connection may finish staging after one screen closes and another
                    // opens. Never inherit physical messages from the previous session.
                    if (elapsed < 0) return;
                    var record = _session.Receive(new MidiTestMessage(message.PortId, message.PortName,
                        elapsed, message.DriverDeltaSeconds, message.Status, message.Data1, message.Data2));
                    if (record.HasValue)
                        _writer?.TryEnqueue(new { type = "event", record = record.Value });
                }
                catch (Exception exception)
                {
                    _captureDrops++;
                    _captureError = "failed to collect MIDI test message: " + exception.Message;
                }
            }
        }

        private void OnStagingDropped(long portId, int channel, long count)
        {
            lock (_sync)
            {
                if (_disposed || !_session.Configuration.Endpoints.Any(endpoint => endpoint.PortId == portId &&
                    (endpoint.Channel == -1 || endpoint.Channel == channel))) return;
                _captureDrops += count;
                _writer?.TryEnqueue(new { type = "source_staging_overflow", port_id = portId, channel, count });
            }
        }

        private void ResolveAssignedDevice(InputDevice device)
        {
            if (device is MidiDevice && (_bindings.ContainsDevice(device) || !PlayerContainer.IsDeviceTaken(device)))
                _bindings.OnDeviceAdded(device);
        }

        private void OnDeviceAdded(InputDevice device)
        {
            ResolveAssignedDevice(device);
            RefreshConfiguration();
        }

        private void OnDeviceRemoved(InputDevice device)
        {
            if (device is MidiDevice) _bindings.OnDeviceRemoved(device);
            RefreshConfiguration();
        }

        private void OnAssignedDeviceChanged(InputDevice device) => RefreshConfiguration();
        private void OnSourceReady(MidiDevice device) => OnDeviceAdded(device);

        private void RefreshConfiguration()
        {
            // Only main-thread notifications reach this method. Copy everything that a
            // callback needs before publishing the configuration under the capture lock.
            try
            {
                var devices = _bindings.InputDevices.OfType<MidiDevice>()
                    .Where(device => device.added && device.SourcePortId > 0).ToArray();
                var endpoints = devices.Select(device => new MidiTestEndpoint(device.SourcePortId,
                    device.SourcePortName, device.channel)).ToArray();
                var mappings = new List<MidiTestBinding>();
                foreach (var binding in _bindings[Profile.GameMode])
                {
                    if (binding is not ButtonBinding buttons) continue;
                    string name = Localize.Key("Bindings", Profile.LeftyFlip ? binding.NameLefty : binding.Name);
                    foreach (var single in buttons.Bindings)
                    {
                        if (single.Control.device is not MidiDevice device || !devices.Contains(device)) continue;
                        bool controller = single.Control is MidiValueControl;
                        int number = single.Control is MidiNoteControl note ? note.noteNumber :
                            single.Control is MidiValueControl cc ? cc.controlNumber : -1;
                        if (number < 0) continue;
                        mappings.Add(new MidiTestBinding(device.SourcePortId, device.channel, controller, number,
                            binding.Key, name, binding.Action,
                            controller && Profile.GameMode == GameMode.EliteDrums &&
                            binding.Action == (int)EliteDrumsAction.EliteStomp, single.Inverted, single.PressPoint));
                    }
                }
                var configuration = new MidiTestConfiguration(endpoints, mappings);
                lock (_sync)
                {
                    if (_disposed) return;
                    _session.Configuration = configuration;
                    _captureError = null;
                    _writer?.TryEnqueue(new { type = "configuration", configuration });
                }
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _session.Configuration = MidiTestConfiguration.Empty;
                    _captureError = "failed to resolve this profile's MIDI assignments: " + exception.Message;
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MidiTestCapture));
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            RawMidiEvents.DetailedMessageReceived -= OnMessage;
            RawMidiEvents.DetailedMessagesDropped -= OnStagingDropped;
            InputManager.DeviceAdded -= OnDeviceAdded;
            InputManager.DeviceRemoved -= OnDeviceRemoved;
            MidiDevice.SourceReady -= OnSourceReady;
            _bindings.DeviceAdded -= OnAssignedDeviceChanged;
            _bindings.DeviceRemoved -= OnAssignedDeviceChanged;
            _bindings.BindingsChanged -= RefreshConfiguration;
            StopRecording();
        }
    }
}
