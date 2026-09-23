using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Editor
{
    [InitializeOnLoad]
    public class StartScene
    {
        static StartScene()
        {
            // Batchmode (-runTests) must enter play mode on the Unity Test Framework's
            // bootstrap scene (InitTestScene) instead of forcing the first build scene;
            // forcing PersistentScene there boots the entire game and starves the runner.
            if (Application.isBatchMode)
            {
                return;
            }

            EditorSceneManager.playModeStartScene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path);
        }
    }
}