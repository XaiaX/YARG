// pattern: Imperative Shell
using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Editor
{
    /// <summary>Normal rendered scene startup for local fixture smoke checks, not performance comparisons.</summary>
    public static class PerformanceFixtureEditorSmoke
    {
        public static void Run()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-performance-fixture") < 0)
                throw new ArgumentException("Editor fixture smoke requires -performance-fixture and -performance-output");
            EditorSceneManager.OpenScene("Assets/Scenes/PersistentScene.unity");
            EditorApplication.EnterPlaymode();
        }
    }
}
