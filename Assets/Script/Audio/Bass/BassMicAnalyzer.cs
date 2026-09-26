#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using ManagedBass;
using YARG.Audio.PitchDetection;
using YARG.Core.Audio;
using YARG.Core.Logging;
using YARG.Settings;

namespace YARG.Audio.BASS
{
    /// <summary>
    ///     Supplies microphone samples to the background analyzer.
    /// </summary>
    internal interface IBassMicSampleSource
    {
        int  SampleRate { get; }
        bool IsValid    { get; }
        int Read(Span<float> destination);
        int GetBacklogBytes();
        bool ResetToLive();
    }

    /// <summary>
    ///     Reads microphone samples on a dedicated background thread, running pitch detection and volume analysis
    ///     to generate timed input frames for vocal gameplay without blocking the main game loop.
    /// </summary>
    internal sealed class BassMicAnalyzer : IDisposable
    {
        private const int IDLE_SLEEP_MS = 1;

        private readonly object                          _analysisLock = new();
        private readonly Func<double>                    _getInputTime;
        private readonly Func<bool>                      _isOutputRecording;
        private readonly ConcurrentQueue<MicOutputFrame> _outputFrames = new();
        private readonly VocalsFrameProcessor            _processor;
        private readonly float[]                         _readBuffer;
        private readonly IBassMicSampleSource             _source;
        private readonly Thread                           _worker;
        private bool _failureLogged;
        private bool _paused;
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
        internal Action? TestFrameCompleted;
        internal Action? TestPauseCompleted;
        internal Action<Exception>? TestWorkerFailed;
#endif

        private volatile bool _stopRequested;

        public BassMicAnalyzer(IBassMicSampleSource source, Func<bool> isOutputRecording, Func<double> getInputTime)
        {
            _source = source;
            _isOutputRecording = isOutputRecording;
            _getInputTime = getInputTime;

            int samplesPerFrame = checked(source.SampleRate * MicDevice.RECORD_PERIOD_MS / 1000);
            _readBuffer = new float[samplesPerFrame];
            _processor = new VocalsFrameProcessor(source.SampleRate);

            _worker = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = $"Mic analysis {source.SampleRate} Hz",
            };
            _worker.Start();
        }

        public void Dispose() => StopAndJoin();

        public bool Reset()
        {
            lock (_analysisLock)
            {
                bool reset = _source.ResetToLive();
                ClearState();
                _paused = false;
                return reset;
            }
        }

        public bool StopAndJoin()
        {
            _stopRequested = true;

            if (Thread.CurrentThread == _worker)
            {
                return true;
            }

            if (!_worker.Join(1000))
            {
                YargLogger.LogError("Timed out waiting for microphone analysis worker to stop");
                _worker.Join();
            }

            return true;
        }

        public bool DequeueOutputFrame(out MicOutputFrame frame) => _outputFrames.TryDequeue(out frame);

        public void ClearOutputQueue() => _outputFrames.Clear();

        private void ReadLoop()
        {
            try
            {
                while (!_stopRequested && _source.IsValid)
                {
                    if (!_isOutputRecording())
                    {
                        PauseAnalysis();
                        Thread.Sleep(IDLE_SLEEP_MS);
                        continue;
                    }

                    if (!TryReadSamples())
                    {
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                try { TestWorkerFailed?.Invoke(exception); }
                catch (Exception) { /* Test observers must never mask the worker failure. */ }
#endif
                YargLogger.LogException(exception, "Microphone analysis worker failed");
            }
        }

        private void PauseAnalysis()
        {
            lock (_analysisLock)
            {
                if (_paused)
                {
                    return;
                }

                if (_source.IsValid && !_source.ResetToLive())
                {
                    LogFailure("Failed to reset disabled microphone analysis source");
                    return;
                }

                ClearState();
                _paused = true;
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                try { TestPauseCompleted?.Invoke(); }
                catch (Exception) { /* Test observers must not alter pause behavior. */ }
#endif
            }
        }

        private bool TryReadSamples()
        {
            lock (_analysisLock)
            {
                if (_stopRequested || !_source.IsValid)
                {
                    return false;
                }

                _paused = false;

                int backlogBytes = _source.GetBacklogBytes();
                if (backlogBytes < 0)
                {
                    LogFailure("Failed to query microphone analysis backlog");
                    return false;
                }

                double readTime = _getInputTime();
                int samplesRead = _source.Read(_readBuffer.AsSpan());

                if (samplesRead < 0)
                {
                    LogFailure("Failed to read microphone analysis samples");
                    return false;
                }

                if (samplesRead == 0)
                {
                    Thread.Sleep(IDLE_SLEEP_MS);
                }
                else
                {
                    _processor.Process(_readBuffer, samplesRead, backlogBytes, readTime,
                        SettingsManager.Settings.MicrophoneSensitivity.Value,
                        (frame, _) => _outputFrames.Enqueue(frame));
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                    try { TestFrameCompleted?.Invoke(); }
                    catch (Exception) { /* Test observers must not alter processing. */ }
#endif
                }

                return true;
            }
        }

        private void ClearState()
        {
            _processor.Reset();
            _outputFrames.Clear();
        }

        private void LogFailure(string message)
        {
            if (_failureLogged)
            {
                return;
            }

            _failureLogged = true;
            YargLogger.LogError($"{message}: {Bass.LastError}");
        }
    }
}
