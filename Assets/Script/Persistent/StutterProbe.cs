// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using YARG.PerformanceTesting;
using UnityEngine;
using UnityEngine.SceneManagement;
using YARG.Gameplay;
using YARG.Core.Diagnostics;
using YARG.Settings;
using Debug = UnityEngine.Debug;

namespace YARG.Persistent
{
    /// <summary>Menu and gameplay cadence sampler. Graph and disk capture are independent consumers.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class StutterProbe : MonoBehaviour
    {
        public const int HISTORY_SIZE = 480;
        public static StutterProbe Instance { get; private set; }
        public readonly double[] History = new double[HISTORY_SIZE];
        public FrameGraphHistory GraphHistory { get; } = new();
        public int SampleCount { get; private set; }
        public int Head { get; private set; }
        public FrameDiagnosticSample Latest { get; private set; }
        public string CapturePath => _capture?.Path;
        public string CaptureError => _capture?.Error ?? _lastCaptureError;
        public int DroppedRows => _capture?.DroppedRows ?? 0;
        public int FinishingCaptures => _finishing.Count;

        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private FrameTimingCapture _capture;
        private GcFrameDiagnostics _gcDiagnostics;
        private CpuPhaseDiagnostics _cpuPhases;
        private string _cpuPhaseCapturePath;
        private bool _cpuPhasesFailed;
        private string _metricsCapturePath;
        private bool _gcDiagnosticsFailed;
        private RenderFrameDiagnostics _renderDiagnostics;
        private string _renderMetricsCapturePath;
        private bool _renderDiagnosticsFailed;
        private ulong _cpuTimerFrequency;
        private readonly List<FrameTimingCapture> _finishing = new();
        private string _lastCaptureError;
        private bool _sampling;
        private bool _hasPreviousSample;
        private double _lastTime = double.NaN;
        private ulong _lastTimingTimestamp;
        private int _sceneHandle = int.MinValue;
        private GameManager _gameManager;
        private double _lastVisual;
        private bool _hasVisual;
        private string _graphicsApi;
        private bool _reportedCaptureError;
        private int _lastSampleFrame;
        private long _targetedGeneration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("StutterProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<StutterProbe>();
        }

        private void Awake() => Instance = this;

        private void Update()
        {
            CheckFinishedCaptures();
            var settings = SettingsManager.Settings;
            bool ready = settings != null && SettingsManager.SettingContainer.IsInitialized;
            TargetedPerformance.SetEnabled(ready && settings.TargetedPerformanceChecks.Value);
            bool logging = ready && settings.TimingDiagnostics.Value;
            if (!logging) StopCapture();
            else if (_capture == null)
            {
                var path = Path.Combine(PerformanceFixtureLaunch.Requested ? PerformanceFixtureLaunch.OutputDirectory : Application.persistentDataPath, "stutter-profile",
                    $"stutter-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.csv");
                _capture = new FrameTimingCapture(path,
                    PerformanceFixtureLaunch.Requested && PerformanceFixtureLaunch.Definition.DeferTimingWrites);
                _lastCaptureError = null;
                _reportedCaptureError = false;
                Debug.Log($"[StutterProbe] capturing menus and gameplay to {path}; Unity={Application.unityVersion}; GPU={SystemInfo.graphicsDeviceName}; OS={SystemInfo.operatingSystem}");
            }

            bool enabled = ready && (settings.FrameGraph.Value || logging);
            UpdateRenderDiagnostics(enabled, TargetedPerformance.Enabled);
            UpdateCpuPhases(enabled && TargetedPerformance.Enabled);
            if (!enabled || !TargetedPerformance.Enabled)
            {
                _gcDiagnostics?.Dispose();
                _gcDiagnostics = null;
                _metricsCapturePath = null;
            }
            else if (_gcDiagnostics == null && !_gcDiagnosticsFailed)
            {
                try
                {
                    _gcDiagnostics = new GcFrameDiagnostics();
                    Debug.Log("[GcFrameDiagnostics] " + _gcDiagnostics.Capabilities);
                }
                catch (Exception error)
                {
                    _gcDiagnosticsFailed = true;
                    Debug.LogWarning("[GcFrameDiagnostics] unavailable for this session: " + error.Message);
                }
            }
            if (_gcDiagnostics != null && _capture != null && _metricsCapturePath != _capture.Path)
            {
                _metricsCapturePath = _capture.Path;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_capture.Path));
                    File.WriteAllText(_capture.Path + ".metrics.json", _gcDiagnostics.Capabilities);
                }
                catch (IOException error) { Debug.LogWarning("GC metric metadata: " + error.Message); }
                catch (UnauthorizedAccessException error) { Debug.LogWarning("GC metric metadata: " + error.Message); }
            }
            if (!enabled)
            {
                if (_sampling) GraphHistory.Reset();
                _sampling = false;
                TargetedPerformance.TakeSnapshot(); // No stale interval when graph/log is enabled later.
                return;
            }
            if (!_sampling)
            {
                _lastTime = double.NaN;
                _lastTimingTimestamp = 0;
                _hasVisual = false;
                _hasPreviousSample = false;
                SampleCount = Head = 0;
                GraphHistory.Reset();
                _sampling = true;
            }
            Sample(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            if (_capture?.Error != null && !_reportedCaptureError)
            {
                _reportedCaptureError = true;
                Debug.LogWarning($"[StutterProbe] file capture failed: {_capture.Error}. Toggle Timing Diagnostics off/on to retry.");
            }
        }

        // Clock is passed in so interval handling can be verified without sleeps or a display.
        public void Sample(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return;
            var targeted = TargetedPerformance.TakeSnapshot();
            bool targetedChanged = targeted.Generation != _targetedGeneration;
            _targetedGeneration = targeted.Generation;
            if (double.IsNaN(_lastTime) || now <= _lastTime)
            {
                _lastTime = now;
                _lastSampleFrame = Time.frameCount;
                return;
            }
            var scene = SceneManager.GetActiveScene();
            if (_sceneHandle != scene.handle || (_gameManager == null && Time.frameCount % 60 == 0))
            {
                _sceneHandle = scene.handle;
                _gameManager = FindFirstObjectByType<GameManager>();
                _hasVisual = false;
            }
            _graphicsApi ??= SystemInfo.graphicsDeviceType.ToString();
            var settings = SettingsManager.Settings;
            var s = new FrameDiagnosticSample
            {
                Frame = Time.frameCount, Time = now, WallMs = (now - _lastTime) * 1000,
                IntervalStartFrame = _lastSampleFrame, Targeted = targeted, TargetedChanged = targetedChanged,
                GcDiagnostics = _gcDiagnostics?.Read() ?? default,
                RenderDiagnostics = _renderDiagnostics?.Read() ?? default,
                CpuPhases = _cpuPhases?.Read() ?? default,
                UnityMs = Time.unscaledDeltaTime * 1000, Scene = scene.name, Focused = FramePacing.HasFocus,
                Quality = QualitySettings.GetQualityLevel(), VSync = QualitySettings.vSyncCount,
                TargetFps = Application.targetFrameRate, RequestedVSync = settings?.VSync.Value ?? false,
                RequestedFps = settings?.FpsCap.Value ?? 0, BackgroundFps = settings?.BackgroundFpsCap.Value ?? 0,
                Width = Screen.width, Height = Screen.height, RefreshHz = Screen.currentResolution.refreshRateRatio.value,
                GraphicsApi = _graphicsApi, GC0 = GC.CollectionCount(0), GC1 = GC.CollectionCount(1), GC2 = GC.CollectionCount(2),
                TimingEnabled = FrameTimingManager.IsFeatureEnabled(), HasGameplay = _gameManager != null
            };
            _lastSampleFrame = Time.frameCount;
            s.BudgetMs = FrameTimingStatistics.BudgetMs(s.VSync, s.TargetFps, s.RefreshHz);
            _lastTime = now;
            if (s.TimingEnabled)
            {
                FrameTimingManager.CaptureFrameTimings();
                s.HasTiming = FrameTimingManager.GetLatestTimings(1, _timings) > 0;
                if (s.HasTiming)
                {
                    var t = _timings[0];
                    if (_cpuTimerFrequency == 0) _cpuTimerFrequency = FrameTimingManager.GetCpuTimerFrequency();
                    s.CpuTimerFrequency = _cpuTimerFrequency;
                    s.FirstSubmitTimestamp = t.firstSubmitTimestamp;
                    s.PresentCalledTimestamp = t.cpuTimePresentCalled;
                    s.FrameCompleteTimestamp = t.cpuTimeFrameComplete;
                    s.TimingTimestamp = t.frameStartTimestamp;
                    s.FreshTiming = s.TimingTimestamp != _lastTimingTimestamp;
                    _lastTimingTimestamp = s.TimingTimestamp;
                    s.CpuMs = t.cpuFrameTime; s.MainMs = t.cpuMainThreadFrameTime;
                    s.RenderMs = t.cpuRenderThreadFrameTime; s.PresentWaitMs = t.cpuMainThreadPresentWaitTime;
                    s.GpuMs = t.gpuFrameTime; s.HasGpu = t.gpuFrameTime > 0;
                }
            }
            if (s.HasGameplay)
            {
                s.VisualTime = _gameManager.VisualTime;
                s.VisualDelta = _hasVisual ? s.VisualTime - _lastVisual : 0;
                s.SongSpeed = _gameManager.SongSpeed;
                _lastVisual = s.VisualTime;
                _hasVisual = true;
            }
            else _hasVisual = false;
            s.PacingChanged = _hasPreviousSample && (s.Focused != Latest.Focused || s.VSync != Latest.VSync ||
                s.TargetFps != Latest.TargetFps || s.RefreshHz != Latest.RefreshHz || s.Quality != Latest.Quality ||
                s.Width != Latest.Width || s.Height != Latest.Height);
            if (s.PacingChanged)
            {
                // The crossing interval spans two modes. Keep it in the CSV, flagged,
                // but don't grade it or older samples against the new frame budget.
                SampleCount = Head = 0;
            }
            else
            {
                History[Head] = s.WallMs;
                Head = (Head + 1) % HISTORY_SIZE;
                SampleCount = Math.Min(SampleCount + 1, HISTORY_SIZE);
            }
            GraphHistory.Add(s);
            Latest = s;
            _hasPreviousSample = true;
            _capture?.Add(s);
        }

        private void UpdateRenderDiagnostics(bool enabled, bool detailed)
        {
            if (!enabled)
            {
                _renderDiagnostics?.Dispose();
                _renderDiagnostics = null;
                _renderMetricsCapturePath = null;
                return;
            }
            if (_renderDiagnostics == null && !_renderDiagnosticsFailed)
            {
                try
                {
                    _renderDiagnostics = new RenderFrameDiagnostics(detailed);
                    Debug.Log("[RenderFrameDiagnostics] " + _renderDiagnostics.Capabilities);
                }
                catch (Exception error)
                {
                    _renderDiagnosticsFailed = true;
                    Debug.LogWarning("[RenderFrameDiagnostics] unavailable for this session: " + error.Message);
                }
            }
            if (_renderDiagnostics != null && _renderDiagnostics.Detailed != detailed)
            {
                try
                {
                    _renderDiagnostics.SetDetailed(detailed);
                    _renderMetricsCapturePath = null;
                }
                catch (Exception error)
                {
                    _renderDiagnostics.Dispose();
                    _renderDiagnostics = null;
                    _renderDiagnosticsFailed = true;
                    Debug.LogWarning("[RenderFrameDiagnostics] unavailable for this session: " + error.Message);
                }
            }
            if (_renderDiagnostics == null || _capture == null || _renderMetricsCapturePath == _capture.Path) return;
            _renderMetricsCapturePath = _capture.Path;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_capture.Path));
                File.WriteAllText(_capture.Path + ".render-metrics.json", _renderDiagnostics.Capabilities);
            }
            catch (IOException error) { Debug.LogWarning("Render metric metadata: " + error.Message); }
            catch (UnauthorizedAccessException error) { Debug.LogWarning("Render metric metadata: " + error.Message); }
        }

        private void UpdateCpuPhases(bool enabled)
        {
            if (!enabled)
            {
                _cpuPhases?.Dispose();
                _cpuPhases = null;
                _cpuPhaseCapturePath = null;
                return;
            }
            if (_cpuPhases == null && !_cpuPhasesFailed)
            {
                try
                {
                    _cpuPhases = new CpuPhaseDiagnostics();
                    Debug.Log("[CpuPhaseDiagnostics] " + _cpuPhases.Capabilities);
                }
                catch (Exception error)
                {
                    _cpuPhasesFailed = true;
                    Debug.LogWarning("[CpuPhaseDiagnostics] unavailable for this session: " + error.Message);
                }
            }
            if (_cpuPhases == null || _capture == null || _cpuPhaseCapturePath == _capture.Path) return;
            _cpuPhaseCapturePath = _capture.Path;
            _cpuPhases.Reset();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_capture.Path));
                File.WriteAllText(_capture.Path + ".cpu-phases.json", _cpuPhases.Capabilities);
            }
            catch (IOException error) { Debug.LogWarning("CPU phase metadata: " + error.Message); }
            catch (UnauthorizedAccessException error) { Debug.LogWarning("CPU phase metadata: " + error.Message); }
        }

        private void StopCapture()
        {
            if (_capture == null) return;
            _capture.Complete(); // Writer drains in the background, including the final flush.
            _finishing.Add(_capture);
            Debug.Log($"[StutterProbe] finishing capture {_capture.Path}; dropped rows: {_capture.DroppedRows}");
            _capture = null;
        }

        public async Task FinishCaptureAsync()
        {
            StopCapture();
            await Task.WhenAll(_finishing.Select(capture => capture.Completion));
            CheckFinishedCaptures();
        }

        private void OnApplicationQuit()
        {
            StopCapture();
            var timeout = Stopwatch.StartNew();
            foreach (var capture in _finishing)
            {
                var remaining = TimeSpan.FromSeconds(2) - timeout.Elapsed;
                if (remaining <= TimeSpan.Zero || !capture.Completion.Wait(remaining))
                {
                    Debug.LogWarning("[StutterProbe] capture flush timed out during shutdown; CSV may be incomplete.");
                    break;
                }
            }
            CheckFinishedCaptures();
        }

        private void CheckFinishedCaptures()
        {
            for (int i = _finishing.Count - 1; i >= 0; i--)
            {
                var capture = _finishing[i];
                if (!capture.Completion.IsCompleted) continue;
                if (capture.Error != null)
                {
                    _lastCaptureError = capture.Error;
                    Debug.LogWarning($"[StutterProbe] capture failed: {capture.Error}");
                }
                else Debug.Log($"[StutterProbe] capture complete: {capture.Path}; dropped rows: {capture.DroppedRows}");
                _finishing.RemoveAt(i);
            }
        }

        private void OnDestroy()
        {
            _gcDiagnostics?.Dispose();
            _cpuPhases?.Dispose();
            _renderDiagnostics?.Dispose();
            StopCapture();
            if (Instance == this)
            {
                TargetedPerformance.SetEnabled(false);
                Instance = null;
            }
        }
    }
}
