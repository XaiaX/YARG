using UnityEngine;
using YARG.Settings;

namespace YARG.Persistent
{
    /// <summary>
    /// Debug-only real-time frame-time graph ("Frame Graph" experimental setting).
    /// Draws a time-linear scrolling bar graph of per-frame delta times via IMGUI:
    /// each refresh period is one pixel, so missed frames appear as blank gaps where
    /// a frame should have been. Only visible during song playback (gameplay scene).
    /// Temporary diagnostic tool — not for upstream.
    /// </summary>
    public sealed class FrameGraph : MonoBehaviour
    {
        private const int GRAPH_WIDTH = 480;  // px; one px per refresh period (4 s @ 120 Hz, 8 s @ 60 Hz)
        private const int TEXT_WIDTH = 320;   // px; readout column to the left of the graph
        private const float GRAPH_TOP_MS = 1000f / 30f; // 33.3 ms at the top of the graph (~67 px)

        private const float MS_PER_PIXEL = 0.5f;
        private const float SPIKE_OVERFLOW_LIMIT = 2.0f; // bars may extend this many panel-heights above

        private static int GraphHeightPx => Mathf.RoundToInt(GRAPH_TOP_MS / MS_PER_PIXEL);

        private static readonly FrameTiming[] _timings = new FrameTiming[1];

        // Ring buffer of pixels: null = gap (missed frame slot), value = frame bar (ms)
        private readonly float?[] _slots = new float?[GRAPH_WIDTH];
        private int _head;

        private float _refreshPeriodMs = 1000f / 60f;
        private int _missedWindow;               // missed refresh periods in the current 1 s window
        private float _windowStart;
        private int _missedLastSecond;
        private float _worstDtMs;

        private Gameplay.GameManager _gameManager;

        private Texture2D _green;
        private Texture2D _yellow;
        private Texture2D _red;
        private Texture2D _bg;
        private bool _texturesBuilt;

        private static bool GraphEnabled =>
            SettingsManager.Settings is not null &&
            SettingsManager.SettingContainer.IsInitialized &&
            SettingsManager.Settings.FrameGraph.Value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("FrameGraph");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameGraph>();
        }

        private void Start()
        {
            _refreshPeriodMs = 1000f / Mathf.Max(1f, (float) Screen.currentResolution.refreshRateRatio.value);
            _windowStart = Time.unscaledTime;
        }

        private void Update()
        {
            if (!GraphEnabled)
            {
                return;
            }

            // Only track while a gameplay session is active; reacquire periodically.
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

                // Entering gameplay: reset per-session stats and the graph.
                _worstDtMs = 0f;
                _missedWindow = 0;
                _windowStart = Time.unscaledTime;
                for (int i = 0; i < _slots.Length; i++)
                {
                    _slots[i] = null;
                }
                _head = 0;
            }
            else if (_gameManager == null) // destroyed between scenes
            {
                return;
            }

            float dtMs = Time.unscaledDeltaTime * 1000f;

            if (dtMs > _worstDtMs)
            {
                _worstDtMs = dtMs;
            }

            // A frame that took longer than one refresh period missed that many
            // periods; render the missed slots as blank gaps, then the frame bar.
            int periods = Mathf.Max(1, Mathf.RoundToInt(dtMs / _refreshPeriodMs));
            int missed = periods - 1;
            _missedWindow += missed;

            for (int i = 0; i < missed; i++)
            {
                _slots[_head] = null;
                _head = (_head + 1) % GRAPH_WIDTH;
            }

            _slots[_head] = dtMs;
            _head = (_head + 1) % GRAPH_WIDTH;

            if (Time.unscaledTime - _windowStart >= 1f)
            {
                _missedLastSecond = _missedWindow;
                _missedWindow = 0;
                _windowStart = Time.unscaledTime;
            }
        }

        private void OnGUI()
        {
            if (!GraphEnabled || _gameManager is null)
            {
                return;
            }

            if (!_texturesBuilt)
            {
                _green = MakeTexture(new Color32(0x00, 0xC8, 0x53, 0xE0));
                _yellow = MakeTexture(new Color32(0xFF, 0xB6, 0x36, 0xE0));
                _red = MakeTexture(new Color32(0xF7, 0x00, 0x72, 0xE0));
                _bg = MakeTexture(new Color32(0x11, 0x11, 0x11, 0xB0));
                _texturesBuilt = true;
            }

            // Flush against the bottom-right corner: graph on the right, readout
            // column to its left, sharing the same vertical space.
            float h = GraphHeightPx;
            float graphX = Screen.width - GRAPH_WIDTH;
            float panelX = graphX - TEXT_WIDTH;
            float y = Screen.height - h;

            // Panel first, then bars on top — bars taller than the graph extend
            // upwards out of it (capped) so spikes stay visible instead of clipping.
            GUI.DrawTexture(new Rect(panelX, y, TEXT_WIDTH + GRAPH_WIDTH, h), _bg);

            float maxBarPx = h * SPIKE_OVERFLOW_LIMIT;
            for (int i = 0; i < GRAPH_WIDTH; i++)
            {
                int idx = (_head - 1 - i + GRAPH_WIDTH * 2) % GRAPH_WIDTH;
                float? dtMs = _slots[idx];
                if (dtMs is not > 0f)
                {
                    continue;
                }

                float barPx = Mathf.Clamp(dtMs.Value / MS_PER_PIXEL, 1f, maxBarPx);
                var rect = new Rect(graphX + GRAPH_WIDTH - (i + 1), y + h - barPx, 1f, barPx);
                GUI.DrawTexture(rect, Classify(dtMs.Value));
            }

            // Budget reference lines across the graph only: 120 Hz = yellow, 60 Hz = red.
            // No 30 Hz line — the top of the graph *is* 33.3 ms; anything above it
            // (overflowing the panel) is below 30 fps by definition.
            var lineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
            };
            foreach (var (budgetMs, label, color) in new[]
                     {
                         (1000f / 120f, "120", _yellow),
                         (1000f / 60f,  "60",  _red),
                     })
            {
                float lineY = y + h - budgetMs / MS_PER_PIXEL;
                GUI.DrawTexture(new Rect(graphX, lineY, GRAPH_WIDTH, 1f), color);

                // Label sits left of the graph so the line doesn't bisect it.
                GUI.Label(new Rect(graphX - 34f, lineY - 7f, 30f, 14f), label, lineStyle);
            }

            // Readout column, left of the graph, stacked to fit the graph's height
            float lastDtMs = Time.unscaledDeltaTime * 1000f;
            float fps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = true,
                wordWrap = false,
            };
            style.padding = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);

            string text = $"<b>{fps:0.0} FPS</b>   frame {lastDtMs:0.0} ms\n" +
                $"budget {_refreshPeriodMs:0.0} ms   <b>missed/s {_missedLastSecond}</b>\n" +
                $"worst {_worstDtMs:0.0} ms";

            // Optional GPU time, if Frame Timing stats are enabled in Player Settings
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                text += $"\ngpu {_timings[0].gpuFrameTime * 1000f:0.0} ms   cpu main {_timings[0].cpuMainThreadFrameTime * 1000f:0.0} ms";
            }

            GUI.Label(new Rect(panelX + 8f, y + 3f, TEXT_WIDTH - 16f, h - 6f), text, style);
        }

        private Texture2D Classify(float dtMs)
        {
            // Absolute thresholds matching the reference lines: past the 60 Hz
            // budget (16.7 ms) is red, past the 120 Hz budget (8.3 ms) is yellow.
            if (dtMs > 1000f / 60f)
            {
                return _red;
            }

            if (dtMs > 1000f / 120f)
            {
                return _yellow;
            }

            return _green;
        }

        private static Texture2D MakeTexture(Color32 color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void OnDestroy()
        {
            Destroy(_green);
            Destroy(_yellow);
            Destroy(_red);
            Destroy(_bg);
        }
    }
}
