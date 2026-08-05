// pattern: Imperative Shell

using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
            var listImage = FindRequired(prefab.transform, "Body/PlayerScroll").GetComponent<Image>();

            Assert.That(header.anchoredPosition.y, Is.LessThanOrEqualTo(-40f));
            Assert.That(body.sizeDelta.y, Is.LessThanOrEqualTo(-240f));
            Assert.That(listImage.color.r, Is.LessThanOrEqualTo(0.1f));
            Assert.That(listImage.color.a, Is.GreaterThanOrEqualTo(0.8f));
            Assert.That(listImage.sprite, Is.Not.Null,
                "The profile list should use the rounded panel sprite.");
        }

        [Test]
        public void Player_Row_Uses_Profile_Marker_Icon_And_Readable_Columns()
        {
            const string path = "Assets/Prefabs/Menu/Maestro/MaestroPlayerRow.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, $"Could not load {path}.");
            var background = prefab.GetComponent<Image>();
            var name = FindRequired(prefab.transform, "Name").GetComponent<RectTransform>();
            var icon = FindRequired(prefab.transform, "InstrumentIcon").GetComponent<RectTransform>();
            var setup = FindRequired(prefab.transform, "Setup").GetComponent<RectTransform>();
            var modifiers = FindRequired(prefab.transform, "Modifiers").GetComponent<RectTransform>();
            var selected = FindRequired(prefab.transform, "SelectedBackground").GetComponent<RectTransform>();

            Assert.That(background.color.r, Is.LessThanOrEqualTo(0.1f));
            Assert.That(background.color.a, Is.LessThanOrEqualTo(0.3f));
            Assert.That(prefab.transform.Find("Status"), Is.Null,
                "Bot state should be an inline marker, not a full-width column.");
            Assert.That(name.anchorMin.x, Is.LessThan(icon.anchorMin.x));
            Assert.That(icon.anchorMin.x, Is.LessThan(setup.anchorMin.x));
            Assert.That(setup.anchorMin.x, Is.LessThan(modifiers.anchorMin.x));
            Assert.That(selected.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(selected.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(selected.GetComponent<Image>().color.r, Is.GreaterThan(0.9f));
            Assert.That(selected.GetComponent<Image>().color.g, Is.GreaterThan(0.5f));
            Assert.That(selected.GetComponent<Image>().color.b, Is.LessThan(0.2f));

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

        [Test]
        public void Setup_Menu_Uses_Explicit_Option_Pickers()
        {
            const string prefabPath = "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab";
            const string scriptPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);

            Assert.That(prefab, Is.Not.Null, $"Could not load {prefabPath}.");
            Assert.That(script, Is.Not.Null, $"Could not load {scriptPath}.");

            var dropdownNames = prefab.GetComponentsInChildren<Component>(true)
                .Where(component => component.GetType().FullName == "TMPro.TMP_Dropdown")
                .Select(component => component.transform.parent.name)
                .ToArray();

            Assert.That(dropdownNames, Is.EquivalentTo(new[] { "InstrumentDropdown", "DifficultyDropdown" }));

            var menu = prefab.GetComponents<Component>()
                .Single(component => component.GetType().FullName ==
                    "YARG.Menu.Maestro.MaestroSetupMenu");
            var serializedMenu = new SerializedObject(menu);
            Assert.That(serializedMenu.FindProperty("_instrumentDropdown").objectReferenceValue,
                Is.Not.Null);
            Assert.That(serializedMenu.FindProperty("_difficultyDropdown").objectReferenceValue,
                Is.Not.Null);
            Assert.That(serializedMenu.FindProperty("_rightNavigationGroup").objectReferenceValue,
                Is.Not.Null);
            Assert.That(serializedMenu.FindProperty("_playButton").objectReferenceValue,
                Is.Not.Null);
            Assert.That(serializedMenu.FindProperty("_readyButton").objectReferenceValue,
                Is.Not.Null);
            Assert.That(serializedMenu.FindProperty("_modifierButton").objectReferenceValue,
                Is.Not.Null, "The Modifiers button must reference its navigatable component.");
            var modifierItemPrefab = serializedMenu.FindProperty("_modifierItemPrefab").objectReferenceValue;
            Assert.That(modifierItemPrefab, Is.Not.Null,
                "The Modifiers dialog must reference a ModifierItem component prefab.");
            Assert.That(modifierItemPrefab.GetType().FullName,
                Is.EqualTo("YARG.Menu.DifficultySelect.ModifierItem"),
                "The Modifiers dialog must reference the ModifierItem component, not its root GameObject.");
            Assert.That(prefab.transform.Find("Body/SelectedPlayerEditor/SelectedPlayerName"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Body/SelectedPlayerEditor/GameModeDropdown"), Is.Null);
            Assert.That(prefab.transform.Find("Body/SelectedPlayerEditor/GameModeButton"), Is.Null);
            Assert.That(prefab.transform.Find("Body/SelectedPlayerEditor/InstrumentButton"), Is.Null);
            Assert.That(prefab.transform.Find("Body/SelectedPlayerEditor/DifficultyButton"), Is.Null);
            Assert.That(script.text, Does.Not.Contain("CycleGameMode"));
            Assert.That(script.text, Does.Not.Contain("CycleInstrument"));
            Assert.That(script.text, Does.Not.Contain("CycleDifficulty"));
            Assert.That(script.text, Does.Contain("ShowModifierPicker"));
            Assert.That(script.text, Does.Contain("MenuAction.Blue"));
            Assert.That(script.text, Does.Contain("Controller Navigation Disabled"));
            Assert.That(script.text, Does.Contain("FinishEditingPlayer"));
            Assert.That(script.text, Does.Contain("BeginEditingPlayer"));
        }

        [Test]
        public void Modifier_Picker_Has_A_Closable_Dialog()
        {
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);

            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.text, Does.Contain("Menu.Common.Close"));
            Assert.That(menu.text, Does.Contain("dialog.AddDialogButton"));
        }

        [Test]
        public void Player_Row_Uses_Game_Mode_Icon_And_Large_Enough_Source_Rect()
        {
            const string rowPrefabPath = "Assets/Prefabs/Menu/Maestro/MaestroPlayerRow.prefab";
            const string rowScriptPath = "Assets/Script/Menu/Maestro/MaestroPlayerRow.cs";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rowPrefabPath);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(rowScriptPath);
            var icon = FindRequired(prefab.transform, "InstrumentIcon").GetComponent<RectTransform>();

            Assert.That(prefab, Is.Not.Null);
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("player.GameMode"));
            Assert.That(script.text, Does.Contain("gameMode.ToResourceName"));
            Assert.That(icon.sizeDelta.y, Is.GreaterThanOrEqualTo(48f),
                "The 512px source icon sheet supports a larger row icon without upscaling.");
        }

        [Test]
        public void Setup_Menu_Shows_Editor_And_Puts_Play_In_Scroll_Content()
        {
            const string prefabPath = "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var menu = prefab.GetComponents<Component>()
                .Single(component => component.GetType().FullName ==
                    "YARG.Menu.Maestro.MaestroSetupMenu");
            var serializedMenu = new SerializedObject(menu);

            Assert.That(prefab.transform.Find("Body/SelectedPlayerEditor").gameObject.activeSelf,
                Is.True, "The editor must remain visible while the left profile list has focus.");
            var play = prefab.transform.Find("Body/PlayerScroll/Viewport/PlayerContent/PlayButton");
            Assert.That(play,
                Is.Not.Null, "Play must be a visible scroll-content entry.");
            Assert.That(play.GetComponent<RectTransform>().sizeDelta.y,
                Is.LessThanOrEqualTo(72f), "Play must fit inside a profile row.");
            var playLabel = play.GetComponentsInChildren<Component>(true)
                .Single(component => component.GetType().FullName == "TMPro.TextMeshProUGUI");
            Assert.That(new SerializedObject(playLabel).FindProperty("m_text").stringValue,
                Is.EqualTo("Play"), "Play must have visible label text.");
            Assert.That(serializedMenu.FindProperty("_modifierButton").objectReferenceValue,
                Is.Not.Null);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(script.text, Does.Contain("_playButton.transform.parent.SetAsLastSibling"));
            Assert.That(script.text, Does.Contain("_rightNavigationGroup?.ClearSelection()"));
        }

        [Test]
        public void Setup_Menu_Uses_Dynamic_Controller_Lock_Help_Label()
        {
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            const string languagePath = "Assets/StreamingAssets/lang/en-US.json";
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);
            var language = File.ReadAllText(languagePath);

            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.text, Does.Contain("ControllersLocked"));
            Assert.That(menu.text, Does.Contain("ControllersUnlocked"));
            Assert.That(menu.text, Does.Contain("UpdateControllerLockHelpBar"));
            Assert.That(language, Does.Contain("\"ControllersLocked\""));
            Assert.That(language, Does.Contain("\"ControllersUnlocked\""));
        }

        [Test]
        public void Instrument_Dropdown_Uses_Inline_Tiered_Labels()
        {
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);

            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.text, Does.Contain("chartName + \" - \""));
            Assert.That(menu.text, Does.Not.Contain("chartName\n                    + $\"\\n<size="));
        }

        [Test]
        public void Setup_Menu_Separates_Row_Selection_From_Editor_Focus()
        {
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            const string rowPath = "Assets/Script/Menu/Maestro/MaestroPlayerRow.cs";
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);
            var row = AssetDatabase.LoadAssetAtPath<MonoScript>(rowPath);

            Assert.That(menu, Is.Not.Null);
            Assert.That(row, Is.Not.Null);
            Assert.That(menu.text, Does.Contain("CanvasGroup"));
            Assert.That(menu.text, Does.Contain("OnRightNavigationSelectionChanged"));
            Assert.That(menu.text, Does.Contain("SelectionOrigin.Mouse"));
            Assert.That(menu.text, Does.Contain("ResetMaestroNavigationStack"));
            Assert.That(row.text, Does.Contain("OnPointerDown"));
            Assert.That(row.text, Does.Contain("_wasSelectedOnPointerDown"));
        }

        [Test]
        public void Setup_Menu_Uses_Readonly_Game_Mode_Tiered_Instruments_And_Party_Vocal_Charts()
        {
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            const string sessionPath = "Assets/Script/Menu/Maestro/MaestroSetupSession.cs";
            const string dropdownPath = "Assets/Prefabs/Menu/Common/DropdownSelection.prefab";
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);
            var session = AssetDatabase.LoadAssetAtPath<MonoScript>(sessionPath);
            var dropdownPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(dropdownPath);

            Assert.That(menu, Is.Not.Null);
            Assert.That(session, Is.Not.Null);
            Assert.That(dropdownPrefab, Is.Not.Null);
            Assert.That(menu.text, Does.Contain("GetTierLabel"));
            Assert.That(menu.text, Does.Contain("ToResourceName"));
            Assert.That(menu.text, Does.Contain("GetPartyVocalsChartLabel"));
            Assert.That(menu.text, Does.Not.Contain("_gameModeDropdown"));
            Assert.That(session.text, Does.Contain("GameMode.PartyVocals"));
            Assert.That(session.text, Does.Contain("Instrument.Vocals"));
            Assert.That(session.text, Does.Contain("Instrument.Harmony"));

            var dropdown = dropdownPrefab.GetComponentsInChildren<Component>(true)
                .Single(component => component.GetType().FullName == "TMPro.TMP_Dropdown");
            Assert.That(new SerializedObject(dropdown).FindProperty("m_ItemImage").objectReferenceValue,
                Is.Not.Null, "Instrument option images need an item image slot in the dropdown prefab.");
        }

        [Test]
        public void Setup_Menu_Uses_Asterisk_Bot_Marker_And_Real_Checkbox_Visuals()
        {
            const string rowPath = "Assets/Script/Menu/Maestro/MaestroPlayerRow.cs";
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            var row = AssetDatabase.LoadAssetAtPath<MonoScript>(rowPath);
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);

            Assert.That(row, Is.Not.Null);
            Assert.That(menu, Is.Not.Null);
            Assert.That(row.text, Does.Contain("*"));
            Assert.That(menu.text, Does.Contain("ModifierItem"));
            Assert.That(menu.text, Does.Not.Contain("☑"));
            Assert.That(menu.text, Does.Not.Contain("☐"));
        }

        [Test]
        public void Setup_Menu_Uses_Global_Confirm_Back_And_Clear_Modifier_Label()
        {
            const string prefabPath = "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab";
            const string scriptPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(script, Is.Not.Null);
            Assert.That(prefab.transform.Find("Footer"), Is.Null,
                "Maestro should use the global confirm/back help bar.");
            Assert.That(prefab.transform.Find("Body/SelectedPlayerEditor/ModifierButton/Label"),
                Is.Not.Null);
            var modifierLabel = prefab.transform
                .Find("Body/SelectedPlayerEditor/ModifierButton/Label")
                .GetComponents<Component>()
                .Single(component => component.GetType().FullName == "TMPro.TextMeshProUGUI");
            Assert.That(new SerializedObject(modifierLabel).FindProperty("m_text").stringValue,
                Is.EqualTo("Modifiers"));
            Assert.That(script.text, Does.Not.Contain("_backButton"));
            Assert.That(script.text, Does.Not.Contain("_continueButton"));
            Assert.That(script.text, Does.Not.Contain("_modifierText"));
        }

        [Test]
        public void Modifier_Picker_Replaces_Conflicting_Choice()
        {
            var rulesType = Type.GetType("YARG.Menu.Maestro.MaestroSelectionRules, Assembly-CSharp");
            Assert.That(rulesType, Is.Not.Null, "Maestro selection rules are missing.");
            var toggle = rulesType.GetMethod("ToggleModifier");
            Assert.That(toggle, Is.Not.Null, "Maestro modifier toggle rule is missing.");
            var modifierType = toggle.GetParameters()[0].ParameterType;

            var allStrums = Enum.Parse(modifierType, "AllStrums");
            var allHopos = Enum.Parse(modifierType, "AllHopos");
            var result = toggle.Invoke(null, new[] { allStrums, allHopos, true });
            ulong resultBits = Convert.ToUInt64(result);

            Assert.That(resultBits & Convert.ToUInt64(allHopos), Is.Not.Zero);
            Assert.That(resultBits & Convert.ToUInt64(allStrums), Is.Zero);
        }

        [Test]
        public void Difficulty_Normalization_Prefers_The_Nearest_Lower_Available_Value()
        {
            var rulesType = Type.GetType("YARG.Menu.Maestro.MaestroSelectionRules, Assembly-CSharp");
            var normalize = rulesType?.GetMethod("SelectDifficultyFallback");
            Assert.That(normalize, Is.Not.Null, "Maestro difficulty normalization rule is missing.");
            var difficultyType = normalize.GetParameters()[0].ParameterType;
            var available = Array.CreateInstance(difficultyType, 2);
            available.SetValue(Enum.Parse(difficultyType, "Easy"), 0);
            available.SetValue(Enum.Parse(difficultyType, "Hard"), 1);

            var result = normalize.Invoke(null, new[]
            {
                Enum.Parse(difficultyType, "Medium"),
                available,
            });

            Assert.That(result.ToString(), Is.EqualTo("Easy"));
        }

        [Test]
        public void Dropdown_Navigation_Removes_Only_Its_Own_Scheme()
        {
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);
            Assert.That(menu.text, Does.Contain("CloseDropdowns()"));

            var navigatorType = Type.GetType("YARG.Menu.Navigation.Navigator, Assembly-CSharp");
            var schemeType = Type.GetType("YARG.Menu.Navigation.NavigationScheme, Assembly-CSharp");
            Assert.That(navigatorType, Is.Not.Null);
            Assert.That(schemeType, Is.Not.Null);
            var remove = navigatorType.GetMethod("RemoveSchemeFromStack",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(remove, Is.Not.Null, "The navigation stack removal rule is missing.");

            var entryType = schemeType.GetNestedType("Entry");
            var entriesType = typeof(System.Collections.Generic.List<>).MakeGenericType(entryType);
            var constructor = schemeType.GetConstructors().Single(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 3 && parameters[2].ParameterType == typeof(Action);
            });
            object NewScheme(Action onPop = null) => constructor.Invoke(new[]
            {
                Activator.CreateInstance(entriesType),
                null,
                onPop,
            });

            int dropdownPops = 0;
            var page = NewScheme();
            var dropdown = NewScheme(() => dropdownPops++);
            var overlay = NewScheme();
            var stackType = typeof(System.Collections.Generic.Stack<>).MakeGenericType(schemeType);
            var stack = Activator.CreateInstance(stackType);
            var push = stackType.GetMethod("Push");
            push.Invoke(stack, new[] { page });
            push.Invoke(stack, new[] { dropdown });
            push.Invoke(stack, new[] { overlay });

            bool removed = (bool) remove.Invoke(null, new[] { stack, dropdown });
            var remaining = (Array) stackType.GetMethod("ToArray").Invoke(stack, null);

            Assert.That(removed, Is.True);
            Assert.That(dropdownPops, Is.EqualTo(1));
            Assert.That(remaining.Length, Is.EqualTo(2));
            Assert.That(remaining.GetValue(0), Is.SameAs(overlay));
            Assert.That(remaining.GetValue(1), Is.SameAs(page));
            Assert.That((bool) remove.Invoke(null, new[] { stack, dropdown }), Is.False);
            Assert.That(dropdownPops, Is.EqualTo(1));
        }

        private static Transform FindRequired(Transform root, string path)
        {
            var child = root.Find(path);
            Assert.That(child, Is.Not.Null, $"Could not find prefab object '{path}'.");
            return child;
        }
    }
}
