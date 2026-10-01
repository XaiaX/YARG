using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Minis;
using UnityEngine;
using YARG.Helpers;

namespace YARG.Gameplay
{
    /// <summary>Gameplay-scoped, bounded local capture for selected raw MIDI messages.</summary>
    internal static class RawMidiLogger
    {
        private const int MAX_QUEUED_RECORDS = 4096;
        private const int MAX_QUEUED_CONTROL_RECORDS = 8;
        private const int MAX_QUEUED_EVENTS = MAX_QUEUED_RECORDS - MAX_QUEUED_CONTROL_RECORDS;
        private const long MAX_FILE_BYTES = 4 * 1024 * 1024;
        private const int MAX_ROTATED_FILES = 3;
        private const int WRITER_BATCH_SIZE = 128;
        private const string DIRECTORY_NAME = "midi-logs";
        private const string FILE_NAME = "raw-midi.csv";
        private const string HEADER = "record_type,session_id,utc,port,rtmidi_timestamp_delta_seconds,status,data1,data2,overflow_count,port_opened_observed";
        private static readonly int NEWLINE_BYTES = Encoding.UTF8.GetByteCount(Environment.NewLine);

        private struct Record
        {
            public string Type;
            public long SessionId;
            public string Utc;
            public string Port;
            public double TimestampDelta;
            public byte Status;
            public byte Data1;
            public byte Data2;
            public long OverflowCount;
            public bool PortOpenedObserved;
        }

        private static readonly object Sync = new();
        private static readonly Queue<Record> PendingRecords = new();
        private static Thread _writerThread;
        private static bool _workerFailed;
        private static bool _enabled;
        private static bool _portOpenedDuringSession;

        private static long _sessionId;
        private static long _overflowCount;
        private static long _nextSessionId = DateTime.UtcNow.Ticks;
        private static int _queuedEvents;
        private static int _queuedControlRecords;

        public static void Start()
        {
            lock (Sync)
            {
                EnsureWorker();
                // Reserve both boundary records before accepting a new session. If disk
                // is stalled, skip this capture rather than write an unpaired session.
                if (_workerFailed || _enabled || _queuedControlRecords > MAX_QUEUED_CONTROL_RECORDS - 2) return;
                _sessionId = ++_nextSessionId;
                _overflowCount = 0;
                _enabled = true;
                _portOpenedDuringSession = false;
                RawMidiEvents.MessageReceived += OnMessage;
                RawMidiEvents.PortOpened += OnPortOpened;
                EnqueueControlRecord(new Record
                {
                    Type = "SESSION_START",
                    SessionId = _sessionId,
                    Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                });
                Monitor.PulseAll(Sync);
            }
        }

        /// <summary>Signals session end without waiting for disk I/O or worker completion.</summary>
        public static void Stop()
        {
            lock (Sync)
            {
                if (!_enabled) return;
                _enabled = false;
                RawMidiEvents.MessageReceived -= OnMessage;
                RawMidiEvents.PortOpened -= OnPortOpened;
                EnqueueControlRecord(new Record
                {
                    Type = "SESSION_END",
                    SessionId = _sessionId,
                    Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    OverflowCount = _overflowCount,
                    PortOpenedObserved = _portOpenedDuringSession,
                });
                Monitor.PulseAll(Sync);
            }
        }

        private static void EnsureWorker()
        {
            // A failed writer can still be unwinding its finally block. Do not start a
            // second writer against the same file until that worker has actually exited.
            if (_writerThread is { IsAlive: true }) return;
            _workerFailed = false;
            _writerThread = new Thread(WriteLoop) { IsBackground = true, Name = "Raw MIDI logger" };
            _writerThread.Start();
        }

        private static void OnMessage(string port, double timestampDelta, byte status, byte data1, byte data2)
        {
            lock (Sync)
            {
                if (!_enabled) return;
                if (_queuedEvents >= MAX_QUEUED_EVENTS)
                {
                    _overflowCount++;
                    return;
                }
                PendingRecords.Enqueue(new Record
                {
                    Type = "EVENT",
                    SessionId = _sessionId,
                    Port = port,
                    TimestampDelta = timestampDelta,
                    Status = status,
                    Data1 = data1,
                    Data2 = data2,
                });
                _queuedEvents++;
                Monitor.Pulse(Sync);
            }
        }

        private static void OnPortOpened(string portName)
        {
            lock (Sync)
            {
                if (_enabled)
                {
                    _portOpenedDuringSession = true;
                }
            }
        }

        private static void EnqueueControlRecord(Record record)
        {
            PendingRecords.Enqueue(record);
            _queuedControlRecords++;
        }

        private static void WriteLoop()
        {
            string path = Path.Combine(PathHelper.PersistentDataPath, DIRECTORY_NAME, FILE_NAME);
            StreamWriter writer = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    string existingHeader;
                    using (var reader = new StreamReader(path)) existingHeader = reader.ReadLine();
                    if (existingHeader != HEADER || new FileInfo(path).Length >= MAX_FILE_BYTES)
                        Rotate(path);
                }
                writer = OpenWriter(path);
                long fileBytes = writer.BaseStream.Length;
                if (fileBytes == 0)
                {
                    writer.WriteLine(HEADER);
                    fileBytes = Encoding.UTF8.GetByteCount(HEADER) + NEWLINE_BYTES;
                }

                while (true)
                {
                    List<Record> batch = new(WRITER_BATCH_SIZE);
                    lock (Sync)
                    {
                        while (PendingRecords.Count == 0) Monitor.Wait(Sync);
                        while (PendingRecords.Count > 0 && batch.Count < WRITER_BATCH_SIZE)
                        {
                            Record record = PendingRecords.Dequeue();
                            if (record.Type == "EVENT") _queuedEvents--;
                            else _queuedControlRecords--;
                            batch.Add(record);
                        }
                    }

                    foreach (Record record in batch)
                    {
                        string line = Format(record);
                        int bytes = Encoding.UTF8.GetByteCount(line) + NEWLINE_BYTES;
                        int headerBytes = Encoding.UTF8.GetByteCount(HEADER) + NEWLINE_BYTES;
                        if (fileBytes + bytes > MAX_FILE_BYTES)
                        {
                            writer.Flush();
                            writer.Dispose();
                            Rotate(path);
                            writer = OpenWriter(path);
                            writer.WriteLine(HEADER);
                            fileBytes = headerBytes;
                        }
                        writer.WriteLine(line);
                        fileBytes += bytes;
                    }
                    // Flush each bounded batch; all writes remain on the background worker.
                    if (batch.Count > 0) writer.Flush();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Raw MIDI logger failed; capture stopped: {exception.Message}");
                lock (Sync)
                {
                    _workerFailed = true;
                    _enabled = false;
                    PendingRecords.Clear();
                    _queuedEvents = 0;
                    _queuedControlRecords = 0;
                    RawMidiEvents.MessageReceived -= OnMessage;
                    RawMidiEvents.PortOpened -= OnPortOpened;
                }
            }
            finally
            {
                writer?.Dispose();
            }
        }

        private static string Format(Record record)
        {
            if (record.Type == "EVENT")
            {
                return string.Join(",", record.Type, record.SessionId.ToString(CultureInfo.InvariantCulture), string.Empty,
                    Escape(record.Port), record.TimestampDelta.ToString("R", CultureInfo.InvariantCulture),
                    record.Status.ToString("X2", CultureInfo.InvariantCulture), record.Data1.ToString("X2", CultureInfo.InvariantCulture),
                    record.Data2.ToString("X2", CultureInfo.InvariantCulture), string.Empty, string.Empty);
            }

            return string.Join(",", record.Type, record.SessionId.ToString(CultureInfo.InvariantCulture), Escape(record.Utc),
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                record.OverflowCount.ToString(CultureInfo.InvariantCulture),
                record.Type == "SESSION_END" ? (record.PortOpenedObserved ? "true" : "false") : string.Empty);
        }

        private static StreamWriter OpenWriter(string path) => new(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096), new UTF8Encoding(false));

        private static void Rotate(string path)
        {
            for (int i = MAX_ROTATED_FILES - 1; i >= 1; i--)
            {
                string source = $"{path}.{i}";
                string destination = $"{path}.{i + 1}";
                if (File.Exists(destination)) File.Delete(destination);
                if (File.Exists(source)) File.Move(source, destination);
            }
            string first = path + ".1";
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(path)) File.Move(path, first);
        }

        private static string Escape(string value)
        {
            // Port names are descriptive metadata, not raw MIDI bytes; bound them so no record can exceed the file cap.
            if (value == null) value = string.Empty;
            if (value.Length > 512) value = value.Substring(0, 512);
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
