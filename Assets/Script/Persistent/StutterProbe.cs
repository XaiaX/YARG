using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using YARG.Gameplay;
using YARG.Settings;

namespace YARG.Persistent
{
    /// <summary>
    /// Per-frame timing diagnostics ("Timing Diagnostics" experimental setting).
    /// Writes one CSV row per gameplay frame to
    /// <c>&lt;persistentDataPath&gt;/stutter-profile/stutter-&lt;timestamp&gt;-&lt;gfxapi&gt;.csv</c>.
    /// Logging only runs while the setting is enabled; it can be toggled at any
    /// time from Settings &gt; Experimental. Rows are only written while a gameplay
    /// session (GameManager) is active. FrameTiming values lag the current frame
    /// by a fixed number of frames (~4); correlate by the frame column, not
    /// wall-clock alignment.
    /// </summary>
    public sealed class StutterProbe : MonoBehaviour
    {
        private const int FLUSH_EVERY = 120;
        private const float HITCH_DT_MULTIPLIER = 1.5f;

        private static readonly FrameTiming[] _timings = new FrameTiming[1];

        private StreamWriter _writer;
        private int _gc0, _gc1, _gc2;
        private float _medianDt = 1f / 60f;
        private int _sinceFlush;
        private int _hitches;
        private float _sessionStart;
        private GameManager _gameManager;
        private double _lastVisualTime;
        private bool _hasLastVisual;

        private static bool DiagnosticsEnabled =>
            SettingsManager.Settings is not null &&
            SettingsManager.SettingContainer.IsInitialized &&
            SettingsManager.Settings.TimingDiagnostics.Value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("StutterProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<StutterProbe>();
        }

        private void Update()
        {
            // Honor the setting at runtime: open on enable, close on disable.
            if (!DiagnosticsEnabled)
            {
                if (_writer is not null)
                {
                    CloseWriter();
                }

                return;
            }

            if (_writer is null)
            {
                OpenWriter();
            }

            // Only capture during gameplay; reacquire the manager periodically.
            if (_gameManager is null)
            {
                if ((Time.frameCount % 30) != 0)
                {
                    return;
                }

                _gameManager = FindObjectOfType<Gameplay.GameManager>();
                if (_gameManager is null)
                {
                    return;
                }

                _hasLastVisual = false;
                UnityEngine.Debug.Log("[StutterProbe] gameplay session detected, capturing");
            }
            else if (_gameManager == null) // destroyed between scenes
            {
                _hasLastVisual = false;
                return;
            }

            FrameTimingManager.CaptureFrameTimings();

            float dt = Time.deltaTime;
            float unscaled = Time.unscaledDeltaTime;

            double visualTime = _gameManager.VisualTime;
            double visualDt = 0.0;
            if (_hasLastVisual)
            {
                visualDt = visualTime - _lastVisualTime;
            }
            _lastVisualTime = visualTime;
            _hasLastVisual = true;

            // Cheap rolling median-ish estimate of the frame budget.
            _medianDt = _medianDt * 0.95f + Mathf.Max(dt, 0.0001f) * 0.05f;
            bool hitch = dt > _medianDt * HITCH_DT_MULTIPLIER && dt > 1f / 240f;
            if (hitch)
            {
                _hitches++;
            }

            int g0 = GC.CollectionCount(0);
            int g1 = GC.CollectionCount(1);
            int g2 = GC.CollectionCount(2);
            bool gcDelta = g0 != _gc0 || g1 != _gc1 || g2 != _gc2;

            float gpu = 0f, cpuFrame = 0f, cpuMain = 0f, cpuPresentWait = 0f, cpuRender = 0f;
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                var t = _timings[0];
                gpu = (float) t.gpuFrameTime;
                cpuFrame = (float) t.cpuFrameTime;
                cpuMain = (float) t.cpuMainThreadFrameTime;
                cpuPresentWait = (float) t.cpuMainThreadPresentWaitTime;
                cpuRender = (float) t.cpuRenderThreadFrameTime;
            }

            var ci = CultureInfo.InvariantCulture;
            _writer.WriteLine(
                Time.frameCount + "," +
                (Time.unscaledTime - _sessionStart).ToString("F4", ci) + "," +
                dt.ToString("F4", ci) + "," + unscaled.ToString("F4", ci) + "," +
                QualitySettings.vSyncCount + "," + Application.targetFrameRate + "," +
                gpu.ToString("F6", ci) + "," + cpuFrame.ToString("F6", ci) + "," +
                cpuMain.ToString("F6", ci) + "," + cpuPresentWait.ToString("F6", ci) + "," +
                cpuRender.ToString("F6", ci) + "," +
                g0 + "," + g1 + "," + g2 + "," + gcDelta + "," + hitch + "," +
                visualTime.ToString("F6", ci) + "," + visualDt.ToString("F6", ci) + "," +
                _gameManager.SongSpeed.ToString("F3", ci));

            _gc0 = g0;
            _gc1 = g1;
            _gc2 = g2;

            if (++_sinceFlush >= FLUSH_EVERY)
            {
                _sinceFlush = 0;
                _writer.Flush();
            }
        }

        private void OpenWriter()
        {
            var dir = Path.Combine(Application.persistentDataPath, "stutter-profile");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir,
                $"stutter-{DateTime.Now:yyyyMMdd-HHmmss}-{SystemInfo.graphicsDeviceType}.csv");

            _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = false };
            _writer.WriteLine(
                "frame,time,dt,unscaledDt,vsync,targetFps," +
                "gpuFrameTime,cpuFrameTime,cpuMainThreadFrameTime,cpuMainThreadPresentWaitTime,cpuRenderThreadFrameTime," +
                "gc0Count,gc1Count,gc2Count,gcDelta,hitch," +
                "visualTime,visualDt,songSpeed");

            _gc0 = GC.CollectionCount(0);
            _gc1 = GC.CollectionCount(1);
            _gc2 = GC.CollectionCount(2);
            _sessionStart = Time.unscaledTime;
            _sinceFlush = 0;
            _hitches = 0;

            UnityEngine.Debug.Log($"[StutterProbe] writing {path} | GPU={SystemInfo.graphicsDeviceType} | {SystemInfo.graphicsDeviceName} | {SystemInfo.operatingSystem}");
        }

        private void CloseWriter()
        {
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
            _hasLastVisual = false;
            UnityEngine.Debug.Log($"[StutterProbe] logging stopped, {_hitches} hitch frames flagged");
        }

        private void OnApplicationPause(bool paused)
        {
            _writer?.Flush();
        }

        private void OnDestroy()
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }
}
