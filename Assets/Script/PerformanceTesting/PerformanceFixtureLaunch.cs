// pattern: Imperative Shell
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace YARG.PerformanceTesting
{
    /// <summary>Reserves a disposable data directory before any normal profiles/settings are loaded.</summary>
    public static class PerformanceFixtureLaunch
    {
        public static bool Requested { get; private set; }
        public static string OutputDirectory { get; private set; }
        public static string DataDirectory { get; private set; }
        public static string Error { get; private set; }
        public static PerformanceFixtureDefinition Definition { get; private set; }
        private static FileStream _reservation;

        public static void Initialize(string fixturePath, string outputPath)
        {
            if (Requested || string.IsNullOrWhiteSpace(fixturePath)) return;
            Requested = true;
            try
            {
                if (string.IsNullOrWhiteSpace(outputPath) || !Path.IsPathRooted(outputPath))
                    throw new ArgumentException("-performance-output must be an absolute, unused run directory");
                OutputDirectory = Path.GetFullPath(outputPath);
                DataDirectory = Path.Combine(OutputDirectory, "data");
                bool resumeEditorRun = false;
#if UNITY_EDITOR
                // Entering Play Mode reloads this assembly after Editor startup already reserved the run.
                resumeEditorRun = UnityEditor.SessionState.GetString("YARG.PerformanceFixture.Output", "") == OutputDirectory;
#endif
                if ((!resumeEditorRun && Directory.Exists(DataDirectory)) || File.Exists(Path.Combine(OutputDirectory, "result.json")))
                    throw new ArgumentException("fixture output already contains a run; choose a new output directory");
                Directory.CreateDirectory(OutputDirectory);
                _reservation = new FileStream(Path.Combine(OutputDirectory, "fixture.lock"), resumeEditorRun ? FileMode.Open : FileMode.CreateNew,
                    FileAccess.Write, FileShare.None);
#if UNITY_EDITOR
                UnityEditor.SessionState.SetString("YARG.PerformanceFixture.Output", OutputDirectory);
#endif
                Directory.CreateDirectory(DataDirectory);
                if (new FileInfo(fixturePath).Length > 1024 * 1024)
                    throw new ArgumentException("fixture exceeds the 1 MiB size limit");
                Definition = PerformanceFixtureDefinition.Parse(File.ReadAllText(fixturePath));
                foreach (string folder in Definition.SongFolders)
                    if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"song folder not found: {folder}");
                foreach (string source in new[] { Definition.UpdatesDtaPath, Definition.UpgradeDtaPath })
                    if (!string.IsNullOrEmpty(source) && !File.Exists(source))
                        throw new FileNotFoundException("selected song source not found", source);
                WriteSettingsSeed();
                WriteJson("fixture.json", JObject.FromObject(Definition));
                WriteJson("status.json", new JObject { ["status"] = "starting" });
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                // Even malformed commands must never fall back to the real user's data directory.
                _reservation?.Dispose();
                _reservation = null;
                // Use a fresh fallback on every initialization error, including reused output directories.
                OutputDirectory = Path.Combine(Path.GetTempPath(), "yarg-fixture-error-" + Guid.NewGuid().ToString("N"));
                DataDirectory = Path.Combine(OutputDirectory, "data");
                Directory.CreateDirectory(DataDirectory);
                WriteSettingsSeed();
            }
        }

        private static void WriteSettingsSeed()
        {
            var settings = new JObject
            {
                ["SongFolders"] = JArray.FromObject(Error == null && Definition != null ? Definition.SongFolders : new System.Collections.Generic.List<string>()),
                ["SongUpdatesDtaPath"] = Error == null ? Definition?.UpdatesDtaPath ?? "" : "",
                ["SongUpgradesDtaPath"] = Error == null ? Definition?.UpgradeDtaPath ?? "" : "",
                ["ExplicitSongSourcesConfigured"] = true, ["ShowExplicitSongSourcesWarning"] = false,
                ["ShowAntiPiracyDialog"] = false, ["ShowEngineInconsistencyDialog"] = false,
                ["ShowExperimentalWarningDialog"] = false, ["ReconnectProfiles"] = false,
                ["AutoCreateProfiles"] = false, ["MaestroEnable"] = false,
                ["SaveScoresWithBots"] = false, ["SuppressReplayAnalysisDialogs"] = true,
                ["FrameGraph"] = false, ["TimingDiagnostics"] = false
            };
            File.WriteAllText(Path.Combine(DataDirectory, "settings.json"), settings.ToString(Formatting.Indented));
        }

        public static void WriteJson(string fileName, JObject value)
        {
            string path = Path.Combine(OutputDirectory, fileName);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, value.ToString(Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
