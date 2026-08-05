// pattern: Imperative Shell

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Tests.EditMode
{
    public sealed class MaestroEditModeSmoke
    {
        [Test]
        public void Smoke_Assembly_Loads_And_Runs()
        {
            Assert.That(typeof(GameObject), Is.Not.Null);
        }

        [Test]
        public void Setup_Menu_Prefab_Has_Top_Level_Canvas_Stack()
        {
            const string path = "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, $"Could not load {path}.");
            Assert.That(prefab.GetComponent<Canvas>(), Is.Not.Null,
                "Top-level menu prefabs need their own Canvas to render under Menu Manager.");
            Assert.That(prefab.GetComponent<CanvasScaler>(), Is.Not.Null,
                "Top-level menu prefabs need a CanvasScaler matching the other menu pages.");
            Assert.That(prefab.GetComponent<GraphicRaycaster>(), Is.Not.Null,
                "Top-level menu prefabs need a GraphicRaycaster for pointer interaction.");
        }

        [Test]
        public void Difficulty_Select_Preserves_Direct_Gameplay_Path_When_Maestro_Is_Disabled()
        {
            const string path = "Assets/Script/Menu/DifficultySelect/DifficultySelectMenu.cs";
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

            Assert.That(script, Is.Not.Null, $"Could not load {path}.");
            Assert.That(script.text, Does.Contain("if (SettingsManager.Settings.MaestroEnable.Value)"),
                "Difficulty Select must only open Maestro setup when Maestro is enabled.");
            Assert.That(script.text, Does.Contain("ApplyVocalSessionModifiers();"),
                "The disabled Maestro path must preserve normal vocal modifier finalization.");
            Assert.That(script.text, Does.Contain("GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);"),
                "Difficulty Select must retain the direct gameplay path when Maestro is disabled.");
        }
    }
}
