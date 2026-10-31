// pattern: Imperative Shell
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Editor
{
    /// <summary>Normal Linux builds for scripts/build_linux.py; target switching belongs to the CLI wrapper.</summary>
    public static class AutomatedLinuxBuild
    {
        [Serializable]
        private sealed class Receipt
        {
            public string status = "Succeeded";
            public string target;
        }

        [Serializable]
        private sealed class SavedSettings
        {
            public int version;
            public bool development;
            public bool profiler;
            public bool debugging;
            public bool deepProfiling;
            public AddressableAssetSettings.PlayerBuildOption contentBuild;
        }

        public static void Build()
        {
            RequireTarget(BuildTarget.StandaloneLinux64);
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
                throw new InvalidOperationException("Linux Build Support is not installed for this Editor.");
            // Linux Mono cross-builds from macOS without changing the shared Standalone backend setting.
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.Mono2x)
                throw new InvalidOperationException("This command requires the Standalone Mono backend; no backend setting was changed.");

            string output = RequiredArgument("-buildOutput");
            string receipt = RequiredArgument("-buildReceipt");
            string settingsState = RequiredArgument("-settingsState");
            if (!Directory.Exists(output)) throw new DirectoryNotFoundException(output);
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No enabled build scenes.");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("Addressables settings are missing.");

            // Recovery runs in another process after cancellation or an Editor crash.
            CaptureSettings(settingsState);
            try
            {
                bool profile = Environment.GetCommandLineArgs().Contains("-profileBuild");
                EditorUserBuildSettings.development = profile;
                EditorUserBuildSettings.connectProfiler = profile;
                EditorUserBuildSettings.allowDebugging = false;
                EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
                AddressableAssetSettings.BuildPlayerContent(out var content);
                if (content == null || !string.IsNullOrEmpty(content.Error))
                    throw new InvalidOperationException("Addressables build failed: " + content?.Error);

                // Content was already built and checked; prevent a second automatic content build.
                settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(output, "YARG.x86_64"),
                    target = BuildTarget.StandaloneLinux64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = profile ? BuildOptions.Development | BuildOptions.ConnectWithProfiler : BuildOptions.None,
                    extraScriptingDefines = new[] { "YARG_NIGHTLY_BUILD" }
                };
                var result = BuildPipeline.BuildPlayer(options).summary;
                if (result.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"Linux player build {result.result}: {result.totalErrors} errors.");
            }
            finally
            {
                RestoreSettingsFromFile(settingsState);
            }

            WriteReceipt(receipt);
        }

        public static void RestoreMac()
        {
            RequireTarget(BuildTarget.StandaloneOSX);
            RestoreSettingsFromFile(RequiredArgument("-settingsState"));
            WriteReceipt(RequiredArgument("-restoreReceipt"));
        }

        private static void CaptureSettings(string path)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("Addressables settings are missing.");
            File.WriteAllText(path, JsonUtility.ToJson(new SavedSettings
            {
                version = 1,
                development = EditorUserBuildSettings.development,
                profiler = EditorUserBuildSettings.connectProfiler,
                debugging = EditorUserBuildSettings.allowDebugging,
                deepProfiling = EditorUserBuildSettings.buildWithDeepProfilingSupport,
                contentBuild = settings.BuildAddressablesWithPlayerBuild
            }, true));
        }

        private static void RestoreSettingsFromFile(string path)
        {
            // No state means startup failed before our build method changed settings.
            if (!File.Exists(path)) return;
            var state = JsonUtility.FromJson<SavedSettings>(File.ReadAllText(path));
            if (state == null || state.version != 1)
                throw new InvalidOperationException("Unrecognized saved build settings: " + path);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("Addressables settings are missing.");
            EditorUserBuildSettings.development = state.development;
            EditorUserBuildSettings.connectProfiler = state.profiler;
            EditorUserBuildSettings.allowDebugging = state.debugging;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = state.deepProfiling;
            if (settings.BuildAddressablesWithPlayerBuild != state.contentBuild)
            {
                settings.BuildAddressablesWithPlayerBuild = state.contentBuild;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
        }

        private static void RequireTarget(BuildTarget expected)
        {
            if (!Application.isBatchMode || EditorUserBuildSettings.activeBuildTarget != expected)
                throw new InvalidOperationException($"Run in batch mode with -buildTarget selecting {expected} before scripts load.");
        }

        private static string RequiredArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException("Missing command-line argument " + name);
            return Path.GetFullPath(args[index + 1]);
        }

        private static void WriteReceipt(string path)
        {
            File.WriteAllText(path, JsonUtility.ToJson(new Receipt
            {
                target = EditorUserBuildSettings.activeBuildTarget.ToString()
            }, true));
        }
    }
}
