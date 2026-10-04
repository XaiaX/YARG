using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YARG.Tests
{
    public static class PlayModeBatchSetup
    {
        private const string BOOT_SCENE_PATH = "Assets/Tests/PlayMode/TestRunBootScene.unity";

        public static void PrepareEmptySceneForTests()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BOOT_SCENE_PATH)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Inert PlayMode Test Boot");
            EditorSceneManager.SaveScene(scene, BOOT_SCENE_PATH);
            EditorApplication.Exit(0);
        }
    }
}
