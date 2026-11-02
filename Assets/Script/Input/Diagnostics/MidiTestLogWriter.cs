// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace YARG.Input.Diagnostics
{
    /// <summary>Screen-owned, bounded JSONL recording. MIDI callbacks only enqueue managed records.</summary>
    public sealed class MidiTestLogWriter : IDisposable
    {
        private readonly object _sync = new();
        private readonly Queue<object> _pending = new();
        private readonly int _capacity;
        private readonly Thread _thread;
        private readonly StreamWriter _writer;
        private bool _accepting = true;
        private bool _complete;
        private string _error;
        private long _droppedRecords;

        public string Path { get; }
        public bool IsRecording { get { lock (_sync) return _accepting && _error == null; } }
        public string Error { get { lock (_sync) return _error; } }
        public long DroppedRecords { get { lock (_sync) return _droppedRecords; } }

        public MidiTestLogWriter(string path, object metadata, int capacity = 8192)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("recording path is required", nameof(path));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            Path = path;
            _capacity = capacity;
            // Creation is called by the UI, never by the RtMidi callback. Fail immediately
            // rather than showing an active recorder whose destination cannot be opened.
            _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, 4096), new UTF8Encoding(false));
            _pending.Enqueue(metadata);
            _thread = new Thread(WriteLoop) { IsBackground = true, Name = "MIDI test recording" };
            _thread.Start();
        }

        public bool TryEnqueue(object record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            lock (_sync)
            {
                if (!_accepting) return false;
                if (_pending.Count >= _capacity)
                {
                    _droppedRecords++;
                    return false;
                }
                _pending.Enqueue(record);
                Monitor.PulseAll(_sync);
                return true;
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _accepting = false;
                _complete = true;
                Monitor.PulseAll(_sync);
            }
            // Stop is UI-owned. No callback takes this path, and no caller should
            // retain the capture lock while waiting for the writer to drain.
            _thread.Join();
        }

        public void Dispose() => Stop();

        private void WriteLoop()
        {
            long unflushed = 0;
            try
            {
                while (true)
                {
                    object record;
                    lock (_sync)
                    {
                        while (_pending.Count == 0 && !_complete) Monitor.Wait(_sync);
                        if (_pending.Count == 0 && _complete) break;
                        record = _pending.Dequeue();
                        unflushed++;
                    }
                    _writer.WriteLine(JsonConvert.SerializeObject(record));
                    // Flush each bounded batch, including quiet periods while recording.
                    bool idle;
                    lock (_sync) idle = _pending.Count == 0;
                    if (idle)
                    {
                        _writer.Flush();
                        unflushed = 0;
                    }
                }
                _writer.WriteLine(JsonConvert.SerializeObject(new {
                    type = "recording_end", dropped_records = DroppedRecords
                }));
                _writer.Flush();
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _error = "failed to write MIDI test recording: " + exception.Message;
                    _accepting = false;
                    _complete = true;
                    _droppedRecords += _pending.Count + unflushed;
                    _pending.Clear();
                }
            }
            finally
            {
                try { _writer.Dispose(); }
                catch (Exception exception)
                {
                    lock (_sync) _error ??= "failed to close MIDI test recording: " + exception.Message;
                }
            }
        }
    }
}
