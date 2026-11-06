// pattern: Imperative Shell
using UnityEngine;
using YARG.Settings;

namespace YARG.Persistent
{
    /// <summary>Update cadence, not display scanout. One bar per sampled update, including menus.</summary>
    public sealed class FrameGraph : MonoBehaviour
    {
        private const float PANEL_WIDTH = 1080;
        private const float PANEL_HEIGHT = 190;
        private const float TEXT_WIDTH = 585;
        private const float GRAPH_HEIGHT = 130;
        private GUIStyle _style;
        private string _readout = "Waiting for timing samples...";
        private double _nextTextUpdate;
        private bool _wasEnabled;
        private double _graphTopMs = 50;
        private Texture2D _graphTexture;
        private Color32[] _graphPixels;
        private int _rasterFrame = -1;
        private string _graphTitle = string.Empty;

        private static bool GraphEnabled => SettingsManager.Settings != null &&
            SettingsManager.SettingContainer.IsInitialized && SettingsManager.Settings.FrameGraph.Value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("FrameGraph");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameGraph>();
        }

        private void Update()
        {
            if (!GraphEnabled) { _wasEnabled = false; return; }
            var probe = StutterProbe.Instance;
            if (probe == null || probe.SampleCount == 0) return;
            var s = probe.Latest;
            if (_wasEnabled && s.Time < _nextTextUpdate) return;
            _wasEnabled = true;
            _nextTextUpdate = s.Time + 0.25;
            var stats = FrameTimingStatistics.Calculate(probe.History, probe.SampleCount, s.BudgetMs);
            _graphTopMs = System.Math.Max(50, s.BudgetMs * 2);
            _graphTitle = $"Wall-clock interval / update • top {_graphTopMs:0.#} ms";
            string cap = s.TargetFps > 0 ? s.TargetFps.ToString() : "unlimited";
            string mode = s.VSync > 0 ? $"VSync /{s.VSync}" : $"FPS cap {cap}";
            string reference = s.VSync == 0 && s.TargetFps <= 0 ? "display reference" : "update budget";
            string timing = !s.TimingEnabled ? "CPU/GPU timing unavailable (requires profiling build)" :
                !s.HasTiming ? "CPU/GPU timing pending or unavailable" :
                $"CPU {s.CpuMs:0.00}  main {s.MainMs:0.00}  render {s.RenderMs:0.00} ms\n" +
                $"Present wait {s.PresentWaitMs:0.00} ms  GPU {(s.HasGpu ? s.GpuMs.ToString("0.00") + " ms" : "unavailable")} ({(s.FreshTiming ? "delayed" : "stale/delayed")})";
            string capture = probe.CaptureError != null ? "CSV ERROR (see player log)" :
                probe.CapturePath != null ? $"CSV recording; dropped rows {probe.DroppedRows}" :
                probe.FinishingCaptures > 0 ? "CSV finishing" : "CSV off";
            _readout = $"<b>UPDATE CADENCE — not display presentation</b>   {s.Scene}\n" +
                $"Wall {s.WallMs:0.00} ms   Unity dt {s.UnityMs:0.00} ms   avg {(stats.AverageMs > 0 ? 1000 / stats.AverageMs : 0):0.0} FPS\n" +
                $"Current mode, last {probe.SampleCount}: p95 {stats.P95Ms:0.00}  worst {stats.WorstMs:0.00} ms\n" +
                $"> budget +5%: {stats.OverBudget}   {reference}: {s.BudgetMs:0.00} ms\n" +
                $"Effective: {mode}   focus {(s.Focused ? "yes" : "NO")}   quality {(s.Quality == 0 ? "Low" : "High")}\n" +
                $"Selected: VSync {(s.RequestedVSync ? "on" : "off")} / cap {s.RequestedFps} / background {s.BackgroundFps}\n" +
                $"{s.Width}x{s.Height} @ {s.RefreshHz:0.##} Hz   {s.GraphicsApi}\n" +
                timing + $"\nGC counts {s.GC0}/{s.GC1}/{s.GC2}   {capture}";
        }

        private void OnGUI()
        {
            if (!GraphEnabled || Event.current.type != EventType.Repaint) return;
            var probe = StutterProbe.Instance;
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, richText = true, wordWrap = false,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0)
            };
            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            float scale = Mathf.Min(1, Screen.width / PANEL_WIDTH);
            GUI.matrix = Matrix4x4.TRS(new Vector3(Screen.width - PANEL_WIDTH * scale,
                Screen.height - PANEL_HEIGHT * scale, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            Draw(new Rect(0, 0, PANEL_WIDTH, PANEL_HEIGHT), new Color(0.03f, 0.04f, 0.06f, 0.94f));
            GUI.color = Color.white;
            GUI.Label(new Rect(8, 6, TEXT_WIDTH - 12, PANEL_HEIGHT - 8), _readout, _style);
            if (probe != null && probe.SampleCount > 0)
            {
                double budget = probe.Latest.BudgetMs;
                UpdateGraphTexture(probe);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(TEXT_WIDTH, 25, StutterProbe.HISTORY_SIZE, GRAPH_HEIGHT), _graphTexture);
                if (budget > 0)
                    Draw(new Rect(TEXT_WIDTH, 25 + GRAPH_HEIGHT - (float)(budget / _graphTopMs * GRAPH_HEIGHT),
                        StutterProbe.HISTORY_SIZE, 1), Color.white);
                GUI.color = Color.white;
                GUI.Label(new Rect(TEXT_WIDTH, 5, 480, 20), _graphTitle, _style);
                GUI.Label(new Rect(TEXT_WIDTH, 160, 480, 24), "Older ←   480 updates   → Newest    White: budget/reference", _style);
            }
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void UpdateGraphTexture(StutterProbe probe)
        {
            if (_graphTexture == null)
            {
                _graphTexture = new Texture2D(StutterProbe.HISTORY_SIZE, (int) GRAPH_HEIGHT,
                    TextureFormat.RGBA32, false)
                {
                    name = "Frame timing graph", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                };
                _graphPixels = new Color32[StutterProbe.HISTORY_SIZE * (int) GRAPH_HEIGHT];
            }
            if (_rasterFrame == probe.Latest.Frame) return;
            _rasterFrame = probe.Latest.Frame;
            FrameGraphRaster.Fill(_graphPixels, (int) GRAPH_HEIGHT, probe.History, probe.SampleCount,
                probe.Head, _graphTopMs, probe.Latest.BudgetMs);
            _graphTexture.SetPixels32(_graphPixels);
            _graphTexture.Apply(false, false);
        }

        private void OnDestroy()
        {
            if (_graphTexture != null) Destroy(_graphTexture);
        }

        private static void Draw(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }
    }
}
