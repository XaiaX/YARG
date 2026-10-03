#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;

namespace YARG.Audio.PitchDetection
{
    public enum VocalsTraceEvent { Read, Decision, Output, Mapping, Clamp, Phrase, Playback, Reset, Dequeue, GuideSchedule, GuideState }

    /// <summary>Numeric-only, best-effort diagnostics. Never accepts audio, lyrics or device names.</summary>
    public sealed class VocalsTimingTrace : IDisposable
    {
        private const int DEFAULT_CAPACITY = 4096;
        private readonly object _gate = new();
        private readonly Queue<string> _queue = new();
        private readonly AutoResetEvent _wake = new(false);
        private readonly Thread _worker;
        private readonly int _capacity;
        private readonly Func<TextWriter> _open;
        private volatile bool _stopping;
        private volatile bool _failed;
        private long _dropped;
        private long _written;
        private long _errors;
        private long _unwritten;
        public long Dropped => Interlocked.Read(ref _dropped);
        public long Written => Interlocked.Read(ref _written);
        public long Errors => Interlocked.Read(ref _errors);
        public long Unwritten => Interlocked.Read(ref _unwritten);
        public bool Completed => !_worker.IsAlive;
        public static VocalsTimingTrace? Current { get; private set; }
        public static bool Enabled => Current != null;
        public static int NewSourceId() => Interlocked.Increment(ref _nextSource);
        private static int _nextSource;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorShutdown()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            UnityEditor.EditorApplication.quitting -= Shutdown;
            UnityEditor.EditorApplication.quitting += Shutdown;
        }

        private static void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode ||
                state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                Shutdown();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            Shutdown();
            try
            {
                Current = Create(Environment.GetEnvironmentVariable("YARG_VOCALS_TRACE"),
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
                Application.quitting -= Shutdown;
                Application.quitting += Shutdown;
            }
            catch (Exception)
            {
                UnityEngine.Debug.LogWarning("Vocals timing trace disabled: invalid or unavailable external destination.");
            }
        }

        public static void Shutdown()
        {
            var trace = Current;
            Current = null;
            if (trace == null) return;
            trace.Dispose();
            UnityEngine.Debug.Log($"Vocals timing trace: written={trace.Written}, dropped={trace.Dropped}, errors={trace.Errors}, unwritten={trace.Unwritten}, writerCompleted={trace.Completed}");
        }

        /// <summary>Requires a new absolute file in an existing directory. Rejects all linked components.</summary>
        public static VocalsTimingTrace? Create(string? path, string projectRoot)
        {
            if (string.IsNullOrEmpty(path)) return null;
            ValidatePath(path!, projectRoot);
            return new VocalsTimingTrace(() =>
            {
                // Recheck on the writer thread immediately before creation; never overwrite an existing file.
                ValidatePath(path!, projectRoot);
                return new StreamWriter(new FileStream(path!, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            });
        }

        public static void ValidatePath(string path, string projectRoot)
        {
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Absolute file required");
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // A linked project root cannot safely be compared lexically with the external path.
            // Fail closed instead of guessing the physical installation location.
            string? rootComponent = root;
            while (rootComponent != null)
            {
                if (Directory.Exists(rootComponent) &&
                    (File.GetAttributes(rootComponent) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Linked project roots are not supported");
                rootComponent = Path.GetDirectoryName(rootComponent);
            }
            if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("External destination required");
            if (File.Exists(full) || Directory.Exists(full)) throw new ArgumentException("New file required");
            string? parent = Path.GetDirectoryName(full);
            if (parent == null || !Directory.Exists(parent)) throw new ArgumentException("Existing directory required");
            // File.GetAttributes also rejects dangling links and reparse points when supported by the runtime.
            try
            {
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Links are not permitted");
            }
            catch (FileNotFoundException) { }
            while (parent != null)
            {
                if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Links are not permitted");
                parent = Path.GetDirectoryName(parent);
            }
        }

        // The writer factory is injectable so tests can simulate blocked and failed storage without microphone access.
        public VocalsTimingTrace(Func<TextWriter> open, int capacity = DEFAULT_CAPACITY)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _open = open;
            _worker = new Thread(WriteLoop) { IsBackground = true, Name = "Vocals timing trace writer" };
            _worker.Start();
        }

        public static void Emit(VocalsTraceEvent kind, int source, params double[] values)
        {
            try { Current?.Record(kind, source, values); }
            catch (Exception) { /* Diagnostics must never alter microphone or scoring behavior. */ }
        }

        public void Record(VocalsTraceEvent kind, int source, params double[] values)
        {
            try
            {
                if (_stopping || _failed) { Interlocked.Increment(ref _dropped); return; }
                // Keep both queue memory and each record bounded. No caller-supplied strings are accepted.
                if (values.Length > 20) { Interlocked.Increment(ref _dropped); return; }
                string record = ((int) kind).ToString(CultureInfo.InvariantCulture) + "," + source + "," + Stopwatch.GetTimestamp();
                foreach (double value in values) record += "," + value.ToString("R", CultureInfo.InvariantCulture);
                if (!Monitor.TryEnter(_gate)) { Interlocked.Increment(ref _dropped); return; }
                try
                {
                    if (_stopping || _failed || _queue.Count >= _capacity) { Interlocked.Increment(ref _dropped); return; }
                    _queue.Enqueue(record);
                }
                finally { Monitor.Exit(_gate); }
                _wake.Set();
            }
            catch (Exception) { Interlocked.Increment(ref _errors); Interlocked.Increment(ref _dropped); }
        }

        private void WriteLoop()
        {
            bool writing = false;
            try
            {
                using var writer = _open();
                writer.WriteLine("# YARG vocals timing trace v1; event,source,stopwatchTicks,values; frequency=" + Stopwatch.Frequency);
                while (true)
                {
                    string? record = null;
                    lock (_gate)
                    {
                        if (_queue.Count > 0) record = _queue.Dequeue();
                        else if (_stopping) break;
                    }
                    if (record == null) { writer.Flush(); _wake.WaitOne(100); continue; }
                    writing = true;
                    writer.WriteLine(record);
                    Interlocked.Increment(ref _written);
                    writing = false;
                }
                writer.WriteLine($"# summary,written={Written},dropped={Dropped},errors={Errors},unwritten={Unwritten}");
                writer.Flush();
            }
            catch (Exception)
            {
                _failed = true;
                Interlocked.Increment(ref _errors);
                lock (_gate)
                {
                    Interlocked.Add(ref _unwritten, _queue.Count + (writing ? 1 : 0));
                    _queue.Clear();
                }
            }
        }

        public void Dispose()
        {
            _stopping = true;
            _wake.Set();
            // Bounded quit wait. Do not close a stream from the main thread or block gameplay on storage.
            if (!_worker.Join(2000))
            {
                Interlocked.Increment(ref _errors);
                lock (_gate) Interlocked.Exchange(ref _unwritten, _queue.Count + 1);
            }
        }
    }
}
