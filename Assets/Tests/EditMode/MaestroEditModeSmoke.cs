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

        [Test]
        public void Setup_Menu_Reserves_Header_And_Help_Bar_Space()
        {
            const string path = "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, $"Could not load {path}.");
            var header = FindRequired(prefab.transform, "Header").GetComponent<RectTransform>();
            var body = FindRequired(prefab.transform, "Body").GetComponent<RectTransform>();
            var footer = FindRequired(prefab.transform, "Footer").GetComponent<RectTransform>();
            var listImage = FindRequired(prefab.transform, "Body/PlayerScroll").GetComponent<Image>();

            Assert.That(header.anchoredPosition.y, Is.LessThanOrEqualTo(-40f));
            Assert.That(body.sizeDelta.y, Is.LessThanOrEqualTo(-240f));
            Assert.That(footer.anchoredPosition.y, Is.GreaterThanOrEqualTo(80f));
            Assert.That(listImage.color.r, Is.LessThanOrEqualTo(0.1f));
            Assert.That(listImage.color.a, Is.LessThanOrEqualTo(0.3f));
        }

        [Test]
        public void Player_Row_Uses_Distinct_Readable_Columns()
        {
            const string path = "Assets/Prefabs/Menu/Maestro/MaestroPlayerRow.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, $"Could not load {path}.");
            var background = prefab.GetComponent<Image>();
            var name = FindRequired(prefab.transform, "Name").GetComponent<RectTransform>();
            var status = FindRequired(prefab.transform, "Status").GetComponent<RectTransform>();
            var setup = FindRequired(prefab.transform, "Setup").GetComponent<RectTransform>();
            var modifiers = FindRequired(prefab.transform, "Modifiers").GetComponent<RectTransform>();
            var selected = FindRequired(prefab.transform, "SelectedBackground").GetComponent<RectTransform>();

            Assert.That(background.color.r, Is.LessThanOrEqualTo(0.1f));
            Assert.That(background.color.a, Is.LessThanOrEqualTo(0.3f));
            Assert.That(name.anchorMin.x, Is.LessThan(status.anchorMin.x));
            Assert.That(status.anchorMin.x, Is.LessThan(setup.anchorMin.x));
            Assert.That(setup.anchorMin.x, Is.LessThan(modifiers.anchorMin.x));
            Assert.That(selected.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(selected.anchorMax, Is.EqualTo(Vector2.one));

            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component.GetType().FullName != "TMPro.TextMeshProUGUI")
                    continue;

                var fontSize = new SerializedObject(component).FindProperty("m_fontSize");
                Assert.That(fontSize, Is.Not.Null, $"Could not inspect {component.name}'s font size.");
                Assert.That(fontSize.floatValue, Is.LessThanOrEqualTo(24f),
                    $"{component.name} is too large for a 72px row.");
            }
        }

        private static Transform FindRequired(Transform root, string path)
        {
            var child = root.Find(path);
            Assert.That(child, Is.Not.Null, $"Could not find prefab object '{path}'.");
            return child;
        }
    }
}
