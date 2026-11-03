// pattern: Imperative Shell
#nullable enable
using System;
using System.Threading;
using YARG.Core.Audio;
using YARG.Core.Logging;
using YARG.Input;

namespace YARG.Audio.BASS
{
    /// <summary>
    ///     Unified player microphone device for both ASIO and Shared Audio backends.
    ///     Manages timed input frames for vocal gameplay scoring and coordinates live monitoring.
    /// </summary>
    public sealed class BassMicDevice : MicDevice
    {
        private readonly IBassMicSource  _source;
        private          BassMicAnalyzer _analyzer;
        private readonly object          _lifecycleLock = new();
        private          bool                 _disposed;
        private          int                  _levelPreviewLeases;
        private          float                _sensitivityOffsetDb;
        private          bool                 _analyzerAvailable;

        public int? DeviceIndex { get; }

        public bool IsConnected
        {
            get { lock (_lifecycleLock) return !_disposed && _analyzerAvailable && _source.IsValid && !_analyzer.HasFailed; }
        }

        public float SensitivityOffsetDb
        {
            get => Volatile.Read(ref _sensitivityOffsetDb);
            set => Volatile.Write(ref _sensitivityOffsetDb, MicrophoneSetupValues.NormalizeSensitivity(value));
        }

        public float EffectiveThresholdDbfs => YARG.Settings.SettingsManager.Settings.MicrophoneSensitivity.Value +
            SensitivityOffsetDb - BassMicAnalyzer.CALIBRATION_GAIN_DB;

        public float PreviewLevelDbfs
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return IsConnected ? _analyzer.PreviewLevelDbfs : BassMicAnalyzer.PREVIEW_SILENCE_DBFS;
                }
            }
        }

        public float PreviewLevel => Math.Max(0f, Math.Min(1f, (PreviewLevelDbfs + 60f) / 60f));

        internal BassMicDevice(IBassMicSource source, int deviceId) : base(source.DisplayName)
        {
            StableId = ComputeStableId(deviceId, source.BaseName);
            DeviceIndex = deviceId >= 0 ? deviceId : null;
            _source = source;
            _analyzer = CreateAnalyzer();
            _analyzerAvailable = true;
            _source.InputChanged += RecreateAnalyzer;
        }

        internal static BassMicDevice? Create(IBassMicSource source, int deviceId)
        {
            try
            {
                return new BassMicDevice(source, deviceId);
            }
            catch (Exception exception)
            {
                YargLogger.LogException(exception, $"Failed to initialize microphone '{source.DisplayName}'");
                source.Dispose();
                return null;
            }
        }

        public override string StableId { get; }

        public bool TryCreateRecordingChannel(bool withEffects, out int handle, out int sampleRate)
            => _source.TryCreateRecordingChannel(withEffects, out handle, out sampleRate);

        public void ReleaseRecordingChannel(int handle) => _source.ReleaseRecordingChannel(handle);

        private BassMicAnalyzer CreateAnalyzer() =>
            new(_source, () => IsRecordingOutput, () => InputManager.CurrentInputTime,
                () => Volatile.Read(ref _levelPreviewLeases) > 0, () => SensitivityOffsetDb);

        public IDisposable BeginLevelPreview()
        {
            lock (_lifecycleLock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(BassMicDevice));
                Interlocked.Increment(ref _levelPreviewLeases);
                return new LevelPreviewLease(this);
            }
        }

        private sealed class LevelPreviewLease : IDisposable
        {
            private BassMicDevice? _owner;
            public LevelPreviewLease(BassMicDevice owner) => _owner = owner;
            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null) Interlocked.Decrement(ref owner._levelPreviewLeases);
            }
        }

        private void RecreateAnalyzer()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    return;
                }

                _analyzerAvailable = false;
                _analyzer.Dispose();
                try
                {
                    _analyzer = CreateAnalyzer();
                    _analyzerAvailable = true;
                }
                catch (Exception exception)
                {
                    YargLogger.LogException(exception, $"Failed to recreate analyzer for '{DisplayName}'");
                    return;
                }
            }
        }

        public override int Reset()
        {
            lock (_lifecycleLock)
            {
                bool sourceReset = _source.Reset();
                bool analyzerReset = _analyzer.Reset();
                return sourceReset && analyzerReset ? 0 : -1;
            }
        }

        public override bool DequeueOutputFrame(out MicOutputFrame frame)
        {
            lock (_lifecycleLock)
            {
                return _analyzer.DequeueOutputFrame(out frame);
            }
        }

        public override void ClearOutputQueue()
        {
            lock (_lifecycleLock)
            {
                _analyzer.ClearOutputQueue();
            }
        }

        public override void SetMonitoringLevel(float volume) => _source.SetMonitoringLevel(volume);

        public override void SetReverbLevel(float wet) => _source.SetReverbLevel(wet);

        public override SerializedMic Serialize() => new(_source.BaseName, _source.Channel, StableId);

        public override MicBufferInfo? GetBufferInfo() => _source.GetBufferInfo();

        protected override void DisposeUnmanagedResources()
        {
            BassMicAnalyzer analyzer;
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _source.InputChanged -= RecreateAnalyzer;
                analyzer = _analyzer;
            }

            analyzer.Dispose();
            _source.Dispose();
        }
    }
}
