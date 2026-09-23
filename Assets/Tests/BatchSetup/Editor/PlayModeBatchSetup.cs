// Test-run infrastructure: batchmode PlayMode test runs need a saved, empty scene
// open when the Unity Test Framework enters play mode. Without one, batchmode falls
// back to the first build scene (Assets/Scenes/PersistentScene.unity), which boots
// the full game and starves the test runner. Invoked via:
//   -executeMethod YARG.Tests.PlayModeBatchSetup.PrepareEmptySceneForTests -quit
// The saved scene path is then picked up as the restored scene for the subsequent
// -runTests invocation.
namespace YARG.Tests
{
    public static class PlayModeBatchSetup
    {
        public const string BootScenePath = "Assets/Tests/PlayMode/TestRunBootScene.unity";

        public static void PrepareEmptySceneForTests()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // A completely empty scene is rejected in batchmode play mode: Unity falls back
            // to the first enabled build scene (PersistentScene), which boots the full game
            // and starves the test runner. One bare GameObject makes the scene "non-empty"
            // so batchmode plays this scene instead.
            new UnityEngine.GameObject("TestRunBootRoot");

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, BootScenePath);
            UnityEditor.EditorApplication.Exit(0);
        }
    }
}
