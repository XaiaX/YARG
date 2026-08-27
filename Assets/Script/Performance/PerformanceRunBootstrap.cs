using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using YARG.Core.Logging;
using YARG.Core.Replays;
using YARG.Core.Song;
using YARG.Gameplay;
using YARG.Helpers;
using YARG.Menu.Persistent;
using YARG.Menu.ScoreScreen;
using YARG.Replays;
using YARG.Song;

namespace YARG
{
    /// <summary>
    /// Dev/test harness that turns a rendered player run into a deterministic, replay-driven
    /// performance run with auto-quit and process exit codes.
    /// </summary>
    /// <remarks>
    /// Enabled only when <c>-perf-replay</c> or <c>-perf-run</c> is present on the command
    /// line; without those arguments this class has zero footprint (no GameObject is created,
    /// nothing is subscribed, no state is touched).
    /// <para>
    /// Lifecycle: <see cref="Initialize"/> runs after the first scene loads and creates a
    /// hidden <see cref="DontDestroyOnLoad"/> GameObject holding this behaviour. That object
    /// survives every scene transition (menu -&gt; gameplay -&gt; score), supervises the run
    /// from a trivial <see cref="Update"/> poll, and never modifies gameplay behaviour beyond
    /// setting the exact launch state the menu sets when watching a replay.
    /// </para>
    /// <para>
    /// Natural end is detected two ways: the scene leaving Gameplay (the normal play flow), or
    /// — for watch-replay runs, which end paused at chart end with no score screen
    /// (GameManager.EndSong's replay-viewer branch) — a held terminal pause past the song-length
    /// end gate. Intended to be used with <c>-perf-quit</c>; without it the run finishes but the
    /// session stays degraded (the persistent menu music player remains removed).
    /// </para>
    /// <para>
    /// Exit codes: 0 natural song end, 2 replay/song resolution failure, 3 no-song-start
    /// timeout or duration watchdog expiry, 4 collector emitted no CSV.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(10001)]
    public sealed class PerformanceRunBootstrap : MonoBehaviour
    {
        private enum Phase
        {
            AwaitReadiness,
            AwaitSongStart,
            Running,
            Ending
        }

        /// <summary>Bounded wait for ReplayContainer init + initial song library scan.</summary>
        private const float READINESS_TIMEOUT_SECONDS = 600f;

        /// <summary>Bounded wait for GameManager.SongStarted after launching gameplay.</summary>
        private const float SONG_START_TIMEOUT_SECONDS = 120f;

        /// <summary>Frames to wait after the run ends so the collector can flush.</summary>
        private const float QUIT_DELAY_SECONDS = 0.5f;

        /// <summary>How often to look for the GameManager instance while awaiting song start.</summary>
        private const int GAME_MANAGER_POLL_FRAMES = 30;

        /// <summary>How often to look for the persistent menu MusicPlayer while awaiting readiness.</summary>
        private const int MUSIC_PLAYER_POLL_FRAMES = 30;

        /// <summary>How long the terminal paused state must hold before declaring the natural end.</summary>
        private const float WATCH_END_CONFIRM_SECONDS = 1f;

        private Phase _phase = Phase.AwaitReadiness;
        private float _phaseStartRealtime;
        private DateTime _startedAtUtc = DateTime.UtcNow;
        private float _launchRealtime;
        private float _runDeadlineRealtime = -1f;
        private float _quitAtRealtime = -1f;
        private int _frameCounter;
        private bool _subscribedGameManager;
        private bool _menuMusicPlayerDestroyed;
        private float _watchEndConfirmStartRealtime = -1f;
        private bool _enteredGameplay;
        private bool _songStarted;
        private int _exitCode;
        private string _exitReason;

        private ReplayInfo _replayInfo;
        private SongEntry _songEntry;
        private GameManager _gameManager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            CommandLineArgs.Initialize();

            // Zero footprint on normal runs: early-out before creating anything
            if (string.IsNullOrEmpty(CommandLineArgs.PerformanceReplay) &&
                string.IsNullOrEmpty(CommandLineArgs.PerformanceRunLabel))
            {
                return;
            }

            // Hidden, scene-transition-proof driver object
            var gameObject = new GameObject(nameof(PerformanceRunBootstrap));
            gameObject.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<PerformanceRunBootstrap>();
        }

        private void Awake()
        {
            _phaseStartRealtime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            switch (_phase)
            {
                case Phase.AwaitReadiness:
                    UpdateAwaitReadiness();
                    break;
                case Phase.AwaitSongStart:
                    UpdateAwaitSongStart();
                    break;
                case Phase.Running:
                    UpdateRunning();
                    break;
                case Phase.Ending:
                    UpdateEnding();
                    break;
            }
        }

        private void UpdateAwaitReadiness()
        {
            if (Time.realtimeSinceStartup - _phaseStartRealtime >= READINESS_TIMEOUT_SECONDS)
            {
                Finish(3, "readiness timeout (ReplayContainer or initial song library scan never completed)");
                return;
            }

            // Remove the persistent menu MusicPlayer before its startup trigger (loading screen
            // end) can fire; see TryDestroyMenuMusicPlayer for why this must precede readiness.
            if (!_menuMusicPlayerDestroyed && ++_frameCounter >= MUSIC_PLAYER_POLL_FRAMES)
            {
                _frameCounter = 0;
                TryDestroyMenuMusicPlayer();
            }

            // ReplayContainer is initialized by GlobalVariables.SingletonAwake (persistent scene)
            if (string.IsNullOrEmpty(ReplayContainer.ReplayDirectory))
            {
                return;
            }

            // Initial song library scan: LoadingScreen.Start awaits SongContainer.RunRefresh, and
            // the loading context deactivates the loading screen object only after the scan (and
            // the rest of startup loading) finishes. "Instance exists and is inactive" therefore
            // means the initial library scan has completed and SongsByHash is populated.
            if (LoadingScreen.Instance == null || LoadingScreen.IsActive)
            {
                return;
            }

            Launch();
        }

        private void Launch()
        {
            if (!TryResolveReplay(out _replayInfo, out string error))
            {
                Finish(2, error);
                return;
            }

            // Resolve the song exactly like the gameplay replay branch does
            // (GameManager.Loading: SongsByHash by the replay's song checksum)
            if (!SongContainer.SongsByHash.TryGetValue(_replayInfo.SongChecksum, out var songs) ||
                songs == null || songs.Count == 0)
            {
                Finish(2, $"replay song checksum {_replayInfo.SongChecksum} is not present in the song library");
                return;
            }

            _songEntry = songs[0];

            RecordLaunchMetadata();

            if (CommandLineArgs.PerformanceDurationSeconds > 0f)
            {
                _runDeadlineRealtime = Time.realtimeSinceStartup + CommandLineArgs.PerformanceDurationSeconds;
            }

            // Same launch state as the menu's watch-replay branch (ViewType.LoadIntoReplay).
            // PlayingWithReplay is deliberately NOT set: pure replay playback, no ghost players.
            GlobalVariables.State = PersistentState.Default;
            GlobalVariables.State.CurrentSong = _songEntry;
            GlobalVariables.State.CurrentReplay = _replayInfo;
            GlobalVariables.State.SongSpeed = _replayInfo.SongSpeed;

            // Seed must land before venue/player creation consumes RNG, i.e. right before the load
            UnityEngine.Random.InitState(CommandLineArgs.PerformanceSeed);

            // NOTE: pre-formatted for YargLogger.LogInfo — the LogFormat* overloads have
            // optional CallerFilePath/line/member parameters that make multi-arg calls
            // with trailing string/int arguments ambiguous (CS0121).
            YargLogger.LogInfo(
                $"[PerfRun] Launching gameplay: replay '{_replayInfo.FilePath}', song '{_songEntry.Name} - {_songEntry.Artist}', seed {CommandLineArgs.PerformanceSeed.ToString(CultureInfo.InvariantCulture)}, run '{CommandLineArgs.PerformanceRunLabel}'");
            // Final music-player attempt in case the readiness poll never caught it
            TryDestroyMenuMusicPlayer();

            GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);

            _launchRealtime = Time.realtimeSinceStartup;
            _phase = Phase.AwaitSongStart;
        }

        /// <summary>
        /// Removes the persistent-scene menu MusicPlayer. Perf runs never navigate the menu, so the
        /// game's normal stop path (HelpBar.SetInfoFromScheme deactivating the player when the scene
        /// leaves the menu) never runs, and menu music would play under gameplay. The object is
        /// destroyed rather than deactivated: MusicPlayer.NextSong's rejection branch only disposes
        /// the loaded mixer and continues its retry loop on an inactive object, which would issue up
        /// to 20 background audio loads during the measured window; against a destroyed object that
        /// loop aborts at its first check instead. If the component had already enabled, its orphaned
        /// startup continuation may run one background audio load and log one MissingReferenceException
        /// afterwards (none observed in practice — the destroy lands before the loading screen ends) —
        /// harmless either way, and nothing is ever played.
        /// </summary>
        private void TryDestroyMenuMusicPlayer()
        {
            if (_menuMusicPlayerDestroyed)
            {
                return;
            }

            var musicPlayer = FindAnyObjectByType<MusicPlayer>();
            if (musicPlayer == null)
            {
                return;
            }

            _menuMusicPlayerDestroyed = true;
            Destroy(musicPlayer.gameObject);
            YargLogger.LogInfo("[PerfRun] Removed the persistent menu MusicPlayer to keep menu audio out of the run");
        }

        private static bool TryResolveReplay(out ReplayInfo replay, out string error)
        {
            string argument = CommandLineArgs.PerformanceReplay;
            string path = null;

            if (!string.IsNullOrEmpty(argument) && File.Exists(argument))
            {
                path = argument;
            }
            else if (!string.IsNullOrEmpty(argument))
            {
                // Not an existing file path; treat the argument as a replay id (the replay's
                // checksum, as keyed by ReplayContainer) and resolve it through the container
                var id = HashWrapper.FromString(argument);
                foreach (var candidate in ReplayContainer.Replays)
                {
                    if (candidate.ReplayChecksum.Equals(id))
                    {
                        path = candidate.FilePath;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                replay = null;
                error = $"-perf-replay '{argument}' is neither an existing file path nor a known replay id";
                return false;
            }

            // Only a fully valid replay can be played back; metadata-only replays would be
            // rejected by the gameplay loader anyway, so fail fast with a clear reason
            var (result, info) = ReplayIO.TryReadMetadata(path);
            if (result != ReplayReadResult.Valid)
            {
                replay = null;
                error = $"failed to read replay metadata for '{path}': {result}";
                return false;
            }

            replay = info;
            error = null;
            return true;
        }

        private void UpdateAwaitSongStart()
        {
            if (_songStarted)
            {
                _phase = Phase.Running;
                return;
            }

            if (Time.realtimeSinceStartup - _launchRealtime >= SONG_START_TIMEOUT_SECONDS)
            {
                Finish(3, $"song did not start within {SONG_START_TIMEOUT_SECONDS:0}s of the gameplay launch");
                return;
            }

            if (_runDeadlineRealtime > 0f && Time.realtimeSinceStartup >= _runDeadlineRealtime)
            {
                Finish(3, "duration watchdog expired before the song started (CSV retained)");
                return;
            }

            var global = GlobalVariables.Instance;
            if (global != null)
            {
                if (!_enteredGameplay && global.CurrentScene == SceneIndex.Gameplay)
                {
                    _enteredGameplay = true;
                }
                else if (_enteredGameplay && global.CurrentScene != SceneIndex.Gameplay)
                {
                    // The gameplay loader returns to the menu when a replay fails to load;
                    // treat any early scene exit as a failed run instead of waiting out the timeout
                    Finish(3, "left the gameplay scene before the song started (replay load failure?)");
                    return;
                }
            }

            // Find the GameManager (a scene object, not a singleton) and subscribe; the
            // SongStarted event invokes immediately for late subscribers, so there is no race
            if (!_subscribedGameManager && ++_frameCounter >= GAME_MANAGER_POLL_FRAMES)
            {
                _frameCounter = 0;
                var gameManager = FindAnyObjectByType<GameManager>();
                if (gameManager != null)
                {
                    _gameManager = gameManager;
                    gameManager.SongStarted += OnSongStarted;
                    _subscribedGameManager = true;
                }
            }
        }

        private void OnSongStarted()
        {
            _songStarted = true;
        }

        private void UpdateRunning()
        {
            var global = GlobalVariables.Instance;

            // The gameplay scene ends the song by loading the score scene
            // (GameManager triggers LoadScene(SceneIndex.Score) at song end), so the scene
            // index leaving Gameplay is the natural-end signal
            if (global == null || global.CurrentScene != SceneIndex.Gameplay)
            {
                Finish(0, "natural song end");
                return;
            }

            // Watch-replay runs never leave the gameplay scene: GameManager.EndSong's replay-viewer
            // branch pauses the runner and returns before the score-screen load (GameManager.cs:647-651),
            // so the scene-exit signal above cannot fire. Detect that terminal pause instead. The
            // threshold mirrors EndSong's own gate exactly (SongTime >= SongLength + SONG_START_DELAY,
            // which SONG_END_DELAY aliases): that pause provably cannot occur below it, while anything
            // looser would also match e.g. a focus-loss pause in the final seconds. Started guards
            // SongTime, whose getter assumes a live song runner.
            if (_gameManager != null && _gameManager.Started && _gameManager.Paused &&
                _gameManager.SongTime >= _gameManager.SongLength + GameManager.SONG_START_DELAY)
            {
                if (_watchEndConfirmStartRealtime < 0f)
                {
                    _watchEndConfirmStartRealtime = Time.realtimeSinceStartup;
                }
                else if (Time.realtimeSinceStartup - _watchEndConfirmStartRealtime >= WATCH_END_CONFIRM_SECONDS)
                {
                    Finish(0, "natural song end (watch replay paused at chart end)");
                    return;
                }
            }
            else
            {
                _watchEndConfirmStartRealtime = -1f;
            }

            if (_runDeadlineRealtime > 0f && Time.realtimeSinceStartup >= _runDeadlineRealtime)
            {
                Finish(3, "duration watchdog expired before natural song end (CSV retained)");
            }
        }

        private void UpdateEnding()
        {
            if (_quitAtRealtime > 0f && Time.realtimeSinceStartup >= _quitAtRealtime)
            {
                _quitAtRealtime = -1f;
                QuitApplication(_exitCode);
            }
        }

        private void Finish(int exitCode, string reason)
        {
            _phase = Phase.Ending;
            _exitCode = exitCode;
            _exitReason = reason;

            // On natural song end the game itself has already called this (GameManager, right
            // before loading the score scene). Repeat it here so the watchdog/timeout paths
            // flush whatever rows exist as well; it is safe to call twice.
            PerformanceDiagnostics.FlushAtSongEnd();

            exitCode = CheckCollectorOutput(exitCode, _startedAtUtc);
            _exitCode = exitCode;

            RecordEndMetadata();

            if (CommandLineArgs.PerformanceQuit)
            {
                YargLogger.LogInfo($"[PerfRun] Run finished: exit code {exitCode} ({reason})");
                _quitAtRealtime = Time.realtimeSinceStartup + QUIT_DELAY_SECONDS;
            }
            else
            {
                YargLogger.LogInfo(
                    $"[PerfRun] Run finished: exit code {exitCode} ({reason}); -perf-quit not set, staying alive");
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Best-effort check that the collector actually emitted a CSV, only when the run
        /// would otherwise report success and an output directory was configured. Only
        /// files written by THIS process count: a stale CSV from an earlier session in a
        /// reused output directory must not mask a collector that died at startup.
        /// </summary>
        private static int CheckCollectorOutput(int exitCode, DateTime processStartUtc)
        {
            if (exitCode != 0 || string.IsNullOrEmpty(CommandLineArgs.PerformanceCsvDirectory))
            {
                return exitCode;
            }

            try
            {
                // The collector opens its CSV (and writes the header) during its Awake,
                // seconds at most before this bootstrap initializes; 30 s of slack covers
                // slow starts while rejecting any pre-existing file.
                DateTime freshnessFloorUtc = processStartUtc.AddSeconds(-30f);
                bool emittedThisRun = false;
                foreach (string file in Directory.GetFiles(CommandLineArgs.PerformanceCsvDirectory, "*_frames.csv"))
                {
                    if (File.GetLastWriteTimeUtc(file) >= freshnessFloorUtc)
                    {
                        emittedThisRun = true;
                        break;
                    }
                }

                if (!emittedThisRun)
                {
                    YargLogger.LogError("[PerfRun] no performance CSV was emitted by this run into the output directory");
                    return 4;
                }
            }
            catch (Exception exception)
            {
                YargLogger.LogFormatWarning("[PerfRun] Unable to check for CSV output: {0}", exception.Message);
            }

            return exitCode;
        }

        private void RecordLaunchMetadata()
        {
            TrySetMetadata("seed", CommandLineArgs.PerformanceSeed.ToString(CultureInfo.InvariantCulture));
            TrySetMetadata("runLabel", CommandLineArgs.PerformanceRunLabel);
            if (_replayInfo != null)
            {
                TrySetMetadata("replayPath", _replayInfo.FilePath);
                TrySetMetadata("replayChecksum", _replayInfo.ReplayChecksum.ToString());
            }

            if (_songEntry != null)
            {
                TrySetMetadata("songChecksum", _songEntry.Hash.ToString());
                TrySetMetadata("songName", _songEntry.Name);
            }

            TrySetMetadata("effectivePersistentDataPath", PathHelper.PersistentDataPath);
        }

        private void RecordEndMetadata()
        {
            TrySetMetadata("exitCode", _exitCode.ToString(CultureInfo.InvariantCulture));
            TrySetMetadata("exitReason", _exitReason);

            // Final per-player scores: the gameplay scene publishes these through global state
            // right before it leaves for the score screen, so they are already populated by the
            // time the scene change is observed. On watchdog expiry they may be absent, and the
            // entries are simply skipped.
            var stats = GlobalVariables.State.ScoreScreenStats;
            if (!stats.HasValue)
            {
                // Watch-replay runs pause at chart end instead of loading the score screen, so
                // ScoreScreenStats is never populated. Harvest the same per-player data from the
                // live GameManager instead (the exact sources EndSong's score path uses).
                RecordPlayerMetadataFromGameManager();
                return;
            }

            var cards = stats.Value.PlayerScores;
            if (cards == null || cards.Length == 0)
            {
                return;
            }

            TrySetMetadata("playerCount", cards.Length.ToString(CultureInfo.InvariantCulture));
            TrySetMetadata("bandScore", stats.Value.BandScore.ToString(CultureInfo.InvariantCulture));
            TrySetMetadata("bandStars", stats.Value.BandStars.ToString(CultureInfo.InvariantCulture));

            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                TrySetMetadata($"player{i}Name", card.Player?.Profile?.Name);

                var playerStats = card.Stats;
                if (playerStats == null)
                {
                    continue;
                }

                TrySetMetadata($"player{i}Score",
                    playerStats.TotalScore.ToString(CultureInfo.InvariantCulture));
                TrySetMetadata($"player{i}Judgments",
                    $"hit={playerStats.NotesHit}/{playerStats.TotalNotes};missed={playerStats.NotesMissed};maxCombo={playerStats.MaxCombo};stars={playerStats.Stars.ToString("0.##", CultureInfo.InvariantCulture)}");
            }
        }

        private void RecordPlayerMetadataFromGameManager()
        {
            if (_gameManager == null)
            {
                return;
            }

            var players = _gameManager.Players;
            if (players == null || players.Count == 0)
            {
                return;
            }

            TrySetMetadata("playerCount", players.Count.ToString(CultureInfo.InvariantCulture));
            TrySetMetadata("bandScore", _gameManager.BandScore.ToString(CultureInfo.InvariantCulture));
            TrySetMetadata("bandStars", ((int) _gameManager.BandStars).ToString(CultureInfo.InvariantCulture));

            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                TrySetMetadata($"player{i}Name", player.Player?.Profile?.Name);

                var playerStats = player.BaseStats;
                if (playerStats == null)
                {
                    continue;
                }

                TrySetMetadata($"player{i}Score",
                    playerStats.TotalScore.ToString(CultureInfo.InvariantCulture));
                TrySetMetadata($"player{i}Judgments",
                    $"hit={playerStats.NotesHit}/{playerStats.TotalNotes};missed={playerStats.NotesMissed};maxCombo={playerStats.MaxCombo};stars={playerStats.Stars.ToString("0.##", CultureInfo.InvariantCulture)}");
            }
        }

        /// <summary>
        /// Null-safe metadata write: entries whose values are unavailable are skipped, and a
        /// failing collector call must never take the run down with it.
        /// </summary>
        private static void TrySetMetadata(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            try
            {
                PerformanceDiagnostics.SetRunMetadata(key, value);
            }
            catch (Exception exception)
            {
                YargLogger.LogWarning($"[PerfRun] Failed to record metadata '{key}': {exception.Message}");
            }
        }

        private static void QuitApplication(int exitCode)
        {
#if UNITY_EDITOR
            YargLogger.LogFormatInfo(
                "[PerfRun] Stopping editor play mode (exit code {0} cannot be conveyed in the editor)",
                exitCode.ToString(CultureInfo.InvariantCulture));
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }
    }
}
