using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YARG.Menu.HighwayConfiguration;
using YARG.Menu.Navigation;

namespace YARG.Editor
{
    /// <summary>Builds the layout editor asset without modifying loaded scenes.</summary>
    public static class EliteDrumsLayoutMenuPrefabBuilder
    {
        public const string PREFAB_PATH = "Assets/Resources/EliteDrumsLayoutMenu.prefab";
        internal const string PROFILE_CONFIGURE_LABEL_KEY = "Menu.ProfileList.ConfigureEliteDrumsLayout";
        private const float GRID_SHIFT = 72f / 1080f;
        internal const float UPPER_ROW_BOTTOM = 0.50f + GRID_SHIFT;
        internal const float UPPER_ROW_TOP = 0.59f + GRID_SHIFT;
        internal const float LOWER_ROW_TOP = 0.49f + GRID_SHIFT;
        internal const float LOWER_ROW_BOTTOM = 0.40f + GRID_SHIFT;
        // The swatch sizeDelta removes 0.6 units from each edge; compensate at the top
        // so both visible gaps, rather than just the anchor gaps, are 10.8 units.
        internal const float FRET_SWATCH_BOTTOM = 0.31722223f + GRID_SHIFT;
        internal const float FRET_SWATCH_TOP = 0.39055556f + GRID_SHIFT;
        private const string BUTTON_PATH = "Assets/Prefabs/Menu/Common/Buttons/SmallRoundButton.prefab";
        private const string MANAGEMENT_BUTTON_PATH = "Assets/Prefabs/Menu/Common/Buttons/RoundButton.prefab";
        private const string DIALOG_BACKGROUND_PATH = "Assets/Art/Menu/Common/DialogBackground.png";
        private const string DIALOG_HEADER_PATH = "Assets/Art/Menu/Common/DialogHeader.png";
        private const string ICON_BUTTON_PATH = "Assets/Prefabs/Menu/Common/Buttons/IconButton.prefab";
        private const string HEADER_PATH = "Assets/Prefabs/Menu/Common/Headers/Header.prefab";
        private const string ROUNDED_RECT_PATH = "Assets/Art/Menu/Common/RoundedSquare.png";
        private const string CIRCLE_PATH = "Assets/Art/Menu/Common/Circle.png";
        private const float MANAGEMENT_BUTTON_HEIGHT = 56f;
        private const float TOOLBAR_ROW_HEIGHT = 97.5f;
        private const float MANAGEMENT_ROW_HEIGHT = 97.5f;
        private const float ASSIGNMENT_LEFT = 0.0225f;
        private const float DIVIDER_THICKNESS = 1.5f;

        [MenuItem("YARG/Build Elite Drums Layout Menu")]
        public static void Build()
        {
            var root = Rect("EliteDrumsLayoutMenu", null, Vector2.zero);
            Stretch(root.GetComponent<RectTransform>());
            root.SetActive(false);
            try
            {
                // SettingsMenu's MenuBackground1 artwork is uniformly RGB 5/6/11.
                root.AddComponent<Image>().color = new Color(5f / 255f, 6f / 255f, 11f / 255f, 1f);
                var menu = root.AddComponent<EliteDrumsLayoutMenu>();
                var navigation = root.AddComponent<NavigationGroup>();
                root.AddComponent<YARG.Menu.MenuObject>().Initialize(
                    YARG.Menu.MenuManager.Menu.EliteDrumsLayout);

                var headerTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(HEADER_PATH);
                if (headerTemplate == null) throw new System.InvalidOperationException("Missing production header prefab.");
                var header = (GameObject)PrefabUtility.InstantiatePrefab(headerTemplate);
                header.transform.SetParent(root.transform, false);
                header.name = "Header";
                var title = header.transform.Find("Single Header Text")?.GetComponent<TMP_Text>();
                var back = header.transform.Find("Button")?.GetComponent<Button>();
                if (title == null || back == null)
                    throw new System.InvalidOperationException("Production header is missing its title or back button.");
                title.text = "Elite Drums Highway";
                // The shared header button may have a serialized callback for its original menu.
                // Clear persistent and runtime listeners so only the layout menu's runtime binding remains.
                var backData = new SerializedObject(back);
                backData.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").arraySize = 0;
                backData.ApplyModifiedPropertiesWithoutUndo();
                back.onClick.RemoveAllListeners();

                var headerRect = header.GetComponent<RectTransform>();
                float headerBottom = 1080f * headerRect.anchorMin.y + headerRect.offsetMin.y;
                var previewControls = AnchoredRect("Toolbar Panel", root.transform,
                    new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero);
                var previewControlsRect = previewControls.GetComponent<RectTransform>();
                previewControlsRect.offsetMin = new Vector2(0f, headerBottom - 1080f - TOOLBAR_ROW_HEIGHT);
                previewControlsRect.offsetMax = new Vector2(0f, headerBottom - 1080f);
                var toolbarBackground = previewControls.AddComponent<Image>();
                // Match the SettingsMenu header fill above the near-black preset background.
                toolbarBackground.color = new Color(3f / 255f, 20f / 255f, 33f / 255f, 1f);
                toolbarBackground.raycastTarget = false;
                float toolbarBottom = headerBottom - TOOLBAR_ROW_HEIGHT;
                var managementRow = AnchoredRect("Management Row", root.transform,
                    new Vector2(0f, 1f), new Vector2(0.65f, 1f),
                    new Vector2(0f, toolbarBottom - 1080f - MANAGEMENT_ROW_HEIGHT),
                    new Vector2(0f, toolbarBottom - 1080f));
                AddBoundary("Toolbar Bottom Border", previewControls.transform,
                    Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, DIVIDER_THICKNESS));
                AddBoundary("Management Bottom Border", managementRow.transform,
                    Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, DIVIDER_THICKNESS));
                AddBoundary("Assignment Preview Divider", root.transform,
                    new Vector2(0.65f, 0f), new Vector2(0.65f, 0f),
                    new Vector2(-DIVIDER_THICKNESS / 2f, ReadHelpBarTop()), new Vector2(DIVIDER_THICKNESS / 2f, toolbarBottom));
                AddBoundary("Layout Colors Separator", previewControls.transform,
                    new Vector2(0.411f, 0.5f), new Vector2(0.411f, 0.5f),
                    new Vector2(-DIVIDER_THICKNESS / 2f, -40f), new Vector2(DIVIDER_THICKNESS / 2f, 40f));
                var dropdownTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Menu/Settings/Presets/PresetDropdown.prefab");
                var colorObject = (GameObject)PrefabUtility.InstantiatePrefab(dropdownTemplate);
                colorObject.name = "Preview Color Profile";
                colorObject.transform.SetParent(previewControls.transform, false);
                ConfigureDropdownRow(colorObject, new Vector2(0.42f, 0.1f), new Vector2(0.73f, 0.9f));
                Object.DestroyImmediate(colorObject.GetComponent<YARG.Menu.Settings.PresetDropdown>());
                var previewColor = ConfigureFullRowDropdown(colorObject);
                previewColor.GetComponent<RectTransform>().anchorMin = new Vector2(0.27f, 0.5f);
                ConfigureDropdownText(previewColor);
                ConfigureSelectedItemGutter(previewColor);
                var colorLabel = AddSectionLabel(colorObject.transform, "Preview Color Profile Label", "COLORS",
                    Vector2.zero, new Vector2(0.25f, 1f));
                ConfigureDropdownTextElement(previewColor.captionText);
                ConfigureSectionLabel(colorLabel);
                var optionsObject = (GameObject)PrefabUtility.InstantiatePrefab(dropdownTemplate);
                optionsObject.name = "Preview Options";
                optionsObject.transform.SetParent(previewControls.transform, false);
                ConfigureDropdownRow(optionsObject, new Vector2(0.75f, 0.1f), new Vector2(0.98f, 0.9f));
                Object.DestroyImmediate(optionsObject.GetComponent<YARG.Menu.Settings.PresetDropdown>());
                var previewOptions = ConfigureFullRowDropdown(optionsObject);
                ConfigureDropdownText(previewOptions);

                var layoutObject = (GameObject)PrefabUtility.InstantiatePrefab(dropdownTemplate);
                layoutObject.name = "Layout Selection";
                layoutObject.transform.SetParent(previewControls.transform, false);
                Object.DestroyImmediate(layoutObject.GetComponent<YARG.Menu.Settings.PresetDropdown>());
                ConfigureDropdownRow(layoutObject, new Vector2(0.02f, 0.1f), new Vector2(0.355f, 0.9f));
                var layoutDropdown = ConfigureFullRowDropdown(layoutObject, true);
                ConfigureDropdownText(layoutDropdown);
                ConfigureSelectedItemGutter(layoutDropdown);
                // Unlike the wider color/options lists, Layout follows its closed control exactly.
                layoutDropdown.template.anchorMin = Vector2.zero;
                layoutDropdown.template.anchorMax = new Vector2(1f, 0f);
                layoutDropdown.template.sizeDelta = new Vector2(0f, 300f);
                layoutDropdown.GetComponent<RectTransform>().anchorMin = new Vector2(0.25f, 0.5f);
                var layoutLabel = ConfigureLayoutLabel(layoutObject);
                colorLabel.font = layoutLabel.font;
                colorLabel.fontSize = layoutLabel.fontSize;
                colorLabel.fontStyle = layoutLabel.fontStyle;
                colorLabel.fontWeight = layoutLabel.fontWeight;
                colorLabel.enableAutoSizing = false;
                var folderObject = InstantiateIconButton(previewControls.transform, "Open Layout Folder",
                    "Assets/Art/Menu/Common/Icons/AssortedIcons.png", "UIIcons_4");
                var folderButton = folderObject.GetComponentInChildren<Button>(true);
                SetButtonRect(folderObject.GetComponent<RectTransform>(), new Vector2(0.38f, 0.5f), new Vector2(76, 76));
                var folderData = new SerializedObject(folderButton);
                folderData.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").arraySize = 0;
                folderData.ApplyModifiedPropertiesWithoutUndo();
                folderButton.GetComponent<Image>().color = new Color(0.18039216f, 0.8509804f, 1f);
                folderObject.transform.Find("Icon").GetComponent<Image>().color = Color.black;

                var cells = new EliteDrumsLayoutCell[20];
                for (int row = 0; row < 2; row++)
                {
                    for (int column = 0; column < 10; column++)
                    {
                        float xMin = ASSIGNMENT_LEFT + column * 0.061f;
                        var cellObject = AnchoredRect($"Column {column + 1} {(row == 0 ? "Cymbal" : "Drum")}",
                            root.transform, new Vector2(xMin, row == 0 ? UPPER_ROW_BOTTOM : LOWER_ROW_BOTTOM),
                            new Vector2(xMin + 0.056f, row == 0 ? UPPER_ROW_TOP : LOWER_ROW_TOP), Vector2.zero, Vector2.zero);
                        var background = cellObject.AddComponent<Image>();
                        background.color = new Color(0.055f, 0.07f, 0.09f, 1f);
                        background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ROUNDED_RECT_PATH);
                        background.type = Image.Type.Sliced;
                        var button = cellObject.AddComponent<Button>();
                        button.transition = Selectable.Transition.None;
                        var outline = cellObject.AddComponent<Outline>();
                        outline.effectDistance = new Vector2(2, -2);
                        outline.effectColor = new Color(0.32f, 0.36f, 0.4f);
                        var fillObject = Rect("Validity Fill", cellObject.transform, Vector2.zero);
                        Stretch(fillObject.GetComponent<RectTransform>(), 3f);
                        var fill = fillObject.AddComponent<Image>();
                        fill.sprite = background.sprite;
                        fill.type = Image.Type.Sliced;
                        fill.color = background.color;
                        fill.raycastTarget = false;
                        var label = Text("Label", cellObject.transform, "Empty", Vector2.zero,
                            new Vector2(96, 62), 19, true);
                        Stretch(label.rectTransform, 4);
                        label.textWrappingMode = TextWrappingModes.NoWrap;
                        label.overflowMode = TextOverflowModes.Ellipsis;
                        var buttonOutline = Rect("Selected Background", cellObject.transform, Vector2.zero);
                        Stretch(buttonOutline.GetComponent<RectTransform>(), 3);
                        var selectedImage = buttonOutline.AddComponent<Image>();
                        selectedImage.sprite = background.sprite;
                        selectedImage.type = Image.Type.Sliced;
                        buttonOutline.transform.SetSiblingIndex(1);
                        selectedImage.color = new Color(0.035f, 0.87f, 0.48f, 0.24f);
                        selectedImage.raycastTarget = false;
                        buttonOutline.SetActive(false);
                        GameObject pill = null;
                        if (row == 1)
                        {
                            pill = AnchoredRect($"Saved Slot Fret {column + 1}", root.transform,
                                new Vector2(xMin + 0.003f, FRET_SWATCH_BOTTOM),
                                new Vector2(xMin + 0.053f, FRET_SWATCH_TOP), Vector2.zero, Vector2.zero);
                            var pillRect = pill.GetComponent<RectTransform>();
                            pillRect.sizeDelta = new Vector2(0f, -1.2f);
                            var pillImage = pill.AddComponent<Image>();
                            pillImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ROUNDED_RECT_PATH);
                            pillImage.type = Image.Type.Sliced;
                            pillImage.color = Color.black;
                            pillImage.raycastTarget = false;
                            var inset = Rect("Fret Number Background", pill.transform, Vector2.zero);
                            var insetRect = inset.GetComponent<RectTransform>();
                            insetRect.anchorMin = insetRect.anchorMax = new Vector2(0.5f, 0.5f);
                            insetRect.sizeDelta = new Vector2(38f, 38f);
                            var insetImage = inset.AddComponent<Image>();
                            insetImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CIRCLE_PATH);
                            insetImage.preserveAspect = true;
                            insetImage.color = Color.black;
                            insetImage.raycastTarget = false;
                            var fretNumber = Text("Fret Number", inset.transform, FretNumberLabel(column),
                                Vector2.zero, Vector2.zero, 25, true);
                            Stretch(fretNumber.rectTransform, 0);
                            fretNumber.rectTransform.anchoredPosition += new Vector2(0f, 1f);
                            fretNumber.color = Color.white;
                            var numberOutline = fretNumber.gameObject.AddComponent<Outline>();
                            numberOutline.effectColor = new Color(0.015f, 0.02f, 0.03f, 1f);
                            numberOutline.effectDistance = new Vector2(1.25f, -1.25f);
                        }
                        var cell = cellObject.AddComponent<EliteDrumsLayoutCell>();
                        Set(cell, "_label", label);
                        Set(cell, "_button", button);
                        Set(cell, "_outline", outline);
                        Set(cell, "_fill", fill);
                        if (pill != null) Set(cell, "_fretPill", pill.GetComponent<Image>());
                        Set(cell, "_focusVisual", buttonOutline);
                        Set(cell, "_selectedVisual", buttonOutline);
                        cells[row * 10 + column] = cell;
                    }
                }

                var status = Text("Status", root.transform, "", Vector2.zero, new Vector2(900, 76), 30);
                status.rectTransform.anchorMin = new Vector2(0.04f, 0.08f);
                status.rectTransform.anchorMax = new Vector2(0.63f, 0.14f);
                status.rectTransform.offsetMin = status.rectTransform.offsetMax = Vector2.zero;
                status.alignment = TextAlignmentOptions.Left;
                var notice = Text("Colors Notice", root.transform, "", Vector2.zero,
                    new Vector2(900, 80), 27);
                notice.rectTransform.anchorMin = new Vector2(0.04f, 0.015f);
                notice.rectTransform.anchorMax = new Vector2(0.63f, 0.07f);
                notice.rectTransform.offsetMin = notice.rectTransform.offsetMax = Vector2.zero;
                notice.alignment = TextAlignmentOptions.Left;

                // Match TrackPreviewUI's authored height; only the horizontal field of view is narrower.
                float previewBottom = ReadHelpBarTop() / 1080f;
                const float PREVIEW_HEIGHT = 711f / 1080f;
                var previewRect = AnchoredRect("Layout Preview", root.transform,
                    new Vector2(0.66f, previewBottom),
                    new Vector2(0.99f, previewBottom + PREVIEW_HEIGHT), Vector2.zero, Vector2.zero);
                var previewImage = previewRect.AddComponent<RawImage>();
                previewImage.color = Color.white;
                previewImage.raycastTarget = false;
                var world = new GameObject("Owned Preview World");
                world.transform.SetParent(root.transform, false);

                var save = Button("Save", root.transform, Vector2.zero, out _, MANAGEMENT_BUTTON_PATH);
                var rename = Button("Rename", root.transform, Vector2.zero, out _, MANAGEMENT_BUTTON_PATH);
                var copy = Button("Copy", root.transform, Vector2.zero, out _, MANAGEMENT_BUTTON_PATH);
                var delete = Button("Delete", root.transform, Vector2.zero, out _, MANAGEMENT_BUTTON_PATH);
                var import = Button("Import", root.transform, Vector2.zero, out _, MANAGEMENT_BUTTON_PATH);
                var export = Button("Export", root.transform, Vector2.zero, out _, MANAGEMENT_BUTTON_PATH);
                var actionButtons = new[] { save, rename, copy, delete, import, export };
                // PresetActions uses equal flexible widths, spacing 10, and a 64-unit spacer.
                // Fit the same arrangement (with Save added) within the assignment grid's edges.
                const float ACTION_LEFT = ASSIGNMENT_LEFT * 1920f;
                const float ACTION_RIGHT = (ASSIGNMENT_LEFT + 9f * 0.061f + 0.056f) * 1920f;
                const float ACTION_SPACING = 10f;
                const float ACTION_GROUP_SPACE = 64f;
                float actionWidth = (ACTION_RIGHT - ACTION_LEFT - 6f * ACTION_SPACING - ACTION_GROUP_SPACE) / actionButtons.Length;
                var actionAnchors = Enumerable.Range(0, actionButtons.Length)
                    .Select(index => new Vector2((ACTION_LEFT + actionWidth / 2f + index * (actionWidth + ACTION_SPACING)
                        + (index >= 4 ? ACTION_GROUP_SPACE + ACTION_SPACING : 0f)) / 1920f,
                        (toolbarBottom - MANAGEMENT_ROW_HEIGHT / 2f) / 1080f)).ToArray();
                for (int i = 0; i < actionButtons.Length; i++)
                {
                    var action = actionButtons[i];
                    var wrapper = ButtonWrapperRect(action);
                    SetButtonRect(wrapper, actionAnchors[i], new Vector2(actionWidth, MANAGEMENT_BUTTON_HEIGHT));
                    var actualButtonRect = action.GetComponent<RectTransform>();
                    actualButtonRect.anchorMin = new Vector2(0f, 0.5f);
                    actualButtonRect.anchorMax = new Vector2(1f, 0.5f);
                    actualButtonRect.anchoredPosition = Vector2.zero;
                    actualButtonRect.sizeDelta = new Vector2(0f, 50f);
                    var actionText = wrapper.GetComponentInChildren<TMP_Text>(true);
                    if (actionText == null)
                        throw new System.InvalidOperationException($"Management button {action.name} has no TMP label in its wrapper.");
                    actionText.rectTransform.offsetMin = new Vector2(8f, 2f);
                    actionText.rectTransform.offsetMax = new Vector2(-8f, -2f);
                    actionText.fontSize = 24f;
                }
                var folderIcon = folderObject.transform.Find("Icon")?.GetComponent<Image>();
                if (folderIcon != null) folderIcon.rectTransform.sizeDelta = new Vector2(24, 24);
                folderButton.onClick.RemoveAllListeners();

                var popup = AnchoredRect("Assignment Popup", root.transform, Vector2.zero,
                    Vector2.one, Vector2.zero, Vector2.zero);
                var backdrop = popup.AddComponent<Image>();
                backdrop.color = new Color(0f, 0f, 0f, 0.55f);
                backdrop.raycastTarget = true;
                var backdropButton = popup.AddComponent<Button>();
                backdropButton.targetGraphic = backdrop;
                backdropButton.onClick.AddListener(menu.CancelPopupFromOutside);
                var popupPanel = AnchoredRect("Assignment Popup Panel", popup.transform,
                    new Vector2(0.32f, 0.18f), new Vector2(0.68f, 0.82f), Vector2.zero, Vector2.zero);
                var panelBackground = popupPanel.AddComponent<Image>();
                panelBackground.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DIALOG_BACKGROUND_PATH);
                if (panelBackground.sprite == null) throw new System.InvalidOperationException("Missing dialog background sprite.");
                panelBackground.type = Image.Type.Sliced;
                panelBackground.color = Color.white;
                var panelOutline = popupPanel.AddComponent<Outline>();
                panelOutline.effectColor = new Color(0.32f, 0.36f, 0.4f, 1f);
                panelOutline.effectDistance = new Vector2(1f, -1f);
                var panelMask = popupPanel.AddComponent<Mask>();
                panelMask.showMaskGraphic = true;
                var popupNavigation = popup.AddComponent<NavigationGroup>();
                var titleBand = AnchoredRect("Assignment Popup Header Band", popupPanel.transform,
                    new Vector2(0f, 0.86f), Vector2.one, Vector2.zero, Vector2.zero);
                var titleBandImage = titleBand.AddComponent<Image>();
                titleBandImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DIALOG_HEADER_PATH);
                if (titleBandImage.sprite == null) throw new System.InvalidOperationException("Missing dialog header sprite.");
                titleBandImage.type = Image.Type.Sliced;
                titleBandImage.color = Color.white;
                titleBandImage.raycastTarget = false;
                // Match the production shared-header treatment (Barlow Medium, normal weight) used by
                // menus such as difficulty select, instead of the bold display font.
                var popupTitle = Text("Assignment Popup Title", titleBand.transform, "Assign A Piece",
                    Vector2.zero, Vector2.zero, 30, false);
                popupTitle.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/Art/Fonts/Barlow/Barlow-Medium.asset");
                popupTitle.fontStyle = FontStyles.Normal;
                Stretch(popupTitle.rectTransform, 8f);
                popupTitle.textWrappingMode = TextWrappingModes.NoWrap;
                popupTitle.overflowMode = TextOverflowModes.Ellipsis;
                var choices = new Button[10];
                var choiceLabels = new TMP_Text[choices.Length];
                for (int i = 0; i < choices.Length; i++)
                {
                    float y = 0.82f - i * 0.085f;
                    var choiceButton = Button("Choice " + i, popup.transform, Vector2.zero,
                        out choiceLabels[i]);
                    choiceButton.transform.parent.SetParent(popupPanel.transform, false);
                    var choiceRect = ButtonWrapperRect(choiceButton);
                    choiceRect.anchorMin = new Vector2(0.12f, y - 0.035f);
                    choiceRect.anchorMax = new Vector2(0.88f, y + 0.035f);
                    choiceRect.offsetMin = choiceRect.offsetMax = Vector2.zero;
                    choiceLabels[i].fontSize = 32;
                    choices[i] = choiceButton;
                }

                Set(menu, "_navigation", navigation);
                SetArray(menu, "_cells", cells);
                Set(menu, "_popup", popup);
                Set(menu, "_popupBackdropButton", backdropButton);
                Set(menu, "_popupNavigation", popupNavigation);
                SetArray(menu, "_choices", choices);
                SetArray(menu, "_choiceLabels", choiceLabels);
                Set(menu, "_save", save);
                Set(menu, "_back", back);
                Set(menu, "_openLayoutFolder", folderButton);
                Set(menu, "_renameLayout", rename);
                Set(menu, "_copyLayout", copy);
                Set(menu, "_deleteLayout", delete);
                Set(menu, "_importLayout", import);
                Set(menu, "_exportLayout", export);
                Set(menu, "_layoutLabel", null);
                Set(menu, "_previewColorLabel", colorLabel);
                Set(menu, "_status", status);
                Set(menu, "_colorsNotice", notice);
                var builtInNoticePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Menu/Settings/Presets/PresetDefaultText.prefab");
                var builtInNotice = (GameObject)PrefabUtility.InstantiatePrefab(builtInNoticePrefab);
                builtInNotice.name = "Built In Notice";
                builtInNotice.transform.SetParent(root.transform, false);
                var noticeRect = builtInNotice.GetComponent<RectTransform>();
                SetAnchors(noticeRect, new Vector2(0.04f, 0.3088889f), new Vector2(0.63f, 0.3788889f));
                builtInNotice.SetActive(false);
                Set(menu, "_builtInNotice", builtInNotice);
                var transformLabels = new[] { "Shift Left", "Shift Right", "Reverse" };
                var transformFields = new[] { "_shiftLeft", "_shiftRight", "_reverseLayout" };
                for (int i = 0; i < transformLabels.Length; i++)
                {
                    var action = Button(transformLabels[i], root.transform, Vector2.zero, out var text,
                        MANAGEMENT_BUTTON_PATH);
                    var wrapper = ButtonWrapperRect(action);
                    SetButtonRect(wrapper, new Vector2(0.14f + i * 0.195f, 0.3438889f),
                        new Vector2(210f, MANAGEMENT_BUTTON_HEIGHT));
                    var buttonRect = action.GetComponent<RectTransform>();
                    buttonRect.anchorMin = new Vector2(0f, 0.5f);
                    buttonRect.anchorMax = new Vector2(1f, 0.5f);
                    buttonRect.anchoredPosition = Vector2.zero;
                    buttonRect.sizeDelta = new Vector2(0f, 50f);
                    text.rectTransform.offsetMin = new Vector2(8f, 2f);
                    text.rectTransform.offsetMax = new Vector2(-8f, -2f);
                    text.fontSize = 24f;
                    wrapper.gameObject.SetActive(false);
                    Set(menu, transformFields[i], action);
                }
                Set(menu, "_layoutDropdown", layoutDropdown);
                Set(menu, "_previewColor", previewColor);
                Set(menu, "_previewOptions", previewOptions);
                Set(menu, "_previewWorld", world.transform);
                Set(menu, "_previewImage", previewImage);
                popup.SetActive(false);
                Directory.CreateDirectory(Path.GetDirectoryName(PREFAB_PATH));
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                AssetDatabase.SaveAssets();
                MountProfileSelector();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static float ReadHelpBarTop()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/PersistentScene.unity");
            try
            {
                var footer = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                    .First(rect => rect.name == "Help Bar");
                return 1080f * footer.anchorMax.y + footer.anchoredPosition.y
                    + footer.sizeDelta.y * (1f - footer.pivot.y);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static TMP_Dropdown ConfigureFullRowDropdown(GameObject row, bool reserveLabel = false)
        {
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown == null) throw new System.InvalidOperationException("Preset dropdown template has no TMP_Dropdown.");
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.transform == dropdown.captionText?.transform) continue;
                if (text.transform == dropdown.itemText?.transform) continue;

                text.gameObject.SetActive(false);
            }

            // Settings rows bring alternating fills and dividers; these controls share one toolbar.
            foreach (var decoration in row.GetComponentsInChildren<Image>(true))
            {
                if (decoration.transform == dropdown.transform || decoration.transform.IsChildOf(dropdown.transform)) continue;
                Object.DestroyImmediate(decoration);
            }

            var rowRect = row.GetComponent<RectTransform>();
            rowRect.localScale = Vector3.one;
            rowRect.anchoredPosition = Vector2.zero;
            rowRect.sizeDelta = Vector2.zero;
            var ancestors = dropdown.transform.parent;
            while (ancestors != null && ancestors != row.transform)
            {
                if (ancestors is RectTransform ancestorRect)
                {
                    ancestorRect.localScale = Vector3.one;
                    ancestorRect.anchorMin = Vector2.zero;
                    ancestorRect.anchorMax = Vector2.one;
                    ancestorRect.offsetMin = Vector2.zero;
                    ancestorRect.offsetMax = Vector2.zero;
                    ancestorRect.anchoredPosition = Vector2.zero;
                    ancestorRect.sizeDelta = Vector2.zero;
                }
                ancestors = ancestors.parent;
            }
            if (ancestors != row.transform)
                throw new System.InvalidOperationException("Dropdown must be nested under its preview control row.");
            var rect = dropdown.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = new Vector2(reserveLabel ? 0.38f : 0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(0f, 64f);
            rect.anchoredPosition = Vector2.zero;
            return dropdown;
        }

        private static TMP_Text AddSectionLabel(Transform parent, string name, string value,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var label = Text(name, parent, value, Vector2.zero, new Vector2(500, 40), 19, true);
            var rect = label.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(8f, 0f);
            rect.offsetMax = Vector2.zero;
            label.alignment = TextAlignmentOptions.Left;
            return label;
        }

        private static void ConfigureSectionLabel(TMP_Text label)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12;
            label.fontSizeMax = 19;
        }

        private static void ConfigureDropdownText(TMP_Dropdown dropdown)
        {
            ConfigureDropdownTextElement(dropdown.captionText);
            ConfigureDropdownTextElement(dropdown.itemText);
            // Keep opened lists readable at the supported 1280x720 Canvas scale.
            // Configure only this menu's instantiated template, not the shared prefab.
            var item = dropdown.itemText.GetComponentInParent<Toggle>(true);
            var itemRect = item.GetComponent<RectTransform>();
            itemRect.localScale = Vector3.one;
            itemRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 56f);
            var scroll = dropdown.template.GetComponent<ScrollRect>();
            if (scroll != null && scroll.content != null)
                scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 64f);
            dropdown.captionText.fontSize = 28f;
            dropdown.itemText.fontSize = 28f;
            dropdown.itemText.rectTransform.offsetMin = new Vector2(14f, 4f);
            dropdown.itemText.rectTransform.offsetMax = new Vector2(-18f, -4f);
            // Lists may be wider than their side-by-side captions. Keep the right
            // edge attached to its control and expand left into the menu's free area.
            var template = dropdown.template;
            template.anchorMin = new Vector2(1f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(1f, 1f);
            template.anchoredPosition = Vector2.zero;
            template.sizeDelta = new Vector2(440f, 300f);
        }

        private static void ConfigureSelectedItemGutter(TMP_Dropdown dropdown)
        {
            // Preserve a dedicated column for the preset dropdown's selected white dot.
            // Every row uses this inset, including rows whose checkmark is hidden.
            var toggle = dropdown.itemText.GetComponentInParent<Toggle>(true);
            var marker = toggle.graphic.rectTransform;
            marker.anchorMin = marker.anchorMax = new Vector2(0f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.anchoredPosition = new Vector2(22f, 0f);
            marker.sizeDelta = new Vector2(12f, 12f);
            dropdown.itemText.rectTransform.offsetMin = new Vector2(42f, 4f);
        }

        private static void ConfigureDropdownTextElement(TMP_Text text)
        {
            if (text == null) return;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Left;
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(20f, 7f);
            rect.offsetMax = new Vector2(-20f, -7f);
        }

        private static TMP_Text ConfigureLayoutLabel(GameObject row)
        {
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            TMP_Text optionLabel = null;
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.transform == dropdown.captionText?.transform || text.transform == dropdown.itemText?.transform)
                    continue;
                if (text.transform.IsChildOf(dropdown.transform)) continue;
                if (text.name == "Option Name")
                {
                    optionLabel = text;
                    break;
                }

                optionLabel ??= text;
            }

            if (optionLabel == null)
                throw new System.InvalidOperationException("Preset dropdown template has no option label for the Layout row.");

            optionLabel.gameObject.SetActive(true);
            optionLabel.text = "LAYOUT";
            optionLabel.enableAutoSizing = false;
            optionLabel.fontSize = 22;
            var labelRect = optionLabel.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.23f, 1f);
            labelRect.offsetMin = new Vector2(12f, 0f);
            labelRect.offsetMax = Vector2.zero;
            optionLabel.alignment = TextAlignmentOptions.Left;
            return optionLabel;
        }

        private static void AddBoundary(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var boundary = AnchoredRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
            var image = boundary.AddComponent<Image>();
            // Use BaseSetting/Divider's muted preset color, with a lighter-weight line.
            image.color = new Color(16f / 255f, 28f / 255f, 43f / 255f, 1f);
            image.raycastTarget = false;
        }

        private static void MountProfileSelector()
        {
            const string path = "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var sidebar = contents.GetComponentInChildren<YARG.Menu.ProfileList.ProfileSidebar>(true);
                var serialized = new SerializedObject(sidebar);
                var sidebarContent = (GameObject)serialized.FindProperty("_sidebarContent").objectReferenceValue;
                var menu = contents.GetComponent<YARG.Menu.ProfileList.ProfileListMenu>();
                serialized.FindProperty("_profileListMenu").objectReferenceValue = menu;

                var colorDropdown = (TMP_Dropdown)serialized.FindProperty("_colorProfileDropdown").objectReferenceValue;
                while (colorDropdown.onValueChanged.GetPersistentEventCount() > 0)
                    UnityEditor.Events.UnityEventTools.RemovePersistentListener(colorDropdown.onValueChanged, 0);
                UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(colorDropdown.onValueChanged,
                    sidebar.ChangeColorProfile);
                var colorRow = colorDropdown.transform;
                while (colorRow.parent != sidebarContent.transform)
                {
                    if (colorRow.parent == null)
                        throw new System.InvalidOperationException("Color Profile must be a direct profile-sidebar row.");
                    colorRow = colorRow.parent;
                }

                // The previous selector was cloned into the Color Profile row. Remove
                // that nested copy before ensuring a complete, independent peer row.
                foreach (var dropdown in colorRow.GetComponentsInChildren<TMP_Dropdown>(true))
                {
                    if (dropdown == colorDropdown) continue;

                    var nestedRow = dropdown.transform;
                    while (nestedRow.parent != colorRow) nestedRow = nestedRow.parent;
                    Object.DestroyImmediate(nestedRow.gameObject);
                }

                var layoutRow = sidebarContent.transform.Find("Elite Drums Layout")?.gameObject;
                if (layoutRow == null)
                {
                    layoutRow = Object.Instantiate(colorRow.gameObject, sidebarContent.transform);
                    layoutRow.name = "Elite Drums Layout";
                }
                layoutRow.transform.SetSiblingIndex(colorRow.GetSiblingIndex() + 1);

                var label = layoutRow.transform.Find("Option Name")?.GetComponent<TMP_Text>();
                if (label != null) label.text = "ELITE DRUMS LAYOUT";

                var layoutSelector = layoutRow.GetComponentInChildren<TMP_Dropdown>(true);
                if (layoutSelector == null || layoutSelector == colorDropdown)
                    throw new System.InvalidOperationException("The Elite layout row must own an independent dropdown.");

                // The cloned row inherits the Color Profile UnityEvent. Clear it so
                // changing a layout cannot mutate the profile's color selection.
                var dropdownData = new SerializedObject(layoutSelector);
                dropdownData.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls").arraySize = 0;
                dropdownData.ApplyModifiedPropertiesWithoutUndo();
                serialized.FindProperty("_eliteLayoutDropdown").objectReferenceValue = layoutSelector;

                const string configureName = "Configure Elite Drums Layout";
                var configureRow = sidebarContent.transform.Find(configureName)?.gameObject;
                if (configureRow == null)
                {
                    var highwayRow = sidebarContent.transform.Find("Configure 4-Lane Drums Highway");
                    if (highwayRow == null)
                        throw new System.InvalidOperationException("The 4-Lane Drums Configure row is missing.");
                    configureRow = Object.Instantiate(highwayRow.gameObject, sidebarContent.transform);
                    configureRow.name = configureName;
                }
                configureRow.transform.SetSiblingIndex(
                    sidebarContent.transform.Find("Configure 5-Lane Drums Highway").GetSiblingIndex() + 1);
                var configureLabel = configureRow.transform.Find("Option Name")?.GetComponent<TMP_Text>();
                if (configureLabel != null)
                {
                    configureLabel.text = YARG.Localization.Localize.Key(PROFILE_CONFIGURE_LABEL_KEY);
                    var localizedLabel = configureLabel.gameObject.GetComponent<YARG.Localization.LocalizeText>() ??
                        configureLabel.gameObject.AddComponent<YARG.Localization.LocalizeText>();
                    var localizedLabelData = new SerializedObject(localizedLabel);
                    localizedLabelData.FindProperty("_localizationKey").stringValue = PROFILE_CONFIGURE_LABEL_KEY;
                    localizedLabelData.ApplyModifiedPropertiesWithoutUndo();
                }
                var configureButton = configureRow.GetComponentInChildren<Button>(true);
                if (configureButton == null)
                    throw new System.InvalidOperationException("The Elite layout Configure row must have a Button.");
                var buttonData = new SerializedObject(configureButton);
                buttonData.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").arraySize = 0;
                buttonData.ApplyModifiedPropertiesWithoutUndo();
                serialized.FindProperty("_eliteLayoutConfigureButton").objectReferenceValue = configureButton;

                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static GameObject AnchoredRect(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var result = Rect(name, parent, Vector2.zero);
            var rect = result.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return result;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void ConfigureDropdownRow(GameObject row, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static RectTransform ButtonWrapperRect(Button button)
        {
            var wrapper = button.transform.parent as RectTransform;
            if (wrapper == null)
                throw new System.InvalidOperationException(
                    $"Production button {button.name} must be nested under a RectTransform wrapper.");
            return wrapper;
        }

        private static void SetButtonRect(RectTransform rect, Vector2 anchor) =>
            SetButtonRect(rect, anchor, new Vector2(180, 76));

        private static void SetButtonRect(RectTransform rect, Vector2 anchor, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static GameObject Rect(string name, Transform parent, Vector2 size)
        {
            var result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            result.GetComponent<RectTransform>().sizeDelta = size;
            return result;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string value, Vector2 position,
            Vector2 size, int fontSize, bool bold = false)
        {
            var result = Rect(name, parent, size);
            result.GetComponent<RectTransform>().anchoredPosition = position;
            var text = result.AddComponent<TextMeshProUGUI>();
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(bold
                ? "Assets/Art/Fonts/Barlow/Barlow-Bold.asset"
                : "Assets/Art/Fonts/Barlow/Barlow-Medium.asset");
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        internal static string FretNumberLabel(int column) => (column + 1).ToString();

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static GameObject InstantiateIconButton(Transform parent, string name,
            string iconPath, string iconName = null)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(ICON_BUTTON_PATH);
            if (template == null) throw new System.InvalidOperationException("Missing production icon button prefab.");
            var result = (GameObject)PrefabUtility.InstantiatePrefab(template);
            PrefabUtility.UnpackPrefabInstance(result, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            result.name = name;
            result.transform.SetParent(parent, false);
            var background = result.transform.Find("Button")?.GetComponent<Image>();
            var icon = result.transform.Find("Icon")?.GetComponent<Image>();
            if (background == null || icon == null)
                throw new System.InvalidOperationException("Production icon button is missing its authored background or icon.");
            background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ROUNDED_RECT_PATH);
            background.type = Image.Type.Sliced;
            var icons = AssetDatabase.LoadAllAssetsAtPath(iconPath);
            var iconSprite = iconName == null
                ? AssetDatabase.LoadAssetAtPath<Sprite>(iconPath)
                : System.Array.Find(icons, asset => asset is Sprite sprite && sprite.name == iconName) as Sprite;
            if (iconSprite == null) throw new System.InvalidOperationException($"Missing icon sprite {iconName ?? iconPath}.");
            icon.sprite = iconSprite;
            return result;
        }

        private static Button Button(string label, Transform parent, Vector2 position) =>
            Button(label, parent, position, out _);

        private static Button Button(string label, Transform parent, Vector2 position, out TMP_Text text,
            string prefabPath = BUTTON_PATH)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (template == null) throw new System.InvalidOperationException("Missing production button prefab.");
            var result = (GameObject) PrefabUtility.InstantiatePrefab(template);
            // Unpack so the button hierarchy (including its label) is saved inline in
            // the generated menu prefab; nested instances drop their children on save.
            PrefabUtility.UnpackPrefabInstance(result, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            result.name = label;
            result.transform.SetParent(parent, false);
            result.GetComponent<RectTransform>().anchoredPosition = position;
            text = result.GetComponentInChildren<TMP_Text>(true);
            if (text == null) throw new System.InvalidOperationException("Production button prefab has no TMP label.");
            text.text = label;
            text.enableAutoSizing = false;
            text.fontSize = 24;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.Center;
            var button = result.GetComponentInChildren<Button>(true);
            if (button == null) throw new System.InvalidOperationException("Production button prefab has no Button component.");
            return button;
        }

        private static void Set(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray<T>(Object target, string property, T[] values) where T : Object
        {
            var serialized = new SerializedObject(target);
            var array = serialized.FindProperty(property);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
