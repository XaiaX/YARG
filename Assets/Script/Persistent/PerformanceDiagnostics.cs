using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using YARG.Core.Diagnostics;

namespace YARG
{
    [DefaultExecutionOrder(10000)]
    public sealed class PerformanceDiagnostics : MonoBehaviour
    {
        public const int SCHEMA_VERSION = 1;
        private const int RING_CAPACITY = 8192;
        private const int FLUSH_CHUNK = 4096;

        // FrameTimingManager delivers a frame's timing with a fixed delay of four
        // frames (GPU results are not available sooner; Unity 6 removed
        // FrameTiming.frameIndex, so samples cannot be keyed by frame number).
        private const int FTM_PENDING_CAPACITY = 16;
        private const int FTM_FRAME_DELAY = 4;

        // Bounded size of the run metadata map exposed by SetRunMetadata.
        private const int RUN_METADATA_CAPACITY = 32;
        private const string CSV_HEADER =
            "schema,session,frame,unityFrame,realtime_s,phase,warmup,dt_s,visualDt_s,inputTime_s,songTime_s,visualTime_s," +
            "targetHz,budget_s,missedRefreshPeriods,ftm_cpu_s,ftm_main_s,ftm_render_s,ftm_gpu_s," +
            "gcAllocBytes,gcCollectionsTotal," +
            "callbackQueueBefore,callbackQueueAfter,callbacksInvoked,callbackExceptions,callbackDrainTicks,callbackDrain_s," +
            "players,inputSystemUpdates,bindingsVisited,gameInputsQueued,runQueuedUpdatesCalls,scheduledBefore,scheduledGenerated,scheduledAfter,scheduledSortTicks,scheduledSort_s,engineLoopIterations," +
            "hitChecks,hitNotesInspected,hitNotesInspectedMax," +
            "trackDueNotes,trackDueBeatlines,trackDueCoda,trackDueUnison,trackDueEffects,trackPooledTake,trackPooledReturn,trackActivations,trackEffectsActive,trackEffectsRemoved,effectSwaps," +
            "vocalRangesDue,vocalLyricsDue,vocalNotesDue,vocalPooledTake,vocalActivations,vocalLinePointUpdates," +
            "stageKitQueueBefore,stageKitQueueAfter,stageKitCommands,stageKitKits,stageKitSendTicks,stageKitSend_s," +
            "sacnSends,sacnChannels,sacnSendTicks,sacnSend_s," +
            "vrmCharacters,vrmRendererCount,vrmBoundsUpdates,vrmBoundsTicks,vrmBounds_s," +
            "neonMaterials,neonPropertyWrites,neonSkippedUnchanged," +
            "starPowerActive,starPowerRendererScans,starPowerMaterialArrayReads," +
            "hudScoreWrites,hudVocalWrites,hudLyricWrites,hudInputViewerWrites,hudSetActiveTransitions," +
            "dataStreamPackets,dataStreamBytes,dataStreamQueueDepth,dataStreamSerializeTicks,dataStreamSerialize_s," +
            "canvasBuildBatchSamples,tmpGenerateMeshSamples,lowFpsCameraRenders,highwayPasses,postProcessExpired,diagnosticDroppedRows";

        public static readonly ProfilerMarker GameManagerUpdateMarker = new("YARG.GameManager.Update");
        public static readonly ProfilerMarker SongRunnerUpdatePlaybackMarker = new("YARG.SongRunner.UpdatePlayback");
        public static readonly ProfilerMarker InputOnAfterUpdateMarker = new("YARG.Input.OnAfterUpdate");
        public static readonly ProfilerMarker BindingsUpdateBindingsForFrameMarker = new("YARG.Bindings.UpdateBindingsForFrame");
        public static readonly ProfilerMarker TrackPlayerUpdateVisualsMarker = new("YARG.TrackPlayer.UpdateVisuals");
        public static readonly ProfilerMarker TrackPlayerUpdateNotesMarker = new("YARG.TrackPlayer.UpdateNotes");
        public static readonly ProfilerMarker TrackPlayerUpdateBeatlinesMarker = new("YARG.TrackPlayer.UpdateBeatlines");
        public static readonly ProfilerMarker TrackPlayerUpdateTrackEffectsMarker = new("YARG.TrackPlayer.UpdateTrackEffects");
        public static readonly ProfilerMarker VocalTrackUpdateMarker = new("YARG.VocalTrack.Update");
        public static readonly ProfilerMarker VocalTrackRangeDrainMarker = new("YARG.VocalTrack.RangeDrain");
        public static readonly ProfilerMarker VocalNoteUpdateLinePointsMarker = new("YARG.VocalNote.UpdateLinePoints");
        public static readonly ProfilerMarker UnityMainThreadCallbackUpdateMarker = new("YARG.UnityMainThreadCallback.Update");
        public static readonly ProfilerMarker StageKitSendCommandsMarker = new("YARG.StageKit.SendCommands");
        public static readonly ProfilerMarker SacnSenderMarker = new("YARG.Sacn.Sender");
        public static readonly ProfilerMarker VrmUpdateBoundsMarker = new("YARG.VRM.UpdateBounds");
        public static readonly ProfilerMarker NeonUpdateMarker = new("YARG.Neon.Update");
        public static readonly ProfilerMarker StarPowerEffectUpdateMarker = new("YARG.StarPowerEffect.Update");
        public static readonly ProfilerMarker CameraLowFpsRenderMarker = new("YARG.Camera.LowFPSRender");
        public static readonly ProfilerMarker HighwayCameraOnPreCameraRenderMarker = new("YARG.HighwayCamera.OnPreCameraRender");
        public static readonly ProfilerMarker HudUpdateMarker = new("YARG.HUD.Update");
        public static readonly ProfilerMarker DataStreamSerializeAndSendMarker = new("YARG.DataStream.SerializeAndSend");

        // Phase 2: sought Profiler stats, in preference order. The names available
        // vary per Unity version and platform, so handles are enumerated at runtime
        // in the Player (ProfilerRecorderHandle.GetAvailable) and matched against
        // these candidates case-insensitively; the first candidate that matches
        // wins, and preferred categories break ties when several handles share a
        // name. Nothing here is hard-wired to a handle value.
        private static readonly string[] GC_ALLOC_CANDIDATES =
        {
            "GC.Alloc In Frame Count",
            "GC.Allocated In Frame Count",
            "GC.Allocated In Frame",
            "GC.Alloc In Frame",
            "GC Alloc In Frame",
            "GC.Alloc In Frame Total",
            "GC Alloc",
        };
        private static readonly string[] GC_ALLOC_PREFERRED_CATEGORIES = { "Memory", "GC", "GarbageCollector" };
        private static readonly string[] MAIN_THREAD_CANDIDATES =
        {
            "Main Thread Time",
            "CPU Main Thread Frame Time",
            "PlayerMainFrameTime",
            "MainFrameTime",
            "CPU Frame Time",
        };
        private static readonly string[] MAIN_THREAD_PREFERRED_CATEGORIES = { "Internal", "Profiler" };
        private static readonly string[] GC_COLLECTION_CANDIDATES =
        {
            "GC.CollectionCount",
            "GC.Collect Count",
            "GC.Collections",
            "GarbageCollector.CollectionCount",
        };
        private static readonly string[] GC_COLLECTION_PREFERRED_CATEGORIES = { "GC", "GarbageCollector", "Memory" };

        // Run metadata (cross-agent contract): other systems (e.g.
        // PerformanceRunBootstrap) call SetRunMetadata from any thread, before or
        // after the collector enables. Deliberately NOT gated on Enabled so that
        // entries recorded before enable survive; while the collector is disabled
        // the map is inert (no counters, no capture, no file I/O). Last write wins
        // per key; distinct keys beyond the capacity are counted and dropped. The
        // parallel key array keeps the JSON write order deterministic. No removals,
        // so insertion order is stable.
        private static readonly object _runMetadataLock = new object();
        private static readonly Dictionary<string, string> _runMetadata = new Dictionary<string, string>(RUN_METADATA_CAPACITY);
        private static readonly string[] _runMetadataKeys = new string[RUN_METADATA_CAPACITY];
        private static int _runMetadataCount;
        private static long _runMetadataDropped;

        private FrameRow[] _rows;
        private static PerformanceDiagnostics _instance;
        private static long _droppedRows;
        private static long _frame;
        private static long _callbackQueueBefore;
        private static long _callbackQueueAfter;
        private static long _callbacksInvoked;
        private static long _callbackExceptions;
        private static long _callbackDrainTicks;
        private static long _inputSystemUpdates;
        private static long _bindingsVisited;
        private static long _gameInputsQueued;
        private static long _trackDueNotes;
        private static long _trackDueBeatlines;
        private static long _trackDueCoda;
        private static long _trackDueUnison;
        private static long _trackDueEffects;
        private static long _trackPooledTake;
        private static long _trackPooledReturn;
        private static long _trackActivations;
        private static long _trackEffectsActive;
        private static long _trackEffectsRemoved;
        private static long _effectSwaps;
        private static long _vocalRangesDue;
        private static long _vocalLyricsDue;
        private static long _vocalNotesDue;
        private static long _vocalPooledTake;
        private static long _vocalPooledReturn;
        private static long _vocalActivations;
        private static long _vocalLinePointUpdates;
        private static long _stageKitQueueBefore;
        private static long _stageKitQueueAfter;
        private static long _stageKitCommands;
        private static long _stageKitKits;
        private static long _stageKitSendTicks;
        private static long _sacnSends;
        private static long _sacnChannels;
        private static long _sacnSendTicks;
        private static long _vrmCharacters;
        private static long _vrmRendererCount;
        private static long _vrmBoundsUpdates;
        private static long _vrmBoundsTicks;
        private static long _neonMaterials;
        private static long _neonPropertyWrites;
        private static long _neonSkippedUnchanged;
        private static long _starPowerActive;
        private static long _starPowerRendererScans;
        private static long _starPowerMaterialArrayReads;
        private static long _hudScoreWrites;
        private static long _hudVocalWrites;
        private static long _hudLyricWrites;
        private static long _hudInputViewerWrites;
        private static long _hudSetActiveTransitions;
        private static long _dataStreamPackets;
        private static long _dataStreamBytes;
        private static long _dataStreamQueueDepth;
        private static long _dataStreamSerializeTicks;
        private static long _canvasBuildBatchSamples;
        private static long _tmpGenerateMeshSamples;
        private static long _lowFpsCameraRenders;
        private static long _highwayPasses;
        private static long _postProcessExpired;

        private StreamWriter _writer;
        private string _outputDirectory;
        private string _framesPath;
        private string _metadataPath;
        private string _session;
        private string _startUtc;
        private string _exitReason = "process_exit";
        private long _monotonicStart;
        private float _realtimeStart;
        private int _rowStart;
        private int _rowCount;
        private double _lastVisualTime;
        private bool _hasVisualTime;
        private static double _clockVisualTime;
        private static double _clockInputTime;
        private static double _clockSongTime;
        private static int _clockPlayers;
        private readonly char[] _formatBuffer = new char[96];

        // Phase 2: ProfilerRecorder capture state (replaces the M1
        // GC.GetAllocatedBytesForCurrentThread path, which is a no-op stub on
        // Unity Mono and returned 0 on every frame of a validated run).
        private ProfilerRecorder _gcAllocRecorder;
        private ProfilerRecorder _mainThreadRecorder;
        private ProfilerRecorder _gcCollectionRecorder;
        private bool _recordersStarted;
        private bool _gcAllocIsPerFrameCounter;
        private double _gcAllocPreviousValue;
        private bool _gcAllocFound;
        private bool _gcAllocRecorderValid;
        private string _gcAllocStatName = "";
        private string _gcAllocStatCategory = "";
        private bool _mainThreadFound;
        private bool _mainThreadRecorderValid;
        private string _mainThreadStatName = "";
        private string _mainThreadStatCategory = "";
        private bool _gcCollectionFound;
        private bool _gcCollectionRecorderValid;
        private string _gcCollectionStatName = "";
        private string _gcCollectionStatCategory = "";

        // Phase 3: FrameTimingManager delayed-mapping state. All preallocated;
        // the frame path performs no allocation.
        private PendingFtmCapture[] _ftmPending;
        private int _ftmPendingHead;
        private int _ftmPendingCount;
        private FrameTiming[] _ftmTimings;
        private int _ftmLastSampleUnityFrame;
        private long _ftmSamples;
        private long _ftmNonzeroSamplesSeen;
        private long _ftmZeroSamples;
        private long _ftmUnmatchedSamples;
        private long _ftmMappingMisses;

        // Identity of the most recently recorded row, for the delayed FTM fill.
        private int _lastRowSlot;
        private long _lastRowFrame;

        // Session frame counter of the newest row already written to the CSV; rows
        // at or below it can no longer receive a delayed FTM fill.
        private long _flushedThroughFrame;

        public static bool Enabled { get; private set; }
        public static string OutputDirectory => CommandLineArgs.PerformanceCsvDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            CommandLineArgs.Initialize();
            if (Enabled || string.IsNullOrWhiteSpace(CommandLineArgs.PerformanceCsvDirectory))
            {
                return;
            }

            Enabled = false;
            var gameObject = new GameObject(nameof(PerformanceDiagnostics));
            DontDestroyOnLoad(gameObject);
            _instance = gameObject.AddComponent<PerformanceDiagnostics>();
            CorePerformanceDiagnostics.Enabled = Enabled;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _outputDirectory = CommandLineArgs.PerformanceCsvDirectory;
            _startUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            _monotonicStart = Stopwatch.GetTimestamp();
            _realtimeStart = Time.realtimeSinceStartup;
            _session = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            _framesPath = Path.Combine(_outputDirectory, _session + "_frames.csv");
            _metadataPath = Path.Combine(_outputDirectory, _session + "_metadata.json");
            _rows = new FrameRow[RING_CAPACITY];
            _ftmPending = new PendingFtmCapture[FTM_PENDING_CAPACITY];
            _ftmTimings = new FrameTiming[4];

            try
            {
                Directory.CreateDirectory(_outputDirectory);
                _writer = new StreamWriter(_framesPath, false, new System.Text.UTF8Encoding(false), 65536);
                _writer.WriteLine(CSV_HEADER);
                Enabled = true;
                CorePerformanceDiagnostics.Enabled = true;
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"Unable to open performance diagnostics output: {exception.Message}");
                Enabled = false;
                CorePerformanceDiagnostics.Enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!Enabled || _writer == null)
            {
                return;
            }

            float elapsed = Time.realtimeSinceStartup - _realtimeStart;
            if (elapsed < CommandLineArgs.PerformanceWarmupSeconds)
            {
                return;
            }

            if (!_recordersStarted)
            {
                // Phase 2: recorders start at the same boundary as row emission
                // (end of the warmup window); enumeration happens once, here.
                StartRecorders();
            }

            RecordFrame(elapsed);
            CaptureAndMapFrameTiming();
            if (_rowCount >= FLUSH_CHUNK)
            {
                FlushRows();
            }
        }

        private void OnApplicationQuit()
        {
            // First-writer-wins: keep an exit reason already set this session (e.g.
            // "song_end" from FlushAtSongEnd) instead of overwriting it at quit time.
            FlushAndWriteMetadata(_exitReason ?? "application_quit");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                FlushAndWriteMetadata(_exitReason);
                _instance = null;
            }
        }

        public static void FlushAtSongEnd()
        {
            if (!Enabled || _instance == null)
            {
                return;
            }

            _instance._exitReason = "song_end";
            _instance.FlushRows();
        }

        // Cross-agent contract: records a run-level metadata entry (e.g. "seed",
        // "replayChecksum") that FlushAndWriteMetadata emits as "run_<key>".
        // Safe to call from any thread, before or after the collector enables
        // (and even if it never enables: the map is static and inert while
        // disabled). Last write wins per key; distinct keys beyond
        // RUN_METADATA_CAPACITY are counted in runMetadataDropped and ignored.
        // Null or empty keys are ignored; a null value is stored as "".
        public static void SetRunMetadata(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (_runMetadataLock)
            {
                if (_runMetadata.ContainsKey(key))
                {
                    _runMetadata[key] = value ?? string.Empty;
                    return;
                }

                if (_runMetadataCount >= RUN_METADATA_CAPACITY)
                {
                    _runMetadataDropped++;
                    return;
                }

                _runMetadata[key] = value ?? string.Empty;
                _runMetadataKeys[_runMetadataCount] = key;
                _runMetadataCount++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Timestamp()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ElapsedTicks(long startTicks)
        {
            return Enabled && startTicks != 0 ? Stopwatch.GetTimestamp() - startTicks : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MarkerScope Scope(ProfilerMarker marker)
        {
            if (!Enabled)
            {
                return default;
            }

            marker.Begin();
            return new MarkerScope(marker, true);
        }

        public readonly struct MarkerScope : IDisposable
        {
            private readonly ProfilerMarker _marker;
            private readonly bool _enabled;

            internal MarkerScope(ProfilerMarker marker, bool enabled)
            {
                _marker = marker;
                _enabled = enabled;
            }

            public void Dispose()
            {
                if (_enabled)
                {
                    _marker.End();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClockSample(double visualTime, double inputTime, double songTime, int players)
        {
            if (!Enabled) return;
            Volatile.Write(ref _clockVisualTime, visualTime);
            Volatile.Write(ref _clockInputTime, inputTime);
            Volatile.Write(ref _clockSongTime, songTime);
            Volatile.Write(ref _clockPlayers, players);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CallbackQueueSample(long before, long after)
        {
            if (!Enabled) return;
            Volatile.Write(ref _callbackQueueBefore, before);
            Volatile.Write(ref _callbackQueueAfter, after);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void CallbackDequeued() { if (Enabled) Interlocked.Increment(ref _callbacksInvoked); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void CallbackInvoked() { }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void CallbackException() { if (Enabled) Interlocked.Increment(ref _callbackExceptions); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void CallbackDrainTicks(long ticks) { if (Enabled) Interlocked.Add(ref _callbackDrainTicks, ticks); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void InputSystemUpdated() { if (Enabled) Interlocked.Increment(ref _inputSystemUpdates); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void BindingVisited() { if (Enabled) Interlocked.Increment(ref _bindingsVisited); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void GameInputQueued() { if (Enabled) Interlocked.Increment(ref _gameInputsQueued); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackDueNote() { if (Enabled) Interlocked.Increment(ref _trackDueNotes); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackDueBeatline() { if (Enabled) Interlocked.Increment(ref _trackDueBeatlines); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackDueCoda() { if (Enabled) Interlocked.Increment(ref _trackDueCoda); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackDueUnison() { if (Enabled) Interlocked.Increment(ref _trackDueUnison); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackDueEffect() { if (Enabled) Interlocked.Increment(ref _trackDueEffects); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackPooledTake() { if (Enabled) Interlocked.Increment(ref _trackPooledTake); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackPooledReturn() { if (Enabled) Interlocked.Increment(ref _trackPooledReturn); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackActivated() { if (Enabled) Interlocked.Increment(ref _trackActivations); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackEffectsActive(long count) { if (Enabled) Volatile.Write(ref _trackEffectsActive, count); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void TrackEffectRemoved() { if (Enabled) Interlocked.Increment(ref _trackEffectsRemoved); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void EffectSwapped() { if (Enabled) Interlocked.Increment(ref _effectSwaps); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VocalRangeDue() { if (Enabled) Interlocked.Increment(ref _vocalRangesDue); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VocalLyricDue() { if (Enabled) Interlocked.Increment(ref _vocalLyricsDue); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VocalNoteDue() { if (Enabled) Interlocked.Increment(ref _vocalNotesDue); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VocalPooledTake() { if (Enabled) Interlocked.Increment(ref _vocalPooledTake); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VocalPooledReturn() { if (Enabled) Interlocked.Increment(ref _vocalPooledReturn); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VocalActivated() { if (Enabled) Interlocked.Increment(ref _vocalActivations); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VocalLinePointUpdate() { if (Enabled) Interlocked.Increment(ref _vocalLinePointUpdates); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void StageKitQueueSample(long before, long after) { if (!Enabled) return; Volatile.Write(ref _stageKitQueueBefore, before); Volatile.Write(ref _stageKitQueueAfter, after); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void StageKitCommand() { if (Enabled) Interlocked.Increment(ref _stageKitCommands); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void StageKitKitCount(long count) { if (Enabled) Volatile.Write(ref _stageKitKits, count); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void StageKitSendTicks(long ticks) { if (Enabled) Interlocked.Add(ref _stageKitSendTicks, ticks); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void SacnSend(long expiredChannels) { if (!Enabled) return; Interlocked.Increment(ref _sacnSends); Interlocked.Add(ref _sacnChannels, expiredChannels); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void SacnSendTicks(long ticks) { if (Enabled) Interlocked.Add(ref _sacnSendTicks, ticks); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VrmBounds(long characters, long renderers, long ticks) { if (!Enabled) return; Interlocked.Add(ref _vrmCharacters, characters); Interlocked.Add(ref _vrmRendererCount, renderers); Interlocked.Increment(ref _vrmBoundsUpdates); Interlocked.Add(ref _vrmBoundsTicks, ticks); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void NeonMaterialCount(long count) { if (Enabled) Volatile.Write(ref _neonMaterials, count); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void NeonPropertyWrite(bool unchanged) { if (!Enabled) return; Interlocked.Increment(ref _neonPropertyWrites); if (unchanged) Interlocked.Increment(ref _neonSkippedUnchanged); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void NeonFloatWrite(float previous, float current) { if (Enabled) NeonPropertyWrite(Mathf.Approximately(previous, current)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void NeonColorWrite(Color previous, Color current) { if (Enabled) NeonPropertyWrite(previous == current); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void StarPowerState(bool active) { if (Enabled && active) Interlocked.Increment(ref _starPowerActive); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void StarPowerRendererScan() { if (Enabled) Interlocked.Increment(ref _starPowerRendererScans); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void StarPowerMaterialArrayRead() { if (Enabled) Interlocked.Increment(ref _starPowerMaterialArrayReads); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void HudScoreWrite() { if (Enabled) Interlocked.Increment(ref _hudScoreWrites); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void HudVocalWrite() { if (Enabled) Interlocked.Increment(ref _hudVocalWrites); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void HudLyricWrite() { if (Enabled) Interlocked.Increment(ref _hudLyricWrites); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void HudInputViewerWrite() { if (Enabled) Interlocked.Increment(ref _hudInputViewerWrites); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void HudSetActiveTransition() { if (Enabled) Interlocked.Increment(ref _hudSetActiveTransitions); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void HudSetActive(GameObject gameObject, bool active)
        {
            if (Enabled && gameObject.activeSelf != active) Interlocked.Increment(ref _hudSetActiveTransitions);
            gameObject.SetActive(active);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void DataStreamPacket(long bytes) { if (Enabled) { Interlocked.Increment(ref _dataStreamPackets); Interlocked.Add(ref _dataStreamBytes, bytes); } }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void DataStreamQueueDepthDelta(long delta) { if (Enabled) Interlocked.Add(ref _dataStreamQueueDepth, delta); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void DataStreamSerializeTicks(long ticks) { if (Enabled) Interlocked.Add(ref _dataStreamSerializeTicks, ticks); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void LowFpsCameraRender() { if (Enabled) Interlocked.Increment(ref _lowFpsCameraRenders); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void HighwayPass() { if (Enabled) Interlocked.Increment(ref _highwayPasses); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void PostProcessExpired() { if (Enabled) Interlocked.Increment(ref _postProcessExpired); }

        private void RecordFrame(float elapsed)
        {
            var core = CorePerformanceDiagnostics.TakeSnapshot();
            double refreshRate = Screen.currentResolution.refreshRateRatio.value;
            double targetHz = refreshRate > 0 ? refreshRate : 0;
            double dt = Time.unscaledDeltaTime;
            double visualTime = Volatile.Read(ref _clockVisualTime);
            double visualDt = _hasVisualTime ? visualTime - _lastVisualTime : 0;
            _lastVisualTime = visualTime;
            _hasVisualTime = true;

            FrameRow row = default;
            row.Frame = Interlocked.Increment(ref _frame);
            row.UnityFrame = Time.frameCount;
            row.Realtime = elapsed;
            row.Dt = dt;
            row.VisualDt = visualDt;
            row.InputTime = Volatile.Read(ref _clockInputTime);
            row.SongTime = Volatile.Read(ref _clockSongTime);
            row.VisualTime = visualTime;
            row.TargetHz = targetHz;
            row.Budget = targetHz > 0 ? 1d / targetHz : -1;
            row.MissedRefreshPeriods = targetHz > 0 ? Math.Max(0, (long) Math.Floor(dt * targetHz) - 1) : -1;
            row.GcAllocBytes = SampleGcAllocBytes();
            row.GcCollectionsTotal = GetGcCollectionsTotal();
            row.Players = Volatile.Read(ref _clockPlayers);
            row.CallbackQueueBefore = Interlocked.Exchange(ref _callbackQueueBefore, 0);
            row.CallbackQueueAfter = Interlocked.Exchange(ref _callbackQueueAfter, 0);
            row.CallbacksInvoked = Interlocked.Exchange(ref _callbacksInvoked, 0);
            row.CallbackExceptions = Interlocked.Exchange(ref _callbackExceptions, 0);
            row.CallbackDrainTicks = Interlocked.Exchange(ref _callbackDrainTicks, 0);
            row.CallbackDrain = TicksToSeconds(row.CallbackDrainTicks);
            row.InputSystemUpdates = Interlocked.Exchange(ref _inputSystemUpdates, 0);
            row.BindingsVisited = Interlocked.Exchange(ref _bindingsVisited, 0);
            row.GameInputsQueued = Interlocked.Exchange(ref _gameInputsQueued, 0);
            row.RunQueuedUpdatesCalls = core.RunQueuedUpdatesCalls;
            row.ScheduledBefore = core.ScheduledBefore;
            row.ScheduledGenerated = core.ScheduledGenerated;
            row.ScheduledAfter = core.ScheduledAfter;
            row.ScheduledSortTicks = core.ScheduledSortTicks;
            row.ScheduledSort = TicksToSeconds(row.ScheduledSortTicks);
            row.EngineLoopIterations = core.EngineLoopIterations;
            row.HitChecks = core.HitChecks;
            row.HitNotesInspected = core.HitNotesInspected;
            row.HitNotesInspectedMax = core.HitNotesInspectedMax;
            row.TrackDueNotes = Interlocked.Exchange(ref _trackDueNotes, 0);
            row.TrackDueBeatlines = Interlocked.Exchange(ref _trackDueBeatlines, 0);
            row.TrackDueCoda = Interlocked.Exchange(ref _trackDueCoda, 0);
            row.TrackDueUnison = Interlocked.Exchange(ref _trackDueUnison, 0);
            row.TrackDueEffects = Interlocked.Exchange(ref _trackDueEffects, 0);
            row.TrackPooledTake = Interlocked.Exchange(ref _trackPooledTake, 0);
            row.TrackPooledReturn = Interlocked.Exchange(ref _trackPooledReturn, 0);
            row.TrackActivations = Interlocked.Exchange(ref _trackActivations, 0);
            row.TrackEffectsActive = Volatile.Read(ref _trackEffectsActive);
            row.TrackEffectsRemoved = Interlocked.Exchange(ref _trackEffectsRemoved, 0);
            row.EffectSwaps = Interlocked.Exchange(ref _effectSwaps, 0);
            row.VocalRangesDue = Interlocked.Exchange(ref _vocalRangesDue, 0);
            row.VocalLyricsDue = Interlocked.Exchange(ref _vocalLyricsDue, 0);
            row.VocalNotesDue = Interlocked.Exchange(ref _vocalNotesDue, 0);
            row.VocalPooledTake = Interlocked.Exchange(ref _vocalPooledTake, 0);
            row.VocalActivations = Interlocked.Exchange(ref _vocalActivations, 0);
            row.VocalLinePointUpdates = Interlocked.Exchange(ref _vocalLinePointUpdates, 0);
            row.StageKitQueueBefore = Interlocked.Exchange(ref _stageKitQueueBefore, 0);
            row.StageKitQueueAfter = Interlocked.Exchange(ref _stageKitQueueAfter, 0);
            row.StageKitCommands = Interlocked.Exchange(ref _stageKitCommands, 0);
            row.StageKitKits = Volatile.Read(ref _stageKitKits);
            row.StageKitSendTicks = Interlocked.Exchange(ref _stageKitSendTicks, 0);
            row.StageKitSend = TicksToSeconds(row.StageKitSendTicks);
            row.SacnSends = Interlocked.Exchange(ref _sacnSends, 0);
            row.SacnChannels = Interlocked.Exchange(ref _sacnChannels, 0);
            row.SacnSendTicks = Interlocked.Exchange(ref _sacnSendTicks, 0);
            row.SacnSend = TicksToSeconds(row.SacnSendTicks);
            row.VrmCharacters = Interlocked.Exchange(ref _vrmCharacters, 0);
            row.VrmRendererCount = Interlocked.Exchange(ref _vrmRendererCount, 0);
            row.VrmBoundsUpdates = Interlocked.Exchange(ref _vrmBoundsUpdates, 0);
            row.VrmBoundsTicks = Interlocked.Exchange(ref _vrmBoundsTicks, 0);
            row.VrmBounds = TicksToSeconds(row.VrmBoundsTicks);
            row.NeonMaterials = Volatile.Read(ref _neonMaterials);
            row.NeonPropertyWrites = Interlocked.Exchange(ref _neonPropertyWrites, 0);
            row.NeonSkippedUnchanged = Interlocked.Exchange(ref _neonSkippedUnchanged, 0);
            row.StarPowerActive = Interlocked.Exchange(ref _starPowerActive, 0);
            row.StarPowerRendererScans = Interlocked.Exchange(ref _starPowerRendererScans, 0);
            row.StarPowerMaterialArrayReads = Interlocked.Exchange(ref _starPowerMaterialArrayReads, 0);
            row.HudScoreWrites = Interlocked.Exchange(ref _hudScoreWrites, 0);
            row.HudVocalWrites = Interlocked.Exchange(ref _hudVocalWrites, 0);
            row.HudLyricWrites = Interlocked.Exchange(ref _hudLyricWrites, 0);
            row.HudInputViewerWrites = Interlocked.Exchange(ref _hudInputViewerWrites, 0);
            row.HudSetActiveTransitions = Interlocked.Exchange(ref _hudSetActiveTransitions, 0);
            row.DataStreamPackets = Interlocked.Exchange(ref _dataStreamPackets, 0);
            row.DataStreamBytes = Interlocked.Exchange(ref _dataStreamBytes, 0);
            row.DataStreamQueueDepth = Volatile.Read(ref _dataStreamQueueDepth);
            row.DataStreamSerializeTicks = Interlocked.Exchange(ref _dataStreamSerializeTicks, 0);
            row.DataStreamSerialize = TicksToSeconds(row.DataStreamSerializeTicks);
            row.FtmCpu = -1;
            row.FtmMain = -1;
            row.FtmRender = -1;
            row.FtmGpu = -1;
            row.CanvasBuildBatchSamples = -1;
            row.TmpGenerateMeshSamples = -1;
            row.LowFpsCameraRenders = Interlocked.Exchange(ref _lowFpsCameraRenders, 0);
            row.HighwayPasses = Interlocked.Exchange(ref _highwayPasses, 0);
            row.PostProcessExpired = Interlocked.Exchange(ref _postProcessExpired, 0);
            row.DiagnosticDroppedRows = Volatile.Read(ref _droppedRows);

            int index = (_rowStart + _rowCount) % RING_CAPACITY;
            if (_rowCount == RING_CAPACITY)
            {
                _rowStart = (_rowStart + 1) % RING_CAPACITY;
                index = (_rowStart + _rowCount - 1) % RING_CAPACITY;
                Interlocked.Increment(ref _droppedRows);
            }
            else
            {
                _rowCount++;
            }

            _rows[index] = row;
            _lastRowSlot = index;
            _lastRowFrame = row.Frame;
        }

        private static void ResetFrameCounters()
        {
            Interlocked.Exchange(ref _callbackQueueBefore, 0);
            Interlocked.Exchange(ref _callbackQueueAfter, 0);
            Interlocked.Exchange(ref _callbacksInvoked, 0);
            Interlocked.Exchange(ref _callbackExceptions, 0);
            Interlocked.Exchange(ref _callbackDrainTicks, 0);
            Interlocked.Exchange(ref _inputSystemUpdates, 0);
            Interlocked.Exchange(ref _bindingsVisited, 0);
            Interlocked.Exchange(ref _gameInputsQueued, 0);
            Interlocked.Exchange(ref _trackDueNotes, 0);
            Interlocked.Exchange(ref _trackDueBeatlines, 0);
            Interlocked.Exchange(ref _trackDueCoda, 0);
            Interlocked.Exchange(ref _trackDueUnison, 0);
            Interlocked.Exchange(ref _trackDueEffects, 0);
            Interlocked.Exchange(ref _trackPooledTake, 0);
            Interlocked.Exchange(ref _trackPooledReturn, 0);
            Interlocked.Exchange(ref _trackActivations, 0);
            Interlocked.Exchange(ref _trackEffectsRemoved, 0);
            Interlocked.Exchange(ref _effectSwaps, 0);
            Interlocked.Exchange(ref _vocalRangesDue, 0);
            Interlocked.Exchange(ref _vocalLyricsDue, 0);
            Interlocked.Exchange(ref _vocalNotesDue, 0);
            Interlocked.Exchange(ref _vocalPooledTake, 0);
            Interlocked.Exchange(ref _vocalPooledReturn, 0);
            Interlocked.Exchange(ref _vocalActivations, 0);
            Interlocked.Exchange(ref _vocalLinePointUpdates, 0);
            Interlocked.Exchange(ref _stageKitQueueBefore, 0);
            Interlocked.Exchange(ref _stageKitQueueAfter, 0);
            Interlocked.Exchange(ref _stageKitCommands, 0);
            Interlocked.Exchange(ref _stageKitSendTicks, 0);
            Interlocked.Exchange(ref _sacnSends, 0);
            Interlocked.Exchange(ref _sacnChannels, 0);
            Interlocked.Exchange(ref _sacnSendTicks, 0);
            Interlocked.Exchange(ref _vrmCharacters, 0);
            Interlocked.Exchange(ref _vrmRendererCount, 0);
            Interlocked.Exchange(ref _vrmBoundsUpdates, 0);
            Interlocked.Exchange(ref _vrmBoundsTicks, 0);
            Interlocked.Exchange(ref _neonPropertyWrites, 0);
            Interlocked.Exchange(ref _neonSkippedUnchanged, 0);
            Interlocked.Exchange(ref _starPowerActive, 0);
            Interlocked.Exchange(ref _starPowerRendererScans, 0);
            Interlocked.Exchange(ref _starPowerMaterialArrayReads, 0);
            Interlocked.Exchange(ref _hudScoreWrites, 0);
            Interlocked.Exchange(ref _hudVocalWrites, 0);
            Interlocked.Exchange(ref _hudLyricWrites, 0);
            Interlocked.Exchange(ref _hudInputViewerWrites, 0);
            Interlocked.Exchange(ref _hudSetActiveTransitions, 0);
            Interlocked.Exchange(ref _dataStreamPackets, 0);
            Interlocked.Exchange(ref _dataStreamBytes, 0);
            Interlocked.Exchange(ref _dataStreamSerializeTicks, 0);
            Interlocked.Exchange(ref _lowFpsCameraRenders, 0);
            Interlocked.Exchange(ref _highwayPasses, 0);
            Interlocked.Exchange(ref _postProcessExpired, 0);
        }

        private static long GetGcCollectionsTotal()
        {
            long total = 0;
            for (int generation = 0; generation <= GC.MaxGeneration; generation++)
            {
                total += GC.CollectionCount(generation);
            }

            return total;
        }

        // Phase 2: enumerate the Profiler stats that actually exist in this Player
        // and start ProfilerRecorders for the sought signals. Never hard-code
        // handles: the available names vary per Unity version and platform.
        // Enumeration uses Unity.Profiling.LowLevel.Unsafe, which requires an
        // unsafe context; the project allows unsafe code (Assembly-CSharp,
        // ProjectSettings allowUnsafeCode=1, no asmdef override under
        // Assets/Script), so the enumeration API is used directly. This is
        // one-time init at the warmup boundary: allocation here is acceptable and
        // nothing below runs per frame.
        private void StartRecorders()
        {
            _recordersStarted = true;
            ResetFrameCounters();

            var handles = new List<ProfilerRecorderHandle>();
            var names = new List<string>();
            var categories = new List<string>();

            try
            {
                unsafe
                {
                    ProfilerRecorderHandle.GetAvailable(handles);
                    for (int i = 0; i < handles.Count; i++)
                    {
                        var description = ProfilerRecorderHandle.GetDescription(handles[i]);
                        names.Add(description.Name);
                        categories.Add(description.Category.ToString());
                    }
                }
            }
            catch (Exception exception)
            {
                // Enumeration failed in this build: leave everything unfound. Rows
                // then carry -1 gcAllocBytes and the metadata records why.
                UnityEngine.Debug.LogWarning($"PerformanceDiagnostics recorder enumeration failed: {exception.Message}");
                return;
            }

            unsafe
            {
                StartGcAllocRecorder(handles, names, categories);
                StartMainThreadRecorder(handles, names, categories);
                StartGcCollectionRecorder(handles, names, categories);
            }
        }

        // Diagnostic: when a sought stat name has no match in this build, a full
        // dump of every available handle name so the real name can be found offline.
        private string _recorderAvailableNamesDump;

        private unsafe void StartGcAllocRecorder(List<ProfilerRecorderHandle> handles, List<string> names, List<string> categories)
        {
            int index = FindStatIndex(names, categories, GC_ALLOC_CANDIDATES, GC_ALLOC_PREFERRED_CATEGORIES);
            if (index < 0)
            {
                RecordAvailableStatNames(names);
                return;
            }

            var description = ProfilerRecorderHandle.GetDescription(handles[index]);
            _gcAllocRecorder = new ProfilerRecorder(description.Category, description.Name, 1);
            _gcAllocRecorder.Start();
            _gcAllocRecorderValid = _gcAllocRecorder.Valid;
            _gcAllocStatName = description.Name;
            _gcAllocStatCategory = categories[index];
            _gcAllocFound = _gcAllocRecorderValid;
            // "…In Frame Total" names are monotonic accumulators, not per-frame values;
            // exclude them from the per-frame classification.
            _gcAllocIsPerFrameCounter =
                description.Name.IndexOf("in frame", StringComparison.OrdinalIgnoreCase) >= 0 &&
                description.Name.IndexOf("total", StringComparison.OrdinalIgnoreCase) < 0;
            _gcAllocPreviousValue = _gcAllocRecorder.Valid ? _gcAllocRecorder.CurrentValueAsDouble : 0;
        }

        // One-time, enabled-path-only diagnostic helper (see _recorderAvailableNamesDump).
        private void RecordAvailableStatNames(List<string> names)
        {
            if (_recorderAvailableNamesDump != null || names.Count == 0)
            {
                return;
            }

            // Full dump, no truncation: a bounded sample hid the sought GC-alloc stat
            // on a build that exposes 221 names, and the sidecar can afford ~2 KB.
            string joined = string.Join(",", names);
            _recorderAvailableNamesDump = names.Count.ToString(CultureInfo.InvariantCulture) + ":" + joined;
        }

        private unsafe void StartMainThreadRecorder(List<ProfilerRecorderHandle> handles, List<string> names, List<string> categories)
        {
            int index = FindStatIndex(names, categories, MAIN_THREAD_CANDIDATES, MAIN_THREAD_PREFERRED_CATEGORIES);
            if (index < 0)
            {
                return;
            }

            var description = ProfilerRecorderHandle.GetDescription(handles[index]);
            _mainThreadRecorder = new ProfilerRecorder(description.Category, description.Name, 1);
            _mainThreadRecorder.Start();
            _mainThreadRecorderValid = _mainThreadRecorder.Valid;
            _mainThreadStatName = description.Name;
            _mainThreadStatCategory = categories[index];
            _mainThreadFound = _mainThreadRecorderValid;
        }

        private unsafe void StartGcCollectionRecorder(List<ProfilerRecorderHandle> handles, List<string> names, List<string> categories)
        {
            int index = FindStatIndex(names, categories, GC_COLLECTION_CANDIDATES, GC_COLLECTION_PREFERRED_CATEGORIES);
            if (index < 0)
            {
                return;
            }

            var description = ProfilerRecorderHandle.GetDescription(handles[index]);
            _gcCollectionRecorder = new ProfilerRecorder(description.Category, description.Name, 1);
            _gcCollectionRecorder.Start();
            _gcCollectionRecorderValid = _gcCollectionRecorder.Valid;
            _gcCollectionStatName = description.Name;
            _gcCollectionStatCategory = categories[index];
            _gcCollectionFound = _gcCollectionRecorderValid;
        }

        private static int FindStatIndex(List<string> names, List<string> categories, string[] candidates, string[] preferredCategories)
        {
            for (int candidate = 0; candidate < candidates.Length; candidate++)
            {
                int bestIndex = -1;
                bool bestPreferred = false;
                for (int i = 0; i < names.Count; i++)
                {
                    if (!string.Equals(names[i], candidates[candidate], StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool preferred = MatchesAny(categories[i], preferredCategories);
                    if (bestIndex < 0 || (preferred && !bestPreferred))
                    {
                        bestIndex = i;
                        bestPreferred = preferred;
                    }
                }

                if (bestIndex >= 0)
                {
                    return bestIndex;
                }
            }

            return -1;
        }

        private static bool MatchesAny(string value, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(value, candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // gcAllocBytes sampling mode, chosen from the matched stat's name:
        // - "in frame" stats (Unity's per-frame accumulator convention, e.g.
        //   "GC.Alloc In Frame Count") reset each frame, so the row's value is read
        //   directly from CurrentValueAsDouble at this LateUpdate. The row is
        //   recorded at the very end of the frame's script phase, so this is the
        //   frame's managed allocation up to the same sampling point as every
        //   other column in the row. LastValueAsDouble is deliberately not used:
        //   it reports the previous frame's end-of-frame sample.
        // - Any other matched name is treated as a monotonic accumulator
        //   (cumulative bytes); the row reports the delta of CurrentValueAsDouble
        //   vs the previous row. A negative delta would mean the counter resets
        //   per frame after all, in which case the raw current value is reported.
        // Unlike the deleted M1 GC.GetAllocatedBytesForCurrentThread() stub, this
        // counts managed allocation on all threads, not only the current one.
        // A missing or invalid handle writes exactly -1.
        private long SampleGcAllocBytes()
        {
            if (!_gcAllocRecorder.Valid)
            {
                return -1;
            }

            double value = _gcAllocRecorder.CurrentValueAsDouble;
            if (_gcAllocIsPerFrameCounter)
            {
                return (long) value;
            }

            double delta = value - _gcAllocPreviousValue;
            _gcAllocPreviousValue = value;
            return (long) (delta >= 0 ? delta : value);
        }

        private void StopRecorders()
        {
            if (!_recordersStarted)
            {
                return;
            }

            _recordersStarted = false;
            _gcAllocRecorder.Dispose();
            _mainThreadRecorder.Dispose();
            _gcCollectionRecorder.Dispose();
        }

        // Phase 3: FrameTimingManager capture with delayed mapping.
        //
        // FrameTimingManager returns a frame's CPU/GPU timing with a fixed delay
        // of four frames (no data for the current frame), and Unity 6 removed
        // FrameTiming.frameIndex, so samples carry no frame identity to key on.
        // Mapping is therefore positional: every recorded frame pushes a pending
        // entry onto a preallocated 16-entry ring, and the single latest sample
        // (GetLatestTimings(1)) is applied to the newest unconsumed entry old
        // enough to own it (captured at least FTM_FRAME_DELAY frames ago). The
        // row is then patched in place in the row ring, before it can be flushed
        // in the normal case (the 4096-row flush interval vastly exceeds the
        // 4-frame delay).
        //
        // Known limitation (accepted; diagnostics-grade): if the platform drops
        // a frame's timing, the next sample is applied to the stale entry and
        // subsequent samples run one frame late until delivery catches up; a
        // platform whose delay differs from four frames shifts the fill by
        // (delay - 4) rows. Without frameIndex there is no way to detect or
        // correct either case at runtime; ftmUnmatchedSamples and
        // ftmMappingMisses in the metadata make the anomalies visible.
        private void CaptureAndMapFrameTiming()
        {
            FrameTimingManager.CaptureFrameTimings();
            PushPendingFtmCapture(_lastRowFrame, Time.frameCount, _lastRowSlot);
            ConsumeFrameTiming(Time.frameCount);
            CompactPendingFtmCaptures();
        }

        private void PushPendingFtmCapture(long frame, int unityFrame, int rowSlot)
        {
            if (_ftmPendingCount == FTM_PENDING_CAPACITY)
            {
                // Oldest entry expired without ever receiving a sample.
                _ftmPendingHead = (_ftmPendingHead + 1) % FTM_PENDING_CAPACITY;
                _ftmPendingCount--;
                _ftmMappingMisses++;
            }

            int slot = (_ftmPendingHead + _ftmPendingCount) % FTM_PENDING_CAPACITY;
            _ftmPending[slot] = new PendingFtmCapture
            {
                Frame = frame,
                UnityFrame = unityFrame,
                RowSlot = rowSlot,
            };
            _ftmPendingCount++;
        }

        private void ConsumeFrameTiming(int currentUnityFrame)
        {
            if (FrameTimingManager.GetLatestTimings(1, _ftmTimings) == 0)
            {
                return;
            }

            // The sample belongs to the frame captured FTM_FRAME_DELAY frames ago;
            // take the newest unconsumed entry old enough to be its owner (this
            // also backfills an entry whose own sample was dropped).
            int oldestAllowed = currentUnityFrame - FTM_FRAME_DELAY;
            int entry = -1;
            for (int i = _ftmPendingCount - 1; i >= 0; i--)
            {
                int slot = (_ftmPendingHead + i) % FTM_PENDING_CAPACITY;
                if (!_ftmPending[slot].Consumed && _ftmPending[slot].UnityFrame <= oldestAllowed)
                {
                    entry = slot;
                    break;
                }
            }

            if (entry < 0)
            {
                // Nothing pending is old enough: the sample predates the pending
                // window (e.g. the warmup boundary) or repeats one already applied.
                _ftmUnmatchedSamples++;
                return;
            }

            FrameTiming timing = _ftmTimings[0];
            _ftmSamples++;
            if (timing.cpuFrameTime == 0 && timing.gpuFrameTime == 0 &&
                timing.cpuMainThreadFrameTime == 0 && timing.cpuRenderThreadFrameTime == 0)
            {
                // All-zero is the documented signature of frame-timing stats being
                // unavailable in this build; counted separately from mapping
                // misses. The entry is consumed and its row keeps the -1 sentinel.
                _ftmPending[entry].Consumed = true;
                _ftmZeroSamples++;
                return;
            }

            if (_ftmPending[entry].UnityFrame <= _ftmLastSampleUnityFrame)
            {
                // Delivery-order guard: this entry's frame was already filled by a
                // newer sample, so this one is a duplicate; do not fill an older
                // row with it.
                _ftmPending[entry].Consumed = true;
                _ftmUnmatchedSamples++;
                return;
            }

            _ftmPending[entry].Consumed = true;
            _ftmLastSampleUnityFrame = _ftmPending[entry].UnityFrame;
            _ftmNonzeroSamplesSeen++;
            ApplyFrameTimingToRow(_ftmPending[entry].Frame, _ftmPending[entry].RowSlot, timing);
        }

        private void ApplyFrameTimingToRow(long frame, int rowSlot, FrameTiming timing)
        {
            if (frame <= _flushedThroughFrame || _rows[rowSlot].Frame != frame)
            {
                // The row was already flushed to the CSV (chunk boundary inside the
                // delay window) or evicted; it stays -1 on disk and counts as a
                // mapping miss. End-of-run flushes naturally leave the last few
                // rows -1 for the same reason; flush is never blocked.
                _ftmMappingMisses++;
                return;
            }

            // FrameTiming fields are documented (and observed: max cpuFrameTime
            // matches max dt_s) in MILLISECONDS; the CSV columns are seconds.
            _rows[rowSlot].FtmCpu = timing.cpuFrameTime / 1000.0;
            _rows[rowSlot].FtmMain = timing.cpuMainThreadFrameTime / 1000.0;
            _rows[rowSlot].FtmRender = timing.cpuRenderThreadFrameTime / 1000.0;
            _rows[rowSlot].FtmGpu = timing.gpuFrameTime / 1000.0;
        }

        private void CompactPendingFtmCaptures()
        {
            while (_ftmPendingCount > 0 && _ftmPending[_ftmPendingHead].Consumed)
            {
                _ftmPendingHead = (_ftmPendingHead + 1) % FTM_PENDING_CAPACITY;
                _ftmPendingCount--;
            }
        }

        private static double TicksToSeconds(long ticks)
        {
            return ticks <= 0 ? 0 : ticks / (double) Stopwatch.Frequency;
        }

        private void FlushRows()
        {
            if (_writer == null || _rowCount == 0)
            {
                return;
            }

            int count = _rowCount;
            for (int i = 0; i < count; i++)
            {
                WriteRow(_rows[(_rowStart + i) % RING_CAPACITY]);
            }

            _flushedThroughFrame = _rows[(_rowStart + count - 1) % RING_CAPACITY].Frame;
            _rowStart = 0;
            _rowCount = 0;
            _writer.Flush();
        }

        private void WriteRow(FrameRow row)
        {
            _writer.Write(SCHEMA_VERSION);
            _writer.Write(',');
            _writer.Write(_session);
            _writer.Write(','); WriteLong(row.Frame);
            _writer.Write(','); WriteLong(row.UnityFrame);
            _writer.Write(','); WriteDouble(row.Realtime);
            _writer.Write(",measured,0,");
            WriteDouble(row.Dt); _writer.Write(','); WriteDouble(row.VisualDt); _writer.Write(',');
            WriteDouble(row.InputTime); _writer.Write(','); WriteDouble(row.SongTime); _writer.Write(','); WriteDouble(row.VisualTime); _writer.Write(',');
            WriteDouble(row.TargetHz); _writer.Write(','); WriteDouble(row.Budget); _writer.Write(','); WriteLong(row.MissedRefreshPeriods); _writer.Write(',');
            WriteDouble(row.FtmCpu); _writer.Write(','); WriteDouble(row.FtmMain); _writer.Write(','); WriteDouble(row.FtmRender); _writer.Write(','); WriteDouble(row.FtmGpu); _writer.Write(',');
            WriteLong(row.GcAllocBytes); _writer.Write(','); WriteLong(row.GcCollectionsTotal); _writer.Write(',');
            WriteLong(row.CallbackQueueBefore); _writer.Write(','); WriteLong(row.CallbackQueueAfter); _writer.Write(','); WriteLong(row.CallbacksInvoked); _writer.Write(','); WriteLong(row.CallbackExceptions); _writer.Write(','); WriteLong(row.CallbackDrainTicks); _writer.Write(','); WriteDouble(row.CallbackDrain); _writer.Write(',');
            WriteLong(row.Players); _writer.Write(','); WriteLong(row.InputSystemUpdates); _writer.Write(','); WriteLong(row.BindingsVisited); _writer.Write(','); WriteLong(row.GameInputsQueued); _writer.Write(','); WriteLong(row.RunQueuedUpdatesCalls); _writer.Write(','); WriteLong(row.ScheduledBefore); _writer.Write(','); WriteLong(row.ScheduledGenerated); _writer.Write(','); WriteLong(row.ScheduledAfter); _writer.Write(','); WriteLong(row.ScheduledSortTicks); _writer.Write(','); WriteDouble(row.ScheduledSort); _writer.Write(','); WriteLong(row.EngineLoopIterations); _writer.Write(',');
            WriteLong(row.HitChecks); _writer.Write(','); WriteLong(row.HitNotesInspected); _writer.Write(','); WriteLong(row.HitNotesInspectedMax); _writer.Write(',');
            WriteLong(row.TrackDueNotes); _writer.Write(','); WriteLong(row.TrackDueBeatlines); _writer.Write(','); WriteLong(row.TrackDueCoda); _writer.Write(','); WriteLong(row.TrackDueUnison); _writer.Write(','); WriteLong(row.TrackDueEffects); _writer.Write(','); WriteLong(row.TrackPooledTake); _writer.Write(','); WriteLong(row.TrackPooledReturn); _writer.Write(','); WriteLong(row.TrackActivations); _writer.Write(','); WriteLong(row.TrackEffectsActive); _writer.Write(','); WriteLong(row.TrackEffectsRemoved); _writer.Write(','); WriteLong(row.EffectSwaps); _writer.Write(',');
            WriteLong(row.VocalRangesDue); _writer.Write(','); WriteLong(row.VocalLyricsDue); _writer.Write(','); WriteLong(row.VocalNotesDue); _writer.Write(','); WriteLong(row.VocalPooledTake); _writer.Write(','); WriteLong(row.VocalActivations); _writer.Write(','); WriteLong(row.VocalLinePointUpdates); _writer.Write(',');
            WriteLong(row.StageKitQueueBefore); _writer.Write(','); WriteLong(row.StageKitQueueAfter); _writer.Write(','); WriteLong(row.StageKitCommands); _writer.Write(','); WriteLong(row.StageKitKits); _writer.Write(','); WriteLong(row.StageKitSendTicks); _writer.Write(','); WriteDouble(row.StageKitSend); _writer.Write(',');
            WriteLong(row.SacnSends); _writer.Write(','); WriteLong(row.SacnChannels); _writer.Write(','); WriteLong(row.SacnSendTicks); _writer.Write(','); WriteDouble(row.SacnSend); _writer.Write(',');
            WriteLong(row.VrmCharacters); _writer.Write(','); WriteLong(row.VrmRendererCount); _writer.Write(','); WriteLong(row.VrmBoundsUpdates); _writer.Write(','); WriteLong(row.VrmBoundsTicks); _writer.Write(','); WriteDouble(row.VrmBounds); _writer.Write(',');
            WriteLong(row.NeonMaterials); _writer.Write(','); WriteLong(row.NeonPropertyWrites); _writer.Write(','); WriteLong(row.NeonSkippedUnchanged); _writer.Write(',');
            WriteLong(row.StarPowerActive); _writer.Write(','); WriteLong(row.StarPowerRendererScans); _writer.Write(','); WriteLong(row.StarPowerMaterialArrayReads); _writer.Write(',');
            WriteLong(row.HudScoreWrites); _writer.Write(','); WriteLong(row.HudVocalWrites); _writer.Write(','); WriteLong(row.HudLyricWrites); _writer.Write(','); WriteLong(row.HudInputViewerWrites); _writer.Write(','); WriteLong(row.HudSetActiveTransitions); _writer.Write(',');
            WriteLong(row.DataStreamPackets); _writer.Write(','); WriteLong(row.DataStreamBytes); _writer.Write(','); WriteLong(row.DataStreamQueueDepth); _writer.Write(','); WriteLong(row.DataStreamSerializeTicks); _writer.Write(','); WriteDouble(row.DataStreamSerialize); _writer.Write(',');
            WriteLong(row.CanvasBuildBatchSamples); _writer.Write(','); WriteLong(row.TmpGenerateMeshSamples); _writer.Write(','); WriteLong(row.LowFpsCameraRenders); _writer.Write(','); WriteLong(row.HighwayPasses); _writer.Write(','); WriteLong(row.PostProcessExpired); _writer.Write(','); WriteLong(row.DiagnosticDroppedRows);
            _writer.WriteLine();
        }

        private void WriteLong(long value)
        {
            if (!value.TryFormat(_formatBuffer.AsSpan(), out int written, default, CultureInfo.InvariantCulture))
            {
                _writer.Write(value.ToString(CultureInfo.InvariantCulture));
                return;
            }

            _writer.Write(_formatBuffer, 0, written);
        }

        private void WriteDouble(double value)
        {
            if (!value.TryFormat(_formatBuffer.AsSpan(), out int written, "R", CultureInfo.InvariantCulture))
            {
                _writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            _writer.Write(_formatBuffer, 0, written);
        }

        private void FlushAndWriteMetadata(string reason)
        {
            if (_writer == null)
            {
                return;
            }

            _exitReason = reason;
            FlushRows();
            _writer.Dispose();
            _writer = null;
            StopRecorders();

            try
            {
                using var metadata = new StreamWriter(_metadataPath, false, new System.Text.UTF8Encoding(false));
                metadata.WriteLine("{");
                WriteJson(metadata, "schemaVersion", SCHEMA_VERSION.ToString(CultureInfo.InvariantCulture), false);
                WriteJson(metadata, "session", _session, false);
                WriteJson(metadata, "utcStart", _startUtc, false);
                WriteJson(metadata, "monotonicStartTicks", _monotonicStart.ToString(CultureInfo.InvariantCulture), false);
                WriteJson(metadata, "applicationVersion", Application.version, false);
                WriteJson(metadata, "buildGuid", Application.buildGUID, false);
                WriteJson(metadata, "unityVersion", Application.unityVersion, false);
                WriteJson(metadata, "platform", Application.platform.ToString(), false);
                WriteJson(metadata, "os", SystemInfo.operatingSystem, false);
                WriteJson(metadata, "deviceModel", SystemInfo.deviceModel, false);
                WriteJson(metadata, "commandLine", string.Join(" ", CommandLineArgs.RawArguments), false);
                WriteJson(metadata, "warmupSeconds", CommandLineArgs.PerformanceWarmupSeconds.ToString(CultureInfo.InvariantCulture), false);
                WriteJson(metadata, "exitReason", _exitReason, false);
                WriteJson(metadata, "droppedRows", Volatile.Read(ref _droppedRows).ToString(CultureInfo.InvariantCulture), false);
                WriteRecorderHandlesMetadata(metadata);
                WriteJson(metadata, "ftmEnabled", _ftmNonzeroSamplesSeen > 0 ? "true" : "false", false);
                WriteJson(metadata, "ftmSamples", _ftmSamples.ToString(CultureInfo.InvariantCulture), false);
                WriteJson(metadata, "ftmZeroSamples", _ftmZeroSamples.ToString(CultureInfo.InvariantCulture), false);
                WriteJson(metadata, "ftmUnmatchedSamples", _ftmUnmatchedSamples.ToString(CultureInfo.InvariantCulture), false);
                bool hasRunMetadata;
                lock (_runMetadataLock)
                {
                    hasRunMetadata = _runMetadataCount > 0 || _runMetadataDropped > 0;
                }

                WriteJson(metadata, "ftmMappingMisses", _ftmMappingMisses.ToString(CultureInfo.InvariantCulture), !hasRunMetadata);
                if (hasRunMetadata)
                {
                    WriteRunMetadata(metadata);
                }

                metadata.WriteLine("}");
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"Unable to write performance diagnostics metadata: {exception.Message}");
            }
        }

        private static void WriteJson(StreamWriter writer, string name, string value, bool last)
        {
            writer.Write("  \"");
            writer.Write(EscapeJson(name));
            writer.Write("\": \"");
            writer.Write(EscapeJson(value));
            writer.Write(last ? "\"\n" : "\",\n");
        }

        private static string EscapeJson(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        // recorderHandles: one entry per sought stat — whether enumeration found a
        // matching stat, the actual name and category it matched, and whether the
        // ProfilerRecorder constructed from it reported Valid at start time
        // (captured then, because the recorders are disposed before the metadata
        // is written). mainThreadTime and gcCollections are corroboration-only
        // signals: they have no CSV column and are never read per frame.
        private void WriteRecorderHandlesMetadata(StreamWriter metadata)
        {
            metadata.Write("  \"recorderHandles\": [\n");
            WriteRecorderHandleEntry(metadata, "gcAllocBytes", _gcAllocFound, _gcAllocStatName, _gcAllocStatCategory, _gcAllocRecorderValid, false);
            WriteRecorderHandleEntry(metadata, "mainThreadTime", _mainThreadFound, _mainThreadStatName, _mainThreadStatCategory, _mainThreadRecorderValid, false);
            WriteRecorderHandleEntry(metadata, "gcCollections", _gcCollectionFound, _gcCollectionStatName, _gcCollectionStatCategory, _gcCollectionRecorderValid, true);
            metadata.Write("  ],\n");
            if (_recorderAvailableNamesDump != null)
            {
                // Bounded comma-separated dump of this build's available stat names,
                // recorded because at least one sought name had no match here.
                WriteJson(metadata, "recorderAvailableNames", _recorderAvailableNamesDump, false);
            }
        }

        private static void WriteRecorderHandleEntry(StreamWriter writer, string sought, bool found, string name, string category, bool valid, bool last)
        {
            writer.Write("    {\"sought\": \"");
            writer.Write(EscapeJson(sought));
            writer.Write("\", \"name\": \"");
            writer.Write(EscapeJson(name));
            writer.Write("\", \"category\": \"");
            writer.Write(EscapeJson(category));
            writer.Write("\", \"found\": ");
            writer.Write(found ? "true" : "false");
            writer.Write(", \"valid\": ");
            writer.Write(valid ? "true" : "false");
            writer.Write(last ? "}\n" : "},\n");
        }

        // run_<key> entries from SetRunMetadata, written last so "run_" data
        // bookends the fixed metadata. The caller guarantees at least one entry is
        // written here, keeping trailing-comma placement valid.
        private void WriteRunMetadata(StreamWriter metadata)
        {
            lock (_runMetadataLock)
            {
                if (_runMetadataDropped > 0)
                {
                    WriteJson(metadata, "runMetadataDropped", _runMetadataDropped.ToString(CultureInfo.InvariantCulture), _runMetadataCount == 0);
                }

                for (int i = 0; i < _runMetadataCount; i++)
                {
                    string key = _runMetadataKeys[i];
                    WriteJson(metadata, "run_" + key, _runMetadata[key], i == _runMetadataCount - 1);
                }
            }
        }

        // One FrameTimingManager capture request awaiting its delayed sample.
        // Frame is the session frame counter (matches FrameRow.Frame), UnityFrame
        // is Time.frameCount at capture, and RowSlot points at the row's slot in
        // the row ring. The slot is only trusted while _rows[RowSlot].Frame still
        // equals Frame (slots are reused once rows are flushed or evicted).
        private struct PendingFtmCapture
        {
            public long Frame;
            public int UnityFrame;
            public int RowSlot;
            public bool Consumed;
        }

        private struct FrameRow
        {
            public long Frame, UnityFrame, MissedRefreshPeriods, GcAllocBytes, GcCollectionsTotal;
            public double Realtime, Dt, VisualDt, InputTime, SongTime, VisualTime, TargetHz, Budget;
            public double FtmCpu, FtmMain, FtmRender, FtmGpu;
            public long CallbackQueueBefore, CallbackQueueAfter, CallbacksInvoked, CallbackExceptions, CallbackDrainTicks;
            public double CallbackDrain;
            public long Players, InputSystemUpdates, BindingsVisited, GameInputsQueued, RunQueuedUpdatesCalls;
            public long ScheduledBefore, ScheduledGenerated, ScheduledAfter, ScheduledSortTicks, EngineLoopIterations;
            public double ScheduledSort;
            public long HitChecks, HitNotesInspected, HitNotesInspectedMax;
            public long TrackDueNotes, TrackDueBeatlines, TrackDueCoda, TrackDueUnison, TrackDueEffects, TrackPooledTake, TrackPooledReturn, TrackActivations, TrackEffectsActive, TrackEffectsRemoved, EffectSwaps;
            public long VocalRangesDue, VocalLyricsDue, VocalNotesDue, VocalPooledTake, VocalActivations, VocalLinePointUpdates;
            public long StageKitQueueBefore, StageKitQueueAfter, StageKitCommands, StageKitKits, StageKitSendTicks;
            public double StageKitSend;
            public long SacnSends, SacnChannels, SacnSendTicks;
            public double SacnSend;
            public long VrmCharacters, VrmRendererCount, VrmBoundsUpdates, VrmBoundsTicks;
            public double VrmBounds;
            public long NeonMaterials, NeonPropertyWrites, NeonSkippedUnchanged;
            public long StarPowerActive, StarPowerRendererScans, StarPowerMaterialArrayReads;
            public long HudScoreWrites, HudVocalWrites, HudLyricWrites, HudInputViewerWrites, HudSetActiveTransitions;
            public long DataStreamPackets, DataStreamBytes, DataStreamQueueDepth, DataStreamSerializeTicks;
            public double DataStreamSerialize;
            public long CanvasBuildBatchSamples, TmpGenerateMeshSamples, LowFpsCameraRenders, HighwayPasses, PostProcessExpired, DiagnosticDroppedRows;
        }
    }
}
