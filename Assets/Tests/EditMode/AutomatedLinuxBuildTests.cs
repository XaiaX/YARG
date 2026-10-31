// pattern: Imperative Shell
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace YARG.Tests.EditMode
{
    public sealed class AutomatedLinuxBuildTests
    {
        [Test]
        public void SavedEditorOptionsCanBeRecoveredAfterBuildProcessExits()
        {
            var type = Type.GetType("Editor.AutomatedLinuxBuild, Assembly-CSharp-Editor", true);
            var capture = type.GetMethod("CaptureSettings", BindingFlags.Static | BindingFlags.NonPublic);
            var restore = type.GetMethod("RestoreSettingsFromFile", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(capture, Is.Not.Null, "Recovery settings must survive the build process.");
            Assert.That(restore, Is.Not.Null);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            bool development = EditorUserBuildSettings.development;
            bool profiler = EditorUserBuildSettings.connectProfiler;
            bool debugging = EditorUserBuildSettings.allowDebugging;
            bool deep = EditorUserBuildSettings.buildWithDeepProfilingSupport;
            try
            {
                capture.Invoke(null, new object[] { path });
                EditorUserBuildSettings.development = !development;
                EditorUserBuildSettings.connectProfiler = !profiler;
                EditorUserBuildSettings.allowDebugging = !debugging;
                EditorUserBuildSettings.buildWithDeepProfilingSupport = !deep;
                restore.Invoke(null, new object[] { path });
                Assert.That(EditorUserBuildSettings.development, Is.EqualTo(development));
                Assert.That(EditorUserBuildSettings.connectProfiler, Is.EqualTo(profiler));
                Assert.That(EditorUserBuildSettings.allowDebugging, Is.EqualTo(debugging));
                Assert.That(EditorUserBuildSettings.buildWithDeepProfilingSupport, Is.EqualTo(deep));
            }
            finally
            {
                EditorUserBuildSettings.development = development;
                EditorUserBuildSettings.connectProfiler = profiler;
                EditorUserBuildSettings.allowDebugging = debugging;
                EditorUserBuildSettings.buildWithDeepProfilingSupport = deep;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
