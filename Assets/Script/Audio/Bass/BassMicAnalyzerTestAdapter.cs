#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
#nullable enable
using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using YARG.Audio.PitchDetection;
using YARG.Core.Audio;

namespace YARG.Audio.BASS
{
    /// <summary>Editor-test bridge driving the actual microphone analysis worker.</summary>
    public sealed class BassMicAnalyzerTestAdapter : IDisposable
    {
        private readonly SyntheticSource _source;
        private readonly BassMicAnalyzer _analyzer;
        private readonly object _controller = new();
        private bool _disposed;
        private bool _isPaused;
        private readonly ManualResetEventSlim _completed = new(false);
        private readonly ManualResetEventSlim _failed = new(false);
        private readonly ManualResetEventSlim _paused = new(false);
        private ExceptionDispatchInfo? _workerFailure;
        private int _completedFrames;
        private volatile bool _recording = true;

        public BassMicAnalyzerTestAdapter(int sampleRate)
        {
            _source = new SyntheticSource(sampleRate);
            _analyzer = new BassMicAnalyzer(_source, () => _recording, _source.ReadTime);
            _analyzer.TestFrameCompleted = () =>
            {
                _source.Complete();
                Interlocked.Increment(ref _completedFrames);
                _completed.Set();
            };
            _analyzer.TestPauseCompleted = () => _paused.Set();
            _analyzer.TestWorkerFailed = error =>
            {
                _source.Fault();
                _workerFailure = ExceptionDispatchInfo.Capture(error);
                _failed.Set();
            };
        }

        public void SubmitAndWait(float[] samples, double readTime, int timeoutMilliseconds)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (samples.Length != _source.FrameSamples) throw new ArgumentException("Submit exactly one frame", nameof(samples));
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            // One controller owns publication through completion; a second caller must not
            // observe the first caller's completion as its own transaction.
            lock (_controller)
            {
                if (!_source.IsValid) throw new ObjectDisposedException(nameof(BassMicAnalyzerTestAdapter));
                int previous = Volatile.Read(ref _completedFrames);
                _completed.Reset();
                try { _source.Submit(samples, readTime, timeoutMilliseconds); }
                catch { ThrowWorkerFailure(); throw; }
                WaitForFrame(previous, timeoutMilliseconds);
            }
        }

        public void WaitForFrameWithoutSubmission(int timeoutMilliseconds)
        {
            lock (_controller) WaitForFrame(Volatile.Read(ref _completedFrames), timeoutMilliseconds);
        }

        private void WaitForFrame(int previous, int timeoutMilliseconds)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (Volatile.Read(ref _completedFrames) == previous)
            {
                ThrowWorkerFailure();
                int remaining = timeoutMilliseconds - (int) clock.ElapsedMilliseconds;
                if (remaining <= 0) throw new TimeoutException("Worker did not finish the submitted frame");
                int signaled = WaitHandle.WaitAny(new[] { _completed.WaitHandle, _failed.WaitHandle }, remaining);
                ThrowWorkerFailure();
                if (signaled == WaitHandle.WaitTimeout)
                    throw new TimeoutException("Worker did not finish the submitted frame");
                // A stale completion signal can race the submitter's Reset; count is authoritative.
                if (signaled == 0 && Volatile.Read(ref _completedFrames) == previous) _completed.Reset();
            }
            ThrowWorkerFailure();
        }

        public void ThrowOnNextFrame(Exception error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            // Installed before publishing: Process invokes Diagnostics after Read and enqueue.
            lock (_controller) _source.SetDiagnosticFault(_analyzer, error);
        }

        public bool TryDequeue(out MicOutputFrame frame) => _analyzer.DequeueOutputFrame(out frame);

        public void PauseAndWait(int timeoutMilliseconds)
        {
            lock (_controller)
            {
                if (!_source.IsValid) throw new ObjectDisposedException(nameof(BassMicAnalyzerTestAdapter));
                if (_isPaused) return;
                _paused.Reset();
                _recording = false;
                _source.Cancel(); // Wake GetBacklogBytes before PauseAnalysis needs _analysisLock.
                if (!_paused.Wait(timeoutMilliseconds))
                {
                    ThrowWorkerFailure();
                    throw new TimeoutException("Worker did not clear state on pause");
                }
                ThrowWorkerFailure();
                _isPaused = true;
            }
        }

        public void Resume()
        {
            lock (_controller)
            {
                if (!_source.IsValid) throw new ObjectDisposedException(nameof(BassMicAnalyzerTestAdapter));
                _source.Rearm();
                _isPaused = false;
                _recording = true;
            }
        }

        public void WaitForBoundary(int timeoutMilliseconds) => _source.WaitForBoundary(timeoutMilliseconds);

        public bool Reset()
        {
            // Reset is only valid after the prior transaction has completed. Wait for
            // the worker to pause before taking the analyzer lock: cancellation alone
            // leaves it repeatedly reacquiring that lock for empty reads.
            lock (_controller)
            {
                if (!_source.IsValid) throw new ObjectDisposedException(nameof(BassMicAnalyzerTestAdapter));
                _source.RequireQuiescent();
                PauseAndWait(3000);
                bool result = _analyzer.Reset();
                Resume();
                return result;
            }
        }

        public void Dispose()
        {
            // Close first, outside the controller lock: it also releases a submitter blocked
            // in GetBacklogBytes. Do not dispose events until that submitter unwinds.
            _source.Close();
            lock (_controller)
            {
                if (_disposed) return;
                _disposed = true;
                _analyzer.Dispose();
                _completed.Dispose();
                _failed.Dispose();
                _paused.Dispose();
            }
        }

        private void ThrowWorkerFailure() => _workerFailure?.Throw();

        private sealed class SyntheticSource : IBassMicSampleSource
        {
            private enum TransactionState { Idle, Published, Consumed, Completed, Canceled, Faulted }
            private readonly object _sync = new();
            private readonly ManualResetEventSlim _reset = new(false);
            private TransactionState _state;
            private float[]? _samples;
            private double _time;
            private bool _valid = true;
            private bool _atBoundary;
            private bool _cancellationAcknowledged;
            private Exception? _diagnosticFault;
            public int SampleRate { get; }
            public int FrameSamples => SampleRate * MicDevice.RECORD_PERIOD_MS / 1000;
            public bool IsValid { get { lock (_sync) return _valid; } }
            public SyntheticSource(int sampleRate) => SampleRate = sampleRate;

            public void Submit(float[] samples, double time, int timeoutMilliseconds)
            {
                lock (_sync)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (_valid && _state != TransactionState.Faulted && (!_atBoundary || _state != TransactionState.Idle))
                    {
                        int remaining = timeoutMilliseconds - (int) clock.ElapsedMilliseconds;
                        if (remaining <= 0) throw new TimeoutException("Worker never reached an idle read");
                        Monitor.Wait(_sync, remaining);
                    }
                    if (!_valid) throw new ObjectDisposedException(nameof(SyntheticSource));
                    if (_state == TransactionState.Faulted) throw new InvalidOperationException("Worker faulted");
                    _atBoundary = false;
                    // Publish a single immutable snapshot shared by backlog, time and Read.
                    _samples = (float[]) samples.Clone();
                    _time = time;
                    _state = TransactionState.Published;
                    Monitor.PulseAll(_sync);
                }
            }

            public int GetBacklogBytes()
            {
                lock (_sync)
                {
                    if (_state == TransactionState.Completed)
                    {
                        _state = TransactionState.Idle;
                        Monitor.PulseAll(_sync);
                    }
                    _atBoundary = true;
                    Monitor.PulseAll(_sync);
                    while (_valid && _state == TransactionState.Idle) Monitor.Wait(_sync);
                    _atBoundary = false;
                    if (_state == TransactionState.Canceled) _cancellationAcknowledged = true;
                    Monitor.PulseAll(_sync);
                    return _state == TransactionState.Published ? _samples!.Length * sizeof(float) : 0;
                }
            }

            public double ReadTime() { lock (_sync) return _state == TransactionState.Published ? _time : 0; }

            public int Read(Span<float> destination)
            {
                lock (_sync)
                {
                    if (_state != TransactionState.Published) return 0;
                    int count = Math.Min(destination.Length, _samples!.Length);
                    _samples.AsSpan(0, count).CopyTo(destination);
                    _samples = null;
                    _state = TransactionState.Consumed; // Completion is signaled only after Process/enqueue.
                    return count;
                }
            }

            public void Complete()
            {
                lock (_sync)
                {
                    if (_state == TransactionState.Consumed) _state = TransactionState.Completed;
                    Monitor.PulseAll(_sync);
                }
            }

            public bool ResetToLive()
            {
                lock (_sync)
                {
                    _samples = null;
                    _reset.Set();
                    Monitor.PulseAll(_sync);
                    return _valid;
                }
            }

            public void WaitForReset(int timeoutMilliseconds)
            {
                if (!_reset.Wait(timeoutMilliseconds)) throw new TimeoutException("Worker did not pause/reset");
            }

            public void WaitForBoundary(int timeoutMilliseconds)
            {
                lock (_sync)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (_valid && _state != TransactionState.Faulted && !_atBoundary)
                    {
                        int remaining = timeoutMilliseconds - (int) clock.ElapsedMilliseconds;
                        if (remaining <= 0) throw new TimeoutException("Worker did not reach the read boundary");
                        Monitor.Wait(_sync, remaining);
                    }
                    if (!_valid) throw new ObjectDisposedException(nameof(SyntheticSource));
                    if (_state == TransactionState.Faulted) throw new InvalidOperationException("Worker faulted");
                }
            }

            public void RequireQuiescent()
            {
                lock (_sync)
                {
                    if (_state == TransactionState.Published || _state == TransactionState.Consumed)
                        throw new InvalidOperationException("Reset requires a completed transaction");
                    if (!_atBoundary)
                        throw new InvalidOperationException("Reset requires a quiescent read boundary");
                }
            }

            public void Cancel()
            {
                lock (_sync)
                {
                    // A consumed frame must finish processing before reset or rearm.
                    if (_state != TransactionState.Consumed) _state = TransactionState.Canceled;
                    _samples = null;
                    _cancellationAcknowledged = !_atBoundary;
                    _reset.Reset();
                    Monitor.PulseAll(_sync);
                }
            }

            public void Rearm()
            {
                lock (_sync)
                {
                    if (_state == TransactionState.Consumed) throw new InvalidOperationException("Frame not completed");
                    if (!_cancellationAcknowledged && _state == TransactionState.Canceled)
                        throw new InvalidOperationException("Worker has not acknowledged cancellation");
                    _state = TransactionState.Idle;
                    _cancellationAcknowledged = false;
                    _reset.Reset();
                    Monitor.PulseAll(_sync);
                }
            }

            public void Fault()
            {
                lock (_sync)
                {
                    _state = TransactionState.Faulted;
                    _samples = null;
                    Monitor.PulseAll(_sync);
                }
            }

            public void Close()
            {
                lock (_sync)
                {
                    _valid = false;
                    _state = TransactionState.Canceled;
                    _samples = null;
                    Monitor.PulseAll(_sync);
                }
            }

            public void SetDiagnosticFault(BassMicAnalyzer analyzer, Exception error)
            {
                lock (_sync)
                {
                    if (_state != TransactionState.Idle && _state != TransactionState.Completed)
                        throw new InvalidOperationException("Install a fault only at a quiescent boundary");
                    _diagnosticFault = error;
                }
                var field = typeof(BassMicAnalyzer).GetField("_processor",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(analyzer) is not VocalsFrameProcessor processor)
                    throw new InvalidOperationException("Real analyzer processor is unavailable");
                processor.Diagnostics = _ =>
                {
                    Exception? fault;
                    lock (_sync) { fault = _diagnosticFault; _diagnosticFault = null; }
                    if (fault != null) throw fault;
                };
            }
        }
    }
}
#endif
