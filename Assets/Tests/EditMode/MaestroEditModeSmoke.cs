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
            var editorImage = FindRequired(prefab.transform,
                "Body/SelectedPlayerEditor").GetComponent<Image>();

            Assert.That(header.anchoredPosition.y, Is.LessThanOrEqualTo(-40f));
            Assert.That(body.sizeDelta.y, Is.LessThanOrEqualTo(-240f));
            Assert.That(listImage.color.r, Is.LessThanOrEqualTo(0.1f));
            Assert.That(listImage.color.a, Is.GreaterThanOrEqualTo(0.8f));
            Assert.That(listImage.sprite, Is.Not.Null,
                "The profile list should use the rounded panel sprite.");
            Assert.That(editorImage.sprite, Is.EqualTo(listImage.sprite),
                "The editor should use the same rounded panel surface as the profile list.");
            Assert.That(editorImage.color.a, Is.GreaterThanOrEqualTo(0.8f));
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
            // Icon is now the first column
            Assert.That(icon.anchorMin.x, Is.LessThan(name.anchorMin.x));
            Assert.That(name.anchorMin.x, Is.LessThan(setup.anchorMin.x));
            Assert.That(setup.anchorMin.x, Is.LessThan(modifiers.anchorMin.x));
            // Column width checks (AC.4): icon ~8%, name ~20%, setup ~29%, modifiers ~39%
            Assert.That(name.anchorMax.x - name.anchorMin.x, Is.EqualTo(0.20f).Within(0.03f));
            Assert.That(setup.anchorMax.x - setup.anchorMin.x, Is.EqualTo(0.29f).Within(0.03f));
            Assert.That(modifiers.anchorMax.x - modifiers.anchorMin.x, Is.GreaterThan(0.35f));
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
            Assert.That(serializedMenu.FindProperty("_accessibilityButton").objectReferenceValue,
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
            Assert.That(script.text, Does.Contain("ShowAdjustmentPicker"));
            Assert.That(script.text, Does.Contain("MenuAction.Blue"));
            Assert.That(script.text, Does.Not.Contain("Controller Navigation Disabled"));
            Assert.That(script.text, Does.Contain("FinishEditingPlayer"));
            Assert.That(script.text, Does.Contain("BeginEditingPlayer"));
        }

        [Test]
        public void Adjustment_Picker_Uses_A_Single_Done_Button()
        {
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);

            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.text, Does.Contain("ShowAdjustmentPicker"));
            Assert.That(menu.text, Does.Contain("Menu.DifficultySelect.Done"));
            Assert.That(menu.text, Does.Not.Contain("Menu.Common.Close"));
            Assert.That(menu.text, Does.Not.Contain("Menu.Common.Confirm"));
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
        public void Setup_Menu_Shows_Editor_And_Repositions_Play_Below_List()
        {
            const string prefabPath = "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var menu = prefab.GetComponents<Component>()
                .Single(component => component.GetType().FullName ==
                    "YARG.Menu.Maestro.MaestroSetupMenu");
            var serializedMenu = new SerializedObject(menu);

            Assert.That(prefab.transform.Find("Body/SelectedPlayerEditor").gameObject.activeSelf,
                Is.True, "The editor must remain visible while the left profile list has focus.");
            var play = prefab.transform.Find("Body/PlayButtonContainer/PlayButton");
            var playContainer = prefab.transform.Find("Body/PlayButtonContainer");
            Assert.That(play,
                Is.Not.Null, "Play must be a visible sibling of the profile list.");
            Assert.That(playContainer,
                Is.Not.Null, "Play must be inside its own centered layout wrapper.");
            Assert.That(prefab.transform.Find("Body/PlayerScroll/Viewport/PlayerContent/PlayButtonContainer"),
                Is.Null, "PlayerContent must not contain an authored Play placeholder.");
            Assert.That(playContainer.GetComponent<HorizontalLayoutGroup>(), Is.Not.Null,
                "Play needs a centered wrapper so its highlight does not touch the panel edges.");
            Assert.That(playContainer.GetComponent<LayoutElement>(), Is.Not.Null,
                "Play's wrapper needs an explicit row height in the scroll layout.");
            Assert.That(play.GetComponent<RectTransform>().sizeDelta.x,
                Is.LessThanOrEqualTo(300f), "Play should not inherit the full profile-panel width.");
            Assert.That(play.GetComponent<RectTransform>().sizeDelta.y,
                Is.LessThanOrEqualTo(72f), "Play must fit inside a profile row.");
            var playLabel = play.GetComponentsInChildren<Component>(true)
                .Single(component => component.GetType().FullName == "TMPro.TextMeshProUGUI");
            Assert.That(new SerializedObject(playLabel).FindProperty("m_text").stringValue,
                Is.EqualTo("Play Song"), "Play button must read 'Play Song'.");
            Assert.That(serializedMenu.FindProperty("_modifierButton").objectReferenceValue,
                Is.Not.Null);
            Assert.That(serializedMenu.FindProperty("_accessibilityButton").objectReferenceValue,
                Is.Not.Null);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(script.text, Does.Contain("_rightNavigationGroup?.ClearSelection()"));
            Assert.That(script.text, Does.Contain("rt.anchorMax = new Vector2(0.46f, 0f)"),
                "Play must use the profile list column width, not half of the full body.");
            Assert.That(script.text, Does.Contain("rt.anchoredPosition = new Vector2(20f, 10f)"),
                "Play must retain the profile panel's horizontal inset and bottom margin.");
            Assert.That(script.text, Does.Contain("rt.sizeDelta = new Vector2(-60f, 72f)"),
                "Play must be narrower than the profile panel so its focus ring has padding.");
            Assert.That(script.text, Does.Contain("scrollRectTransform.offsetMin"),
                "The profile list must reserve vertical space for Play and its focus ring.");
            int repositionIndex = script.text.IndexOf("RepositionPlayButton();",
                StringComparison.Ordinal);
            int playerLoopIndex = script.text.IndexOf(
                "foreach (var player in Session.Players)", StringComparison.Ordinal);
            Assert.That(repositionIndex, Is.GreaterThanOrEqualTo(0).And.LessThan(playerLoopIndex),
                "Play positioning must happen before dynamic rows are created.");
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
        public void Maestro_Direct_Summary_Setting_Is_Off_And_Visible_With_Maestro()
        {
            const string settingsPath = "Assets/Script/Settings/SettingsManager.Settings.cs";
            const string metadataPath = "Assets/Script/Settings/SettingsManager.cs";
            const string difficultyPath = "Assets/Script/Menu/DifficultySelect/DifficultySelectMenu.cs";
            const string languagePath = "Assets/StreamingAssets/lang/en-US.json";
            var settings = AssetDatabase.LoadAssetAtPath<MonoScript>(settingsPath);
            var metadata = AssetDatabase.LoadAssetAtPath<MonoScript>(metadataPath);
            var difficulty = AssetDatabase.LoadAssetAtPath<MonoScript>(difficultyPath);
            var language = File.ReadAllText(languagePath);

            Assert.That(settings, Is.Not.Null);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(difficulty, Is.Not.Null);
            Assert.That(settings.text, Does.Contain("MaestroGoDirectlyToSummary").And.Contain("new(false)"),
                "Direct-to-summary must default off.");
            Assert.That(metadata.text, Does.Contain("MaestroGoDirectlyToSummary"));
            Assert.That(metadata.text, Does.Contain("Settings.MaestroEnable.Value"));
            Assert.That(difficulty.text, Does.Contain("MaestroGoDirectlyToSummary.Value"));
            Assert.That(difficulty.text, Does.Contain("PlayerContainer.Players.Count"));
            Assert.That(language, Does.Contain("MaestroGoDirectlyToSummary"));
        }

        [Test]
        public void Maestro_Footer_Provides_Skip_Toggle_And_Show_Pin()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            var language = File.ReadAllText("Assets/StreamingAssets/lang/en-US.json");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("MenuAction.Orange"));
            Assert.That(script.text, Does.Contain("MenuAction.Yellow"));
            Assert.That(script.text, Does.Contain("SkipToMaestroOn"));
            Assert.That(script.text, Does.Contain("SkipToMaestroOff"));
            Assert.That(language, Does.Contain("Skip to Maestro (On)"));
            Assert.That(language, Does.Contain("Skip to Maestro (Off)"));
            Assert.That(script.text, Does.Contain("ShowMaestroPairingPin"));
            Assert.That(script.text, Does.Contain(
                "SettingsManager.Settings.MaestroGoDirectlyToSummary.Value"));
        }

        [Test]
        public void Maestro_Footer_Uses_Settings_Button_Localization_And_GRYBO_Order()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            var language = File.ReadAllText("Assets/StreamingAssets/lang/en-US.json");

            Assert.That(script, Is.Not.Null);
            Assert.That(language, Does.Contain("\"Button\":"));
            Assert.That(script.text, Does.Contain("Settings.Button.SkipToMaestroOn"));
            Assert.That(script.text, Does.Contain("Settings.Button.SkipToMaestroOff"));
            Assert.That(script.text, Does.Contain("Settings.Button.ShowMaestroPairingPin"));

            int red = script.text.IndexOf("MenuAction.Red", StringComparison.Ordinal);
            int yellow = script.text.IndexOf("MenuAction.Yellow", StringComparison.Ordinal);
            int blue = script.text.IndexOf("MenuAction.Blue", StringComparison.Ordinal);
            int orange = script.text.IndexOf("MenuAction.Orange", StringComparison.Ordinal);
            Assert.That(red, Is.GreaterThanOrEqualTo(0));
            Assert.That(red, Is.LessThan(yellow));
            Assert.That(yellow, Is.LessThan(blue));
            Assert.That(blue, Is.LessThan(orange));
        }

        [Test]
        public void Difficulty_Select_Initializes_Current_Player_Before_Direct_Maestro()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/DifficultySelect/DifficultySelectMenu.cs");
            Assert.That(script, Is.Not.Null);

            int initializeIndex = script.text.IndexOf("ChangePlayer(0);", StringComparison.Ordinal);
            int subscribeIndex = script.text.IndexOf(
                "_navGroup.SelectionChanged += UpdateForSelectionChanged;", StringComparison.Ordinal);
            int directIndex = script.text.IndexOf("OpenMaestroSummaryDirectly();", StringComparison.Ordinal);
            Assert.That(initializeIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(subscribeIndex, Is.GreaterThan(initializeIndex));
            Assert.That(directIndex, Is.GreaterThan(subscribeIndex),
                "Direct Maestro must be opened only after Difficulty Select has initialized its view and navigation.");

            int conditionIndex = script.text.IndexOf(
                "MaestroGoDirectlyToSummary.Value", StringComparison.Ordinal);
            string directBlock = script.text.Substring(conditionIndex,
                directIndex - conditionIndex + "OpenMaestroSummaryDirectly();".Length);
            Assert.That(directBlock, Does.Not.Contain("return;"),
                "An early return leaves the stale/default Difficulty Select UI behind Maestro.");
        }

        [Test]
        public void Difficulty_Select_Defers_Direct_Maestro_Until_Menu_Push_Completes()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/DifficultySelect/DifficultySelectMenu.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("StartCoroutine(OpenMaestroSummaryDirectlyNextFrame())"));
            Assert.That(script.text, Does.Contain("yield return null"));
            Assert.That(script.text, Does.Contain("StopCoroutine(_directMaestroCoroutine)"));
            int onEnableIndex = script.text.IndexOf("private void OnEnable", StringComparison.Ordinal);
            int helperIndex = script.text.IndexOf(
                "private System.Collections.IEnumerator OpenMaestroSummaryDirectlyNextFrame",
                StringComparison.Ordinal);
            string onEnable = script.text.Substring(onEnableIndex, helperIndex - onEnableIndex);
            Assert.That(onEnable, Does.Not.Contain("OpenMaestroSummaryDirectly();"));
            Assert.That(script.text, Does.Contain("OpenMaestroSummaryDirectlyNextFrame"),
                "Direct Maestro must not be pushed synchronously from Difficulty Select OnEnable.");
        }

        [Test]
        public void Navigator_Hold_Release_Survives_Transition_Callbacks()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Navigation/Navigator.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("var hold = _holdInputs[i]"));
            Assert.That(script.text, Does.Contain("_holdInputs.Remove(hold)"));
            Assert.That(script.text, Does.Not.Contain("_holdInputs[i].Tracker.ClearEvents()"),
                "Hold cleanup must not index the list after StopHolding callbacks can change it.");
        }

        [Test]
        public void Direct_Maestro_Hides_Difficulty_Player_Content_During_Transition()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/DifficultySelect/DifficultySelectMenu.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("PrepareForDirectMaestroSummary"));
            Assert.That(script.text, Does.Contain("_container.gameObject.SetActive"));
            Assert.That(script.text, Does.Contain("directSummary"));
        }

        [Test]
        public void Maestro_Reuses_Header_And_Returns_To_Song_Select()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            var manager = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/MenuManager.cs");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab");

            Assert.That(script, Is.Not.Null);
            Assert.That(manager, Is.Not.Null);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(script.text, Does.Contain("Player Settings Summary"));
            Assert.That(script.text, Does.Contain("BackToSongSelect"));
            Assert.That(script.text, Does.Contain("PopToMenu(MenuManager.Menu.MusicLibrary)"));
            Assert.That(script.text, Does.Contain("FindHeaderBackButton"));
            Assert.That(script.text, Does.Contain("ConfigureHeaderSourceIcon"));
            Assert.That(script.text, Does.Contain("SongSources.SourceToIcon"));
            Assert.That(script.text, Does.Contain("_controllerLockText.gameObject.SetActive(false)"));
            Assert.That(manager.text, Does.Contain("PopToMenu(Menu menu)"));
            Assert.That(prefab.transform.Find("Header"), Is.Not.Null);
            string prefabText = File.ReadAllText(
                "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab");
            Assert.That(prefabText, Does.Contain("value: SharedHeader"),
                "Maestro must contain the shared Header prefab instance.");
            Assert.That(prefabText, Does.Contain(
                "guid: fbe721481a76d3340871db2a026bbbcb"));
            var hierarchyNames = prefab.GetComponentsInChildren<Transform>(true)
                .Select(transform => transform.name)
                .ToArray();
            Assert.That(hierarchyNames, Does.Contain("SharedHeader"));
            Assert.That(hierarchyNames, Does.Contain("Single Header Text"));
            Assert.That(hierarchyNames, Does.Contain("Button"));
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
            Assert.That(menu.text, Does.Contain("_playButton?.SetSelected(false"));
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
            const string modifierLabelPath =
                "Body/SelectedPlayerEditor/AdjustmentButtonsRow/ModifierButton/Label";
            Assert.That(prefab.transform.Find(modifierLabelPath),
                Is.Not.Null);
            var modifierLabel = prefab.transform
                .Find(modifierLabelPath)
                .GetComponents<Component>()
                .Single(component => component.GetType().FullName == "TMPro.TextMeshProUGUI");
            Assert.That(new SerializedObject(modifierLabel).FindProperty("m_text").stringValue,
                Is.EqualTo("Modifiers"));
            Assert.That(script.text, Does.Not.Contain("_backButton"));
            Assert.That(script.text, Does.Not.Contain("_continueButton"));
            Assert.That(script.text, Does.Not.Contain("_modifierText"));
        }

        [Test]
        public void Player_Row_Uses_Localized_Instrument_And_Difficulty_Summary()
        {
            const string rowPath = "Assets/Script/Menu/Maestro/MaestroPlayerRow.cs";
            var row = AssetDatabase.LoadAssetAtPath<MonoScript>(rowPath);

            Assert.That(row, Is.Not.Null);
            Assert.That(row.text, Does.Contain("player.Instrument.ToLocalizedName()"));
            Assert.That(row.text, Does.Contain("player.Difficulty.ToLocalizedName()"));
            Assert.That(row.text, Does.Not.Contain("player.GameMode} · {player.Instrument}"));
        }

        [Test]
        public void Maestro_Stages_Accessibility_Settings_With_Active_Summaries()
        {
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            const string sessionPath = "Assets/Script/Menu/Maestro/MaestroSetupSession.cs";
            const string rowPath = "Assets/Script/Menu/Maestro/MaestroPlayerRow.cs";
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);
            var session = AssetDatabase.LoadAssetAtPath<MonoScript>(sessionPath);
            var row = AssetDatabase.LoadAssetAtPath<MonoScript>(rowPath);

            Assert.That(menu, Is.Not.Null);
            Assert.That(session, Is.Not.Null);
            Assert.That(row, Is.Not.Null);
            Assert.That(menu.text, Does.Contain("StageLeftyFlip"));
            Assert.That(menu.text, Does.Contain("StageRangeEnabled"));
            Assert.That(menu.text, Does.Contain("StageOpenLaneDisplayType"));
            Assert.That(session.text, Does.Contain("LeftyFlip"));
            Assert.That(session.text, Does.Contain("RangeEnabled"));
            Assert.That(session.text, Does.Contain("OpenLaneDisplayType"));
            Assert.That(row.text, Does.Contain("HasNoRangeShifts"));
        }

        [Test]
        public void Vocal_Adjustments_Keep_Unpitched_Parts_Adjacent()
        {
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);

            Assert.That(menu, Is.Not.Null);
            int part1 = menu.text.IndexOf("Modifier.UnpitchedOnly", StringComparison.Ordinal);
            int part2 = menu.text.IndexOf("Modifier.UnpitchedHarm2", StringComparison.Ordinal);
            int part3 = menu.text.IndexOf("Modifier.UnpitchedHarm3", StringComparison.Ordinal);
            Assert.That(part1, Is.GreaterThanOrEqualTo(0));
            Assert.That(part2, Is.GreaterThan(part1));
            Assert.That(part3, Is.GreaterThan(part2));
            Assert.That(menu.text, Does.Contain("GetVocalModifierOptions"));
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

        [Test]
        public void Player_Row_Hover_Select_Is_Enabled()
        {
            const string path = "Assets/Prefabs/Menu/Maestro/MaestroPlayerRow.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var row = prefab.GetComponents<Component>()
                .Single(c => c.GetType().FullName == "YARG.Menu.Maestro.MaestroPlayerRow");
            Assert.That(new SerializedObject(row).FindProperty("_selectOnHover").boolValue,
                Is.True, "Player rows must select on mouse hover (AC.6).");
        }

        [Test]
        public void Player_Row_Shows_Tier_Label_On_Second_Line()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroPlayerRow.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("\\n<size="),
                "Row setup text must support a second-line tier label (AC.1).");
            Assert.That(script.text, Does.Contain("tierLabel"),
                "Refresh must accept a tierLabel parameter.");
        }

        [Test]
        public void Setup_Menu_Dims_NonSelected_Rows_When_Editing()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("SetEditorDimmed"),
                "Menu must call per-row dimming when editor is focused (AC.7).");
            Assert.That(script.text, Does.Contain("SetEditorDimmed(editorFocused && isNotSelected)"),
                "Only non-selected rows should dim while the editor has focus.");
            var rowScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroPlayerRow.cs");
            Assert.That(rowScript.text, Does.Contain("0.2f"),
                "Non-selected rows must dim to 20% alpha.");
        }

        [Test]
        public void Player_Row_Dims_Text_And_Icon_Without_Dimming_Background()
        {
            const string path = "Assets/Prefabs/Menu/Maestro/MaestroPlayerRow.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);

            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var row = instance.GetComponents<Component>()
                    .Single(component => component.GetType().FullName ==
                        "YARG.Menu.Maestro.MaestroPlayerRow");
                var setup = instance.transform.Find("Setup").GetComponent<Graphic>();
                var icon = instance.transform.Find("InstrumentIcon").GetComponent<Graphic>();
                var background = instance.transform.Find("SelectedBackground").GetComponent<Graphic>();
                var setupColor = setup.color;
                var iconColor = icon.color;
                var backgroundColor = background.color;

                row.GetType().GetMethod("SetEditorDimmed")?.Invoke(row, new object[] { true });

                Assert.That(setup.color.a, Is.EqualTo(setupColor.a * 0.2f).Within(0.01f));
                Assert.That(icon.color.a, Is.EqualTo(iconColor.a * 0.2f).Within(0.01f));
                Assert.That(background.color, Is.EqualTo(backgroundColor),
                    "The row background should remain unchanged while its contents dim.");

                row.GetType().GetMethod("SetEditorDimmed")?.Invoke(row, new object[] { false });
                Assert.That(setup.color, Is.EqualTo(setupColor));
                Assert.That(icon.color, Is.EqualTo(iconColor));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Setup_Menu_Dims_Editor_Content_When_Profile_List_Is_Focused()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text,
                Does.Contain("SetSelectedEditorContentAlpha(editorFocused ? 1f : UnfocusedEditorContentAlpha)"),
                "Editor content must retain 50% readability when the profile list is focused.");
        }

        [Test]
        public void Setup_Menu_Caches_Editor_Alpha_Before_Reentry_Dimming()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("private void Awake()"),
                "Editor graphic alpha must be captured before OnEnable can dim the page.");
            Assert.That(script.text, Does.Contain("CaptureSelectedEditorGraphicAlphas();"));
            Assert.That(script.text, Does.Not.Contain("_selectedEditorGraphicAlphas.Clear();"),
                "Re-entering Maestro must not discard the authored alpha cache.");
        }

        [Test]
        public void Setup_Menu_Dims_Play_Content_When_Editor_Is_Focused()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("_playButtonCanvasGroup"),
                "Play must be treated as a left-column navigation item.");
            Assert.That(script.text,
                Does.Contain("_playButtonCanvasGroup.alpha = editorFocused ? 0.2f : 1f"),
                "Play content must dim with the profile list while the editor is focused.");
            Assert.That(script.text, Does.Contain("SetLeftNavigationHoverSelection(_playButton)"),
                "Play must select on mouse hover like the profile rows.");
        }

        [Test]
        public void Setup_Menu_Uses_Focused_And_Unfocused_Pane_Surface_Opacity()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("_playerPanelBackground"));
            Assert.That(script.text, Does.Contain("_selectedPlayerBackground"));
            Assert.That(script.text, Does.Contain("FocusedPaneBackgroundAlpha = 0.9f"));
            Assert.That(script.text, Does.Contain("UnfocusedPaneBackgroundAlpha = 0.2f"));
            Assert.That(script.text, Does.Contain("UnfocusedEditorBackgroundAlpha = 0.5f"));
            Assert.That(script.text, Does.Contain("UnfocusedEditorContentAlpha = 0.5f"));
        }

        [Test]
        public void Setup_Menu_Uses_Last_Right_Focus_And_Pane_Background_Hover()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            var hover = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroPaneHoverTarget.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(hover, Is.Not.Null);
            Assert.That(script.text, Does.Contain("_lastRightSelectionIndex"));
            Assert.That(script.text, Does.Contain("ConfigurePaneHoverTargets"));
            Assert.That(script.text, Does.Contain("FocusProfileList"));
            Assert.That(script.text, Does.Contain("FocusProfileEditor"));
            Assert.That(script.text, Does.Contain("_playButton?.SetSelected(false"));
            Assert.That(hover.text, Does.Contain("IPointerEnterHandler"));
        }

        [Test]
        public void Setup_Menu_Pads_Editor_And_Enables_Right_Side_Hover_Focus()
        {
            const string prefabPath = "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab";
            const string menuPath = "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs";
            const string navigationPath = "Assets/Script/Menu/Navigation/NavigatableBehaviour.cs";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(menuPath);
            var navigation = AssetDatabase.LoadAssetAtPath<MonoScript>(navigationPath);
            var editor = FindRequired(prefab.transform, "Body/SelectedPlayerEditor");
            var layout = editor.GetComponent<VerticalLayoutGroup>();

            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.padding.left, Is.EqualTo(20));
            Assert.That(layout.padding.right, Is.EqualTo(20));
            Assert.That(layout.padding.top, Is.EqualTo(20));
            Assert.That(layout.padding.bottom, Is.EqualTo(20));
            Assert.That(menu.text, Does.Contain("SetSelectOnHover(true)"),
                "Right-side controls must select when the pointer hovers them.");
            Assert.That(navigation.text, Does.Contain("SetSelectOnHover"),
                "NavigatableBehaviour needs a runtime hover-selection setter.");
        }

        [Test]
        public void Setup_Menu_Updates_Editor_Focus_Visuals_For_All_Right_Selections()
        {
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(menu, Is.Not.Null);
            int handler = menu.text.IndexOf("private void OnRightNavigationSelectionChanged",
                StringComparison.Ordinal);
            Assert.That(handler, Is.GreaterThanOrEqualTo(0));
            var handlerText = menu.text.Substring(handler);
            Assert.That(handlerText, Does.Contain("UpdateFocusVisual()"),
                "Moving between right-side controls must immediately restore the editor alpha state.");
            Assert.That(handlerText, Does.Not.Contain(
                "selectionOrigin != SelectionOrigin.Mouse || _editingPlayer"),
                "Right-side focus must not ignore keyboard or already-editing selection changes.");
        }

        [Test]
        public void Adjustment_Picker_Defaults_To_Done()
        {
            var menu = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            var dialog = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Common/Dialogs/Dialog.cs");
            Assert.That(menu, Is.Not.Null);
            Assert.That(dialog, Is.Not.Null);
            Assert.That(menu.text, Does.Contain("dialog.SelectLast();"),
                "Done must be the initial selection in adjustment dialogs.");
            Assert.That(dialog.text, Does.Contain("public void SelectLast()"));
        }

        [Test]
        public void Setup_Menu_Resets_Profile_Content_To_The_Top()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("contentRect.anchoredPosition = Vector2.zero"));
            Assert.That(script.text, Does.Contain("verticalNormalizedPosition = 1f"));
        }

        [Test]
        public void Dropdown_Navigatable_Has_Focus_Border_Field()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroDropdownNavigatable.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("_focusBorder"),
                "Dropdown navigatable must have a focus border field (AC.9).");
            Assert.That(script.text, Does.Contain("SpriteHelper.GetRoundedRect"),
                "Focus border must use SpriteHelper for procedural rounded-rect sprite.");
        }

        [Test]
        public void Adjustment_Buttons_Are_Side_By_Side()
        {
            const string path = "Assets/Prefabs/Menu/Maestro/MaestroSetupMenu.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var row = prefab.transform.Find("Body/SelectedPlayerEditor/AdjustmentButtonsRow");
            Assert.That(row, Is.Not.Null,
                "Both buttons must be in a shared horizontal container (AC.5).");
            Assert.That(row.GetComponent<HorizontalLayoutGroup>(), Is.Not.Null,
                "The button container must use a HorizontalLayoutGroup.");
            Assert.That(row.Find("ModifierButton"), Is.Not.Null);
            Assert.That(row.Find("AccessibilityButton"), Is.Not.Null);
        }

        [Test]
        public void Adjustment_Buttons_Use_Blue_Not_Green()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupMenu.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text,
                Does.Contain("MenuData.Colors.BrightButton"),
                "Modifier and Accessibility buttons must pass BrightButton blue " +
                "to ConfigureButton (AC.3).");
            Assert.That(script.text, Does.Contain("button.transform.parent"),
                "Recoloring must start at the RoundButton root so it reaches the background and ring.");
            Assert.That(script.text, Does.Contain("img.color = backgroundColor.Value"),
                "The background and selection ring must both use the requested blue color.");
        }

        [Test]
        public void GameMode_PartyVocals_Maps_To_HarmVocals()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Helpers/Extensions/GameModeExtensions.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("PartyVocals    => \"harmVocals\""),
                "Party Vocals must map to the harmony icon, not solo vocals (AC.3).");
        }

        [Test]
        public void Dropdown_Click_Forwarder_Calls_Confirm_Directly()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroDropdownNavigatable.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Not.Contain("UniTask.NextFrame"),
                "OnPointerClick must call Confirm() directly without a UniTask delay (AC.1).");
        }

        [Test]
        public void Dropdown_Border_Has_Hide_When_Open_Logic()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroDropdownNavigatable.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("new Vector2(-6, 12)"),
                "Focus border must be slightly wider than the dropdown (AC.2).");
            Assert.That(script.text, Does.Contain("GetRoundedRect(18, 2)"),
                "Focus border corner radius must track the dropdown's larger rounded corners.");
        }

        [Test]
        public void PartyVocals_Stage_Maps_To_Harmony_When_HarmonyIndex_Set()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Maestro/MaestroSetupSession.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text,
                Does.Contain("GameMode == GameMode.PartyVocals && Instrument == Instrument.PartyVocals"),
                "Constructor must map PartyVocals to Harmony/Vocals (AC.7).");
            Assert.That(script.text,
                Does.Contain("if (staged.GameMode == GameMode.PartyVocals)"),
                "TryCommit must restore PartyVocals instrument on commit (AC.7).");
        }

        [Test]
        public void Dialog_Base_Has_Red_Cancel_Entry()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Script/Menu/Common/Dialogs/Dialog.cs");
            Assert.That(script, Is.Not.Null);
            Assert.That(script.text, Does.Contain("MenuAction.Red"),
                "Base Dialog must have a Red/Cancel entry so all dialogs " +
                "(including MessageDialog popups) can be closed with Back/Esc (AC.5).");
        }

        private static Transform FindRequired(Transform root, string path)
        {
            var child = root.Find(path);
            Assert.That(child, Is.Not.Null, $"Could not find prefab object '{path}'.");
            return child;
        }
    }
}
