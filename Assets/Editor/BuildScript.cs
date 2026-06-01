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
    /// Requires the matching Build Support module installed in Unity Hub. Uses the Mono
    /// scripting backend because IL2CPP cannot cross-compile from a non-target host
    /// (e.g. Mac → Windows); Mono builds anywhere. See
    /// docs/party-vocals-prototype-overview-and-build.md.
    /// </summary>
    public static class BuildScript
    {
        private const string DefineSymbol = "YARG_TEST_BUILD";

        [MenuItem("File/Prototype Build/Windows (x64)", false, 230)]
        public static void BuildWindows() =>
            Build(BuildTarget.StandaloneWindows64, "Windows", "YARG.exe");

        [MenuItem("File/Prototype Build/Linux (x64)", false, 231)]
        public static void BuildLinux() =>
            Build(BuildTarget.StandaloneLinux64, "Linux", "YARG.x86_64");

        [MenuItem("File/Prototype Build/macOS", false, 232)]
        public static void BuildMac() =>
            Build(BuildTarget.StandaloneOSX, "Mac", "YARG.app");

        [MenuItem("File/Prototype Build/All Platforms", false, 244)]
        public static void BuildAll()
        {
            BuildWindows();
            BuildLinux();
            BuildMac();
        }

        private static void Build(BuildTarget target, string subdir, string exeName)
        {
            // Output dir: -buildOutput <dir> CLI arg, else <project>/Builds.
            string root = GetArg("-buildOutput")
                ?? Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Builds");
            string outDir = Path.Combine(root, subdir);
            Directory.CreateDirectory(outDir);
            string locationPath = Path.Combine(outDir, exeName);

            // Mono so the build cross-compiles from any host (IL2CPP can't, e.g. Mac→Windows).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

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
