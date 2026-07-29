using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using YARG.Audio.BASS;
using YARG.Core.Logging;
using YARG.Core.Audio;
using YARG.Helpers;
using YARG.Input;
using YARG.Integration;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Player;
using YARG.Playlists;
using YARG.Replays;
using YARG.Scores;
using YARG.Settings;
using YARG.Settings.Customization;

namespace YARG
{
    public enum SceneIndex
    {
        Persistent,
        Menu,
        Gameplay,
        Calibration,
        Score
    }

    [DefaultExecutionOrder(-5000)]
    public class GlobalVariables : MonoSingleton<GlobalVariables>
    {
        public List<YargPlayer> Players { get; private set; }

        public static bool OfflineMode    { get; private set; }
        public static bool VerboseReplays { get; private set; }

        public static string PersistentDataPathOverride { get; private set; }

        public static PersistentState State = PersistentState.Default;

        public SceneIndex CurrentScene { get; private set; } = SceneIndex.Persistent;

        // FORK-LOCAL (Party Vocals prototype branding): the version label for non-editor
        // builds. Test builds fall back to this when no (gitignored) Resources/version.txt
        // is present; this is only ever displayed (watermark, main menu) or stored as a
        // string (score GameVersion), never parsed. Restore "v0.14" if merged upstream.
        // See docs/party-vocals-prototype-build-branding.md.
        public string CurrentVersion { get; private set; } = "Party Vocals Prototype";

        protected override void SingletonAwake()
        {
            CurrentVersion = LoadVersion();
            YargLogger.LogFormatInfo("YARG {0}", CurrentVersion);

            // Command line arguments

            if (CommandLineArgs.Offline)
            {
                OfflineMode = true;
                YargLogger.LogInfo("Playing in offline mode");
            }

            if (CommandLineArgs.VerboseReplays)
            {
                VerboseReplays = true;
                YargLogger.LogInfo("Verbose replays enabled");
            }

            if (!string.IsNullOrEmpty(CommandLineArgs.DownloadLocation))
            {
                PathHelper.SetPathsFromDownloadLocation(CommandLineArgs.DownloadLocation);
            }

            // TODO: Actually respect the PersistentDataPath arg

            // Initialize important classes

            ReplayContainer.Init();
            ScoreContainer.Init();
            PlaylistContainer.Initialize();
            CustomContentManager.Initialize();
            LocalizationManager.Initialize(CommandLineArgs.Language);

            int profileCount = PlayerContainer.LoadProfiles();
            YargLogger.LogFormatInfo("Loaded {0} profiles", profileCount);

            int savedCount = PlayerContainer.SaveProfiles(false);
            YargLogger.LogFormatInfo("Saved {0} profiles", savedCount);

            GlobalAudioHandler.Initialize<BassAudioManager>();

            Players = new List<YargPlayer>();

            // Set alpha fading (on the tracks) to on
            // (this is mostly for the editor, but just in case)
            Shader.SetGlobalFloat("_IsFading", 1f);
        }

        private void Start()
        {
            SettingsManager.LoadSettings();
            InputManager.Initialize();

            // Spawn the persistent Maestro controller on this GameObject so it survives
            // Menu/Gameplay/Score scene transitions. The host is disabled by default;
            // it only starts when explicitly enabled (Phase 4 toggle / future setting).
            if (GetComponent<Integration.Maestro.MaestroController>() == null)
            {
                gameObject.AddComponent<Integration.Maestro.MaestroController>();
            }

            LoadScene(SceneIndex.Menu);
        }

        // Tracks whether audio was muted because the window lost focus,
        // so it can be restored when focus returns.
        private bool _mutedFromFocusLoss;

        /// <summary>
        /// Read-only query for whether the master bus is muted due to focus loss.
        /// Maestro uses this to reject master-volume commands that would unmute YARG
        /// while <see cref="SettingsManager.Settings.MuteOnFocusLoss"/> is active.
        /// </summary>
        public bool IsMutedFromFocusLoss => _mutedFromFocusLoss;

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                if (SettingsManager.Settings.MuteOnFocusLoss.Value && !_mutedFromFocusLoss)
                {
                    GlobalAudioHandler.SetMasterVolume(0);
                    _mutedFromFocusLoss = true;
                }
            }
            else if (_mutedFromFocusLoss)
            {
                GlobalAudioHandler.SetMasterVolume(SettingsManager.Settings.MasterMusicVolume.Value);
                _mutedFromFocusLoss = false;
            }
        }

#if UNITY_EDITOR

        // For respecting the editor's mute button
        private bool _previousMute;

        private void Update()
        {
            bool muted = UnityEditor.EditorUtility.audioMasterMute;
            if (muted != _previousMute)
            {
                GlobalAudioHandler.SetMasterVolume(muted ? 0 : SettingsManager.Settings.MasterMusicVolume.Value);
                _previousMute = muted;
            }
        }

#endif

        protected override void SingletonDestroy()
        {
            SettingsManager.SaveSettings();
            PlayerContainer.SaveProfiles();
            PlaylistContainer.SaveAll();
            CustomContentManager.SaveAll();

            ReplayContainer.Destroy();
            ScoreContainer.Destroy();
            InputManager.Destroy();
            PlayerContainer.Destroy();
            GlobalAudioHandler.Close();

#if UNITY_EDITOR
            // Set alpha fading (on the tracks) to off
            Shader.SetGlobalFloat("_IsFading", 0f);
#endif
        }

        private async void LoadSceneAdditive(SceneIndex scene)
        {
            CurrentScene = scene;

            GameStateFetcher.SetSceneIndex(scene);

            await SceneManager.LoadSceneAsync((int) scene, LoadSceneMode.Additive);

            // When complete, set the newly loaded scene to the active one
            SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex((int) scene));
            Navigator.Instance.DisableMenuInputs = false;

            await Resources.UnloadUnusedAssets();
            GC.Collect();
        }

        public void LoadScene(SceneIndex scene)
        {
            Navigator.Instance.DisableMenuInputs = true;
            // Unload the current scene and load in the new one, or just load in the new one
            if (CurrentScene != SceneIndex.Persistent)
            {
                // Unload the current scene
                var asyncOp = SceneManager.UnloadSceneAsync((int) CurrentScene);

                // The load the new scene
                asyncOp.completed += _ => LoadSceneAdditive(scene);
            }
            else
            {
                LoadSceneAdditive(scene);
            }
        }

        // Due to the preprocessor, it doesn't know that an instance variable is being used
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private string LoadVersion()
        {
#if UNITY_EDITOR
            return LoadVersionFromGit();
#elif YARG_TEST_BUILD
            // FORK-LOCAL (Party Vocals prototype branding): display
            // "Party Vocals Prototype (<short commit>)" instead of the raw git string.
            // BuildGitCommitVersion writes version.txt = "{branch} b{count} ({sha})"; we keep
            // the prototype name (CurrentVersion's initializer) for branding and append only
            // the commit for build traceability. Restore the plain version.txt read below if
            // this branch is ever upstreamed. See docs/party-vocals-prototype-build-branding.md.
            var protoVersionFile = Resources.Load<TextAsset>("version");
            string commit = ExtractShortCommit(protoVersionFile?.text);
            return commit != null ? $"{CurrentVersion} ({commit})" : CurrentVersion;
#elif YARG_NIGHTLY_BUILD
            var versionFile = Resources.Load<TextAsset>("version");
            if (versionFile != null)
            {
                return versionFile.text;
            }
            else
            {
                return CurrentVersion;
            }
#else
            return CurrentVersion;
#endif
        }

#if YARG_TEST_BUILD
        // FORK-LOCAL (Party Vocals prototype branding): pull the short commit hash out of the
        // string BuildGitCommitVersion writes ("{branch} b{count} ({sha})"). Returns null when
        // the input is null/empty or has no "(...)" segment.
        private static string ExtractShortCommit(string gitVersion)
        {
            if (string.IsNullOrEmpty(gitVersion))
            {
                return null;
            }

            int open = gitVersion.LastIndexOf('(');
            int close = gitVersion.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return null;
            }

            string commit = gitVersion.Substring(open + 1, close - open - 1).Trim();
            return commit.Length > 0 ? commit : null;
        }
#endif

        public static string LoadVersionFromGit()
        {
            var process = new Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            // Branch
            process.StartInfo.Arguments = "rev-parse --abbrev-ref HEAD";
            process.Start();
            string branch = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            // Commit Count
            process.StartInfo.Arguments = "rev-list --count HEAD";
            process.Start();
            string commitCount = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            // Commit
            process.StartInfo.Arguments = "rev-parse --short HEAD";
            process.Start();
            string commit = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

#if YARG_NIGHTLY_BUILD
            return $"b{commitCount} ({commit})";
#else
            return $"{branch} b{commitCount} ({commit})";
#endif
        }
    }
}
