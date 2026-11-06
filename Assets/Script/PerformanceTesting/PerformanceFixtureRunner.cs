// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using YARG.Core;
using YARG.Core.Game;
using YARG.Gameplay;
using YARG.Persistent;
using YARG.Player;
using YARG.Settings;
using YARG.Song;

namespace YARG.PerformanceTesting
{
    /// <summary>Opt-in, isolated, rendered bot run using the normal song-loading and gameplay paths.</summary>
    public sealed class PerformanceFixtureRunner : MonoBehaviour
    {
        private static bool _libraryReady;
        private static string _libraryError;
        private PerformanceFixtureDefinition _fixture;
        private GameManager _game;
        private List<YargProfile> _profiles;
        private double _deadline, _measurementEnd, _measurementStart;
        private bool _launched, _measuring, _finishing;
        private int _frames, _unfocusedFrames, _overBudgetFrames, _lastFrame = -1;
        private double _wallTotal, _wallMaximum;
        private JObject _result;
        private int _runtimeErrors;
        private string _firstRuntimeError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Force argument initialization before checking the opt-in flag.
            _ = CommandLineArgs.PersistentDataPath;
            if (!PerformanceFixtureLaunch.Requested) return;
            _libraryReady = false;
            _libraryError = null;
            var go = new GameObject("PerformanceFixtureRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<PerformanceFixtureRunner>();
        }

        public static void LibraryReady(string error = null)
        {
            if (!PerformanceFixtureLaunch.Requested) return;
            _libraryError = error;
            _libraryReady = true;
        }

        private void Awake()
        {
            Application.logMessageReceived += ObserveLog;
            _fixture = PerformanceFixtureLaunch.Definition;
            _deadline = Time.realtimeSinceStartupAsDouble + (_fixture?.LoadTimeoutSeconds ?? 30);
            _result = new JObject
            {
                ["version"] = 1, ["startedUtc"] = DateTime.UtcNow.ToString("O"),
                ["unityVersion"] = Application.unityVersion, ["gameVersion"] = Application.version,
                ["os"] = SystemInfo.operatingSystem, ["device"] = SystemInfo.deviceModel,
                ["gpu"] = SystemInfo.graphicsDeviceName, ["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString(),
                ["processor"] = SystemInfo.processorType, ["processorCount"] = SystemInfo.processorCount,
                ["dataDirectory"] = PerformanceFixtureLaunch.DataDirectory
            };
        }

        private void OnDestroy() => Application.logMessageReceived -= ObserveLog;

        private void ObserveLog(string message, string stackTrace, LogType type)
        {
            if (!_launched || _finishing || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            _runtimeErrors++;
            if (_firstRuntimeError == null) _firstRuntimeError = message.Length > 4096 ? message.Substring(0, 4096) : message;
        }

        private void LateUpdate()
        {
            if (_finishing) return;
            try { Tick(); }
            catch (Exception ex) { Finish("failed", ex.ToString()); }
        }

        private void Tick()
        {
            if (_runtimeErrors > 0) throw new InvalidOperationException("Gameplay reported an error: " + _firstRuntimeError);
            if (PerformanceFixtureLaunch.Error != null) throw new InvalidOperationException(PerformanceFixtureLaunch.Error);
            if (Time.realtimeSinceStartupAsDouble > _deadline) throw new TimeoutException("Fixture phase timed out");
            if (!_launched)
            {
                if (!_libraryReady) return;
                if (_libraryError != null) throw new InvalidOperationException("Library scan failed: " + _libraryError);
                if (LoadingScreen.IsActive) return;
                StartRun();
                return;
            }
            if (_game == null)
            {
                _game = FindFirstObjectByType<GameManager>();
                return;
            }
            if (!_game.IsSongStarted || !_game.Started) return;
            if (_profiles != null)
            {
                ValidateLoadedPlayers();
                _profiles = null;
                _deadline = Time.realtimeSinceStartupAsDouble + _fixture.RunTimeoutSeconds;
                _measurementStart = _fixture.WarmupSeconds;
                _measurementEnd = _fixture.DurationSeconds.HasValue
                    ? _measurementStart + _fixture.DurationSeconds.Value : _game.SongLength;
                if (_measurementStart >= _game.SongLength || _measurementEnd > _game.SongLength)
                    throw new InvalidOperationException("Requested measurement lies outside the loaded song");
                _result["songLengthSeconds"] = _game.SongLength;
                _result["measurementStartSongTime"] = _measurementStart;
                _result["measurementEndSongTime"] = _measurementEnd;
                _result["settings"] = PerformanceFixtureSettings.Snapshot();
                _result["width"] = Screen.width;
                _result["height"] = Screen.height;
                _result["refreshHz"] = Screen.currentResolution.refreshRateRatio.value;
                _result["effectiveVSync"] = QualitySettings.vSyncCount;
                _result["effectiveTargetFps"] = Application.targetFrameRate;
                Status("warmup");
            }
            if (_game.Paused) throw new InvalidOperationException("Gameplay paused during fixture run");
            if (_game.SongTime < _measurementStart) return;
            if (!_measuring)
            {
                _measuring = true;
                _result["firstMeasurementFrame"] = Time.frameCount;
                Status("measuring");
            }
            var probe = StutterProbe.Instance;
            if (probe == null || probe.CaptureError != null)
                throw new InvalidOperationException("Frame capture unavailable: " + probe?.CaptureError);
            var sample = probe.Latest;
            if (sample.Frame != _lastFrame && sample.HasGameplay && sample.WallMs > 0)
            {
                _lastFrame = sample.Frame;
                _frames++;
                _wallTotal += sample.WallMs;
                _wallMaximum = Math.Max(_wallMaximum, sample.WallMs);
                if (!sample.Focused) _unfocusedFrames++;
                if (sample.BudgetMs > 0 && sample.WallMs > sample.BudgetMs) _overBudgetFrames++;
            }
            if (_game.SongTime >= _measurementEnd)
            {
                _result["lastMeasurementFrame"] = Time.frameCount;
                _result["bandScore"] = _game.BandScore;
                _result["players"] = new JArray(_game.Players.Select(player => new JObject
                {
                    ["score"] = player.Score, ["notesHit"] = player.NotesHit
                }));
                if (_frames == 0) throw new InvalidOperationException("No measured gameplay frames");
                if (_fixture.RequireFocus && _unfocusedFrames > 0)
                    throw new InvalidOperationException("Window lost focus during measurement");
                Finish("passed");
            }
        }

        private void StartRun()
        {
            _result["buildVersion"] = GlobalVariables.Instance.CurrentVersion;
            _result["sources"] = new JObject
            {
                ["updates"] = DescribeSource(_fixture.UpdatesDtaPath),
                ["upgrades"] = DescribeSource(_fixture.UpgradeDtaPath)
            };
            var matches = PerformanceFixtureCatalog.Find(SongContainer.UnfilteredSongs, _fixture.Song, _fixture.Mode == "discover");
            if (_fixture.Mode == "discover")
            {
                _result["songs"] = JArray.FromObject(matches.Select(PerformanceFixtureCatalog.Describe));
                Finish("discovered");
                return;
            }
            if (matches.Length != 1) throw new InvalidOperationException($"Expected exactly one song, found {matches.Length}; use discovery and song.directory to disambiguate");
            var song = matches[0];
            _profiles = PerformanceFixtureCatalog.BuildBots(song, _fixture.Bots);
            if (PlayerContainer.Players.Count != 0) throw new InvalidOperationException("Fixture data unexpectedly contains connected players");
            PerformanceFixtureSettings.Apply(_fixture.Settings);
            SettingsManager.Settings.TimingDiagnostics.Value = true;
            FramePacing.Apply();
            _result["song"] = JObject.FromObject(PerformanceFixtureCatalog.Describe(song));
            _result["requestedSettings"] = _fixture.Settings.DeepClone();
            _result["roster"] = new JArray(_profiles.Select(profile => new JObject
            {
                ["name"] = profile.Name, ["instrument"] = profile.CurrentInstrument.ToString(),
                ["difficulty"] = profile.CurrentDifficulty.ToString()
            }));
            foreach (var profile in _profiles)
            {
                if (!PlayerContainer.AddProfile(profile) || PlayerContainer.CreatePlayerFromProfile(profile, false) == null)
                    throw new InvalidOperationException("Could not create temporary fixture bot " + profile.Name);
            }
            GlobalVariables.State = PersistentState.Default;
            GlobalVariables.State.CurrentSong = song;
            GlobalVariables.State.SongSpeed = (float) _fixture.SongSpeed;
            _launched = true;
            _deadline = Time.realtimeSinceStartupAsDouble + _fixture.LoadTimeoutSeconds;
            Status("loadingSong");
            GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);
        }

        private static JToken DescribeSource(string path)
        {
            if (string.IsNullOrEmpty(path)) return JValue.CreateNull();
            var info = new FileInfo(path);
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return new JObject
            {
                ["path"] = path, ["bytes"] = info.Length,
                ["modifiedUtc"] = info.LastWriteTimeUtc.ToString("O"),
                ["dtaSha256"] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant()
            };
        }

        private void ValidateLoadedPlayers()
        {
            if (_game.YargPlayers.Count != _profiles.Count || _game.Players.Count != _profiles.Count)
                throw new InvalidOperationException("Loaded player count differs from fixture roster");
            var sources = new JArray();
            foreach (var player in _game.YargPlayers)
            {
                var playback = player.ResolvedDrumPlayback;
                if (playback == null) continue;
                sources.Add(new JObject
                {
                    ["instrument"] = player.Profile.CurrentInstrument.ToString(),
                    ["sourceFormat"] = playback.SourceFormat.ToString(),
                    ["sourceDifficulty"] = playback.SourceDifficulty.ToString()
                });
                // Catalog availability is checked before launch; also verify what the normal loader actually chose.
                if (player.Profile.CurrentInstrument == Instrument.EliteDrums && playback.SourceFormat != DrumSourceFormat.Elite &&
                    !player.Profile.IsModifierActive(Modifier.EnableEliteUpconversion))
                    throw new InvalidOperationException("Native Elite requested but loader selected a converted chart");
            }
            _result["drumSources"] = sources;
        }

        private void Status(string status) => PerformanceFixtureLaunch.WriteJson("status.json", new JObject
        {
            ["status"] = status, ["utc"] = DateTime.UtcNow.ToString("O")
        });

        private async void Finish(string status, string error = null)
        {
            if (_finishing) return;
            _finishing = true;
            int exitCode = status == "failed" ? 1 : 0;
            try
            {
                var probe = StutterProbe.Instance;
                _result["capturePath"] = probe?.CapturePath;
                _result["droppedRows"] = probe?.DroppedRows ?? 0;
                if (SettingsManager.SettingContainer.IsInitialized) SettingsManager.Settings.TimingDiagnostics.Value = false;
                if (probe != null)
                {
                    var completion = probe.FinishCaptureAsync();
                    if (await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(10))) != completion)
                        throw new TimeoutException("Timing writer did not finish within 10 seconds");
                    await completion;
                    if (probe.CaptureError != null || (int) _result["droppedRows"] > 0)
                    {
                        status = "failed";
                        error = "Incomplete timing capture: " + probe.CaptureError;
                        exitCode = 1;
                    }
                }
                _result["runtimeErrors"] = _runtimeErrors;
                _result["status"] = status;
                _result["error"] = error;
                _result["finishedUtc"] = DateTime.UtcNow.ToString("O");
                _result["measuredFrames"] = _frames;
                _result["meanWallMs"] = _frames > 0 ? _wallTotal / _frames : 0;
                _result["maxWallMs"] = _wallMaximum;
                _result["overBudgetFrames"] = _overBudgetFrames;
                _result["unfocusedFrames"] = _unfocusedFrames;
                PerformanceFixtureLaunch.WriteJson("result.json", _result);
                Status(status);
                Debug.Log($"[PerformanceFixture] {status}: {error}; results: {PerformanceFixtureLaunch.OutputDirectory}");
            }
            catch (Exception ex)
            {
                exitCode = 1;
                Debug.LogException(ex);
                try
                {
                    _result["status"] = "failed";
                    _result["error"] = ex.ToString();
                    _result["finishedUtc"] = DateTime.UtcNow.ToString("O");
                    PerformanceFixtureLaunch.WriteJson("result.json", _result);
                }
                catch (Exception writeError) { Debug.LogException(writeError); }
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(exitCode);
#else
            Application.Quit(exitCode);
#endif
        }
    }
}
