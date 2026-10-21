#nullable enable
using System;
using System.Threading;
using YARG.Core.Audio;
using YARG.Core.Logging;
using YARG.Input;
using YARG.Settings;

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
        private          bool                 _analyzerAvailable;
        private          int                  _levelPreviewLeases;
        private          float                _sensitivityOffsetDb;

        public int? DeviceIndex { get; }

        internal BassMicDevice(IBassMicSource source, int? deviceIndex = null) : base(source.DisplayName)
        {
            _source = source;
            DeviceIndex = deviceIndex >= 0 ? deviceIndex : null;
            _analyzer = CreateAnalyzer();
            _analyzerAvailable = true;
            _source.InputChanged += RecreateAnalyzer;
        }

        internal static BassMicDevice? Create(IBassMicSource source, int? deviceIndex = null)
        {
            try
            {
                return new BassMicDevice(source, deviceIndex);
            }
            catch (Exception exception)
            {
                YargLogger.LogException(exception, $"Failed to initialize microphone '{source.DisplayName}'");
                source.Dispose();
                return null;
            }
        }

        public override bool IsConnected
        {
            get
            {
                lock (_lifecycleLock)
                {
                    // ASIO keeps ownership of its sources while restarting the driver.
                    return !_disposed && (_source.IsRecovering || (_source.IsConnected && _analyzerAvailable && !_analyzer.HasFailed));
                }
            }
        }

        public float SensitivityOffsetDb
        {
            get => Volatile.Read(ref _sensitivityOffsetDb);
            set => Volatile.Write(ref _sensitivityOffsetDb,
                float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(-50f, Math.Min(50f, value)));
        }

        public float EffectiveThresholdDbfs => SettingsManager.Settings.MicrophoneSensitivity.Value +
            SensitivityOffsetDb - BassMicAnalyzer.CALIBRATION_GAIN_DB;

        public float PreviewDecibels
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return !_disposed && _source.IsConnected && !_source.IsRecovering && _analyzerAvailable
                        ? _analyzer.PreviewDecibels : BassMicAnalyzer.PREVIEW_SILENCE_DBFS;
                }
            }
        }

        public float PreviewLevel
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return !_disposed && _source.IsConnected && !_source.IsRecovering && _analyzerAvailable
                        ? _analyzer.PreviewLevel : 0f;
                }
            }
        }

        // The menu owns this lease; capturing levels does not enable gameplay input or monitoring.
        public IDisposable BeginLevelPreview()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(BassMicDevice));
                }

                Interlocked.Increment(ref _levelPreviewLeases);
                return new LevelPreviewLease(this);
            }
        }

        private sealed class LevelPreviewLease : IDisposable
        {
            private BassMicDevice? _device;

            public LevelPreviewLease(BassMicDevice device) => _device = device;

            public void Dispose()
            {
                var device = Interlocked.Exchange(ref _device, null);
                if (device is not null)
                {
                    Interlocked.Decrement(ref device._levelPreviewLeases);
                }
            }
        }

        public bool TryCreateRecordingChannel(bool withEffects, out int handle, out int sampleRate)
            => _source.TryCreateRecordingChannel(withEffects, out handle, out sampleRate);

        public void ReleaseRecordingChannel(int handle) => _source.ReleaseRecordingChannel(handle);

        private BassMicAnalyzer CreateAnalyzer() =>
            new(_source, () => IsRecordingOutput, () => InputManager.CurrentInputTime,
                () => Volatile.Read(ref _levelPreviewLeases) > 0, () => SensitivityOffsetDb);

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

        public override SerializedMic Serialize() => new(_source.BaseName, _source.Channel);

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
