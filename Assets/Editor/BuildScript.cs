using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Batchmode/one-command prototype builds. Builds Addressables content, then the
    /// standalone player with the YARG_TEST_BUILD define (same "Party Vocals Prototype"
    /// branding as File → Make Test Build).
    ///
    /// CLI example (output dir optional; defaults to &lt;project&gt;/Builds):
    ///   Unity -batchmode -nographics -quit -projectPath &lt;tree&gt;/YARG \
    ///         -executeMethod Editor.BuildScript.BuildWindows -buildOutput /path/to/out
    ///
    /// Requires the matching Build Support module installed in Unity Hub. The *Mono* entry
    /// points cross-compile from any host; the *IL2CPP* entry points give better runtime
    /// performance but only build on a matching host (no cross-compile — e.g. Windows IL2CPP
    /// must be built on Windows). See docs/party-vocals-prototype-overview-and-build.md.
    /// </summary>
    public static class BuildScript
    {
        private const string DefineSymbol = "YARG_TEST_BUILD";

        // Mono backend — cross-compiles from any host.
        [MenuItem("File/Prototype Build/Windows (x64, Mono)", false, 230)]
        public static void BuildWindows() =>
            Build(BuildTarget.StandaloneWindows64, "Windows", "YARG.exe", ScriptingImplementation.Mono2x);

        [MenuItem("File/Prototype Build/Linux (x64, Mono)", false, 231)]
        public static void BuildLinux() =>
            Build(BuildTarget.StandaloneLinux64, "Linux", "YARG.x86_64", ScriptingImplementation.Mono2x);

        [MenuItem("File/Prototype Build/macOS (universal, Mono)", false, 232)]
        public static void BuildMac() =>
            Build(BuildTarget.StandaloneOSX, "Mac", "YARG.app", ScriptingImplementation.Mono2x);

        [MenuItem("File/Prototype Build/All Platforms (Mono)", false, 233)]
        public static void BuildAll()
        {
            BuildWindows();
            BuildLinux();
            BuildMac();
        }

        [MenuItem("File/Prototype Build/Mac + Linux (Mono)", false, 234)]
        public static void BuildMacLinux()
        {
            BuildLinux();
            BuildMac();
        }

        // IL2CPP backend — better runtime perf, but only builds on a matching host (no
        // cross-compile). Output goes to a separate "<platform>-IL2CPP" folder so it can sit
        // beside the Mono build for an A/B performance comparison.
        [MenuItem("File/Prototype Build/Windows (x64, IL2CPP)", false, 264)]
        public static void BuildWindowsIL2CPP() =>
            Build(BuildTarget.StandaloneWindows64, "Windows", "YARG.exe", ScriptingImplementation.IL2CPP);

        [MenuItem("File/Prototype Build/Linux (x64, IL2CPP)", false, 265)]
        public static void BuildLinuxIL2CPP() =>
            Build(BuildTarget.StandaloneLinux64, "Linux", "YARG.x86_64", ScriptingImplementation.IL2CPP);

        [MenuItem("File/Prototype Build/macOS (universal, IL2CPP)", false, 266)]
        public static void BuildMacIL2CPP() =>
            Build(BuildTarget.StandaloneOSX, "Mac", "YARG.app", ScriptingImplementation.IL2CPP);

        private static void Build(BuildTarget target, string baseSubdir, string exeName,
            ScriptingImplementation backend)
        {
            // Output dir: -buildOutput <dir> CLI arg, else <project>/Builds. IL2CPP builds go
            // to "<platform>-IL2CPP" so they don't overwrite the Mono build (A/B side by side).
            string subdir = backend == ScriptingImplementation.IL2CPP ? baseSubdir + "-IL2CPP" : baseSubdir;
            string root = GetArg("-buildOutput")
                ?? Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Builds");
            string outDir = Path.Combine(root, subdir);
            Directory.CreateDirectory(outDir);
            string locationPath = Path.Combine(outDir, exeName);

            // Mono cross-compiles from any host; IL2CPP only builds on a matching host.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);

            // Switch active target (first switch per platform reimports — slow).
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target))
            {
                Debug.LogError($"[BuildScript] Could not switch to {target}. " +
                    "Is the Build Support module installed in Unity Hub?");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // YARG loads content through Addressables — build it before the player.
            AddressableAssetSettings.BuildPlayerContent();

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPath,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { DefineSymbol },
            };

            BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] {target} succeeded → {locationPath} ({summary.totalSize} bytes)");
            }
            else
            {
                Debug.LogError($"[BuildScript] {target} {summary.result}: {summary.totalErrors} error(s)");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }

            return null;
        }
    }
}
