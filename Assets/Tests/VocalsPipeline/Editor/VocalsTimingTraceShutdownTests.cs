using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Audio.PitchDetection;

namespace YARG.Tests.VocalsPipeline
{
    public sealed class VocalsTimingTraceShutdownTests
    {
        private const string KEY = "YARG.VocalsTimingTraceShutdownTests.";
        private const string TRACE_ENV = "YARG_VOCALS_TRACE";
        private const BindingFlags STATIC_PRIVATE = BindingFlags.Static | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            if (SessionState.GetBool(KEY + "active", false)) return;
            Assert.That(EditorApplication.isPlaying, Is.False);
            Assert.That(VocalsTimingTrace.Current, Is.Null, "Do not interrupt an existing trace.");
            SessionState.SetBool(KEY + "active", true);
            string previous = Environment.GetEnvironmentVariable(TRACE_ENV);
            SessionState.SetBool(KEY + "hadEnv", previous != null);
            SessionState.SetString(KEY + "env", previous ?? "");
            SessionState.SetBool(KEY + "optionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(KEY + "options", (int) EditorSettings.enterPlayModeOptions);
            SessionState.SetString(KEY + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "vocals-shutdown-" + Guid.NewGuid().ToString("N"));
            SessionState.SetString(KEY + "directory", directory);
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable(TRACE_ENV, Path.Combine(directory, "trace.csv"));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            if (!SessionState.GetBool(KEY + "active", false)) yield break;
            // Cleanup only: the lifecycle assertions run before this fallback shutdown.
            VocalsTimingTrace.Shutdown();
            Environment.SetEnvironmentVariable(TRACE_ENV, SessionState.GetBool(KEY + "hadEnv", false)
                ? SessionState.GetString(KEY + "env", "") : null);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(KEY + "optionsEnabled", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions) SessionState.GetInt(KEY + "options", 0);
            string startScene = SessionState.GetString(KEY + "startScene", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(startScene)
                ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
            string directory = SessionState.GetString(KEY + "directory", "");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            SessionState.SetBool(KEY + "active", false);
        }

        [UnityTest]
        public IEnumerator ExitPlayModeWithDomainReloadWritesFooter()
        {
            return ExitPlayModeWritesFooter(false);
        }

        [UnityTest]
        public IEnumerator ExitPlayModeWithoutDomainReloadWritesFooter()
        {
            return ExitPlayModeWritesFooter(true);
        }

        private static IEnumerator ExitPlayModeWritesFooter(bool disableDomainReload)
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = disableDomainReload
                ? EnterPlayModeOptions.DisableDomainReload : EnterPlayModeOptions.None;
            EditorSceneManager.playModeStartScene = null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Vocals trace shutdown test scene");
            yield return new EnterPlayMode();
            Assert.That(EditorApplication.isPlaying, Is.True);
            Assert.That(VocalsTimingTrace.Current, Is.Not.Null, "Runtime initialization must read the external trace environment.");
            int source = VocalsTimingTrace.NewSourceId();
            SessionState.SetInt(KEY + "source", source);
            VocalsTimingTrace.Emit(VocalsTraceEvent.Reset, source, 123.5);
            // Wait for the record to flush, so the pre-exit check proves this is an open trace without a footer.
            string path = Path.Combine(SessionState.GetString(KEY + "directory", ""), "trace.csv");
            double deadline = EditorApplication.timeSinceStartup + 5;
            while ((!File.Exists(path) || !File.ReadAllText(path).Contains(",123.5")) &&
                EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(File.ReadAllText(path), Does.Contain(",123.5"));
            Assert.That(File.ReadAllText(path), Does.Not.Contain("# summary,"));

            // No Dispose or Shutdown call: Unity's actual play-mode exit must close the writer.
            yield return new ExitPlayMode();
            Assert.That(EditorApplication.isPlaying, Is.False);
            Assert.That(VocalsTimingTrace.Current, Is.Null);
            Assert.That(VocalsTimingTrace.Enabled, Is.False);
            path = Path.Combine(SessionState.GetString(KEY + "directory", ""), "trace.csv");
            string[] lines = File.ReadAllLines(path);
            Assert.That(lines.Count(line => line.StartsWith("# summary,")), Is.EqualTo(1));
            Assert.That(lines.Last(), Is.EqualTo("# summary,written=1,dropped=0,errors=0,unwritten=0"));
            Assert.That(lines[1], Does.StartWith((int) VocalsTraceEvent.Reset + "," +
                SessionState.GetInt(KEY + "source", 0) + ","));
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Debug.Log("Verified actual ExitPlayMode vocals trace footer; domain reload disabled=" + disableDomainReload);
        }

        [Test]
        public void RepeatedShutdownClearsCurrentAndWritesOneFooter()
        {
            var text = new StringWriter();
            var trace = new VocalsTimingTrace(() => text);
            typeof(VocalsTimingTrace).GetProperty(nameof(VocalsTimingTrace.Current))
                .GetSetMethod(true).Invoke(null, new object[] { trace });
            VocalsTimingTrace.Emit(VocalsTraceEvent.Read, 1, 440);
            VocalsTimingTrace.Shutdown();
            Assert.That(VocalsTimingTrace.Current, Is.Null);
            Assert.That(trace.Completed, Is.True);
            Assert.That(trace.Written, Is.EqualTo(1));
            string result = text.ToString();
            Assert.DoesNotThrow(VocalsTimingTrace.Shutdown);
            Assert.DoesNotThrow(VocalsTimingTrace.Shutdown);
            Assert.That(text.ToString(), Is.EqualTo(result));
            Assert.That(result.Split('\n').Count(line => line.StartsWith("# summary,")), Is.EqualTo(1));
        }

        [Test]
        public void EditorShutdownRegistrationIsPresentAndIdempotent()
        {
            AssertBindings();
            var register = typeof(VocalsTimingTrace).GetMethod("RegisterEditorShutdown", STATIC_PRIVATE);
            Assert.That(register, Is.Not.Null);
            register.Invoke(null, null);
            register.Invoke(null, null);
            AssertBindings();
        }

        private static void AssertBindings()
        {
            AssertBinding(typeof(EditorApplication), "playModeStateChanged", "OnEditorPlayModeChanged");
            AssertBinding(typeof(AssemblyReloadEvents), "beforeAssemblyReload", nameof(VocalsTimingTrace.Shutdown));
            AssertBinding(typeof(EditorApplication), "quitting", nameof(VocalsTimingTrace.Shutdown));
        }

        private static void AssertBinding(Type owner, string eventName, string methodName)
        {
            // Inspect subscriptions only; never invoke Unity's global reload/quitting events in a test runner.
            var fields = owner.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            string internalName = "m_" + char.ToUpperInvariant(eventName[0]) + eventName.Substring(1) + "Event";
            var field = fields.FirstOrDefault(candidate => candidate.Name == internalName) ??
                fields.FirstOrDefault(candidate => candidate.Name == eventName);
            Assert.That(field, Is.Not.Null, "Unity event fields: " + string.Join(",", fields.Select(candidate => candidate.Name)));
            var method = typeof(VocalsTimingTrace).GetMethod(methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(ReadCallbacks(field.GetValue(null), 0).Count(callback => callback.Method == method), Is.EqualTo(1),
                owner.Name + "." + eventName + " must contain exactly one trace shutdown handler.");
        }

        private static System.Collections.Generic.IEnumerable<Delegate> ReadCallbacks(object storage, int depth)
        {
            if (storage == null || depth > 6 || storage is string ||
                storage.GetType().IsPrimitive || storage.GetType().IsEnum) yield break;
            if (storage is Delegate callback)
            {
                foreach (var entry in callback.GetInvocationList()) yield return entry;
                yield break;
            }
            if (storage is IEnumerable list)
            {
                foreach (object item in list)
                    foreach (var entry in ReadCallbacks(item, depth + 1)) yield return entry;
                yield break;
            }
            // Unity 6 wraps callbacks in performance-tracked delegate/list containers.
            foreach (var member in storage.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var entry in ReadCallbacks(member.GetValue(storage), depth + 1)) yield return entry;
            }
        }
    }
}
