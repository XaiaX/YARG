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
            YARG.Helpers.PathHelper.Init();
            var localizationManager = typeof(YARG.Localization.LocalizationManager);
            var localizationMap = (System.Collections.Generic.IDictionary<string, string>)localizationManager
                .GetField("_localizationMap", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .GetValue(null);
            var previousLocalizations = localizationMap.ToArray();
            var cultureProperty = localizationManager.GetProperty("CultureCode");
            var previousCulture = cultureProperty.GetValue(null);
            try
            {
                var loadLanguage = localizationManager.GetMethod("TryParseAndLoadLanguage",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (!(bool)loadLanguage.Invoke(null, new object[] { "en-US" }))
                    throw new System.InvalidOperationException(
                        "The menu builder requires the production English localization for measurement.");
                BuildWithEnglishLocalization();
            }
            finally
            {
                localizationMap.Clear();
                foreach (var entry in previousLocalizations) localizationMap.Add(entry.Key, entry.Value);
                cultureProperty.GetSetMethod(true).Invoke(null, new[] { previousCulture });
            }
        }

        private static void BuildWithEnglishLocalization()
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
                backdropButton.transition = Selectable.Transition.None;
                backdropButton.onClick.AddListener(menu.CancelPopupFromOutside);
                var popupPanel = AnchoredRect("Assignment Popup Panel", popup.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                popupPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(600f, 735f);
                var difficultyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Menu/DifficultySelect/DifficultySelectMenu.prefab");
                if (difficultyPrefab == null) throw new System.InvalidOperationException("Missing difficulty menu prefab.");
                var authoredMenu = difficultyPrefab.transform.Find("Menu");
                var panelBackground = popupPanel.AddComponent<Image>();
                panelBackground.color = Color.clear;
                panelBackground.canvasRenderer.cullTransparentMesh = false;
                panelBackground.raycastTarget = true;
                // Only the straight-sided body is rectangular; outer corners belong to the bands.
                var listSurface = AnchoredRect("Dark List Surface", popupPanel.transform, Vector2.zero,
                    Vector2.one, new Vector2(0f, 15f), new Vector2(0f, -80f)).AddComponent<Image>();
                listSurface.color = new Color(1f / 255f, 8f / 255f, 14f / 255f, 1f);
                listSurface.raycastTarget = false;
                // A click on empty panel space must terminate here, not bubble to the backdrop.
                var panelBlocker = popupPanel.AddComponent<Button>();
                panelBlocker.targetGraphic = panelBackground;
                panelBlocker.transition = Selectable.Transition.None;
                panelBlocker.navigation = new Navigation { mode = Navigation.Mode.None };
                var popupNavigation = popup.AddComponent<NavigationGroup>();
                var titleBand = Object.Instantiate(authoredMenu.Find("Header").gameObject, popupPanel.transform, false);
                titleBand.name = "Assignment Popup Header Band";
                var popupTitle = titleBand.GetComponentInChildren<TMP_Text>(true);
                foreach (Transform child in titleBand.transform)
                {
                    if (child != popupTitle.transform) Object.DestroyImmediate(child.gameObject);
                }
                Object.DestroyImmediate(titleBand.GetComponent<HorizontalLayoutGroup>());
                Stretch(popupTitle.rectTransform);
                popupTitle.rectTransform.offsetMin = new Vector2(48f, 12f);
                popupTitle.rectTransform.offsetMax = new Vector2(-48f, -12f);
                popupTitle.text = YARG.Localization.Localize.Key("Menu.EliteLayout.AssignmentTitle");
                popupTitle.enableAutoSizing = false;
                popupTitle.textWrappingMode = TextWrappingModes.Normal;
                popupTitle.overflowMode = TextOverflowModes.Overflow;
                foreach (var graphic in titleBand.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
                var footer = Object.Instantiate(authoredMenu.Find("Footer").gameObject, popupPanel.transform, false);
                footer.GetComponent<Image>().raycastTarget = false;
                var headerBacking = ConfigurePopupBand(titleBand, EliteAssignmentPopupSpriteBuilder.EnsureHeader());
                ConfigurePopupBand(footer, EliteAssignmentPopupSpriteBuilder.EnsureFooter());
                var viewport = AnchoredRect("Assignment Choices Viewport", popupPanel.transform,
                    Vector2.zero, Vector2.one, new Vector2(0f, 15f), new Vector2(0f, -80f));
                viewport.AddComponent<RectMask2D>();
                var content = AnchoredRect("Assignment Choices", viewport.transform,
                    new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero);
                var contentRect = content.GetComponent<RectTransform>();
                contentRect.pivot = new Vector2(0.5f, 1f);
                var list = content.AddComponent<VerticalLayoutGroup>();
                list.childControlWidth = true;
                list.childControlHeight = true;
                list.childForceExpandWidth = true;
                list.childForceExpandHeight = false;
                var fitter = content.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = popupPanel.AddComponent<ScrollRect>();
                scroll.viewport = viewport.GetComponent<RectTransform>();
                scroll.content = contentRect;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 36f;
                var rowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Menu/DifficultySelect/DifficultyItem.prefab");
                var coloredRowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Menu/DifficultySelect/DifficultyItemColored.prefab");
                var coloredSelection = coloredRowPrefab.transform.Find("Selected Background").GetComponent<Image>();
                var choices = new Button[10];
                var choiceLabels = new TMP_Text[choices.Length];
                for (int i = 0; i < choices.Length; i++)
                {
                    var row = Object.Instantiate(rowPrefab, content.transform, false);
                    row.name = "Choice " + i;
                    Object.DestroyImmediate(row.GetComponent<YARG.Menu.DifficultySelect.DifficultyItem>());
                    Object.DestroyImmediate(row.GetComponent<NavigatableButton>());
                    Object.DestroyImmediate(row.GetComponent<VerticalLayoutGroup>());
                    row.transform.Find("Header").gameObject.SetActive(false);
                    var rowHeight = row.AddComponent<LayoutElement>();
                    rowHeight.minHeight = rowHeight.preferredHeight = 64f;
                    var label = row.transform.Find("Body").GetComponent<TMP_Text>();
                    Stretch(label.rectTransform);
                    label.rectTransform.offsetMin = new Vector2(24f, 8f);
                    label.rectTransform.offsetMax = new Vector2(-24f, -8f);
                    label.textWrappingMode = TextWrappingModes.Normal;
                    label.overflowMode = TextOverflowModes.Overflow;
                    label.enableAutoSizing = false;
                    var selection = row.transform.Find("Selected Background").GetComponent<Image>();
                    var ordinarySprite = selection.sprite;
                    var ordinaryColor = selection.color;
                    EditorUtility.CopySerialized(coloredSelection, selection);
                    foreach (var graphic in row.GetComponentsInChildren<Graphic>(true))
                        graphic.raycastTarget = false;
                    var buttonObject = AnchoredRect("Button", row.transform, Vector2.zero, Vector2.one,
                        Vector2.zero, Vector2.zero);
                    var hitTarget = buttonObject.AddComponent<Image>();
                    hitTarget.color = Color.clear;
                    hitTarget.canvasRenderer.cullTransparentMesh = false;
                    hitTarget.raycastTarget = true;
                    var choiceButton = buttonObject.AddComponent<Button>();
                    choiceButton.targetGraphic = hitTarget;
                    choiceButton.transition = Selectable.Transition.None;
                    choiceButton.navigation = new Navigation { mode = Navigation.Mode.None };
                    // Keep the ordinary authored glow; action mode adopts the colored Done fill.
                    if (coloredSelection.sprite != null)
                        throw new System.InvalidOperationException("Difficulty colored row must use a solid selection fill.");
                    var adapter = buttonObject.AddComponent<AssignmentPopupRow>();
                    adapter.Configure(selection, label, ordinarySprite, ordinaryColor);
                    choiceLabels[i] = label;
                    choices[i] = choiceButton;
                }

                var popupLayout = popup.AddComponent<AssignmentPopupLayout>();
                popupLayout.Configure(popupPanel.GetComponent<RectTransform>(), root.GetComponent<RectTransform>(),
                    popupTitle, choiceLabels, scroll, listSurface.rectTransform, headerBacking);
                Set(menu, "_popupLayout", popupLayout);
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
                const float ACTION_ROW_Y = 0.3438889f;
                const float TRANSFORM_LEFT = 0.0225f;
                const float ACTION_GAP = 12f;
                float leftSectionRight = MountLeftyFlipControl(root.transform, menu, ACTION_ROW_Y);
                var noticeRect = builtInNotice.GetComponent<RectTransform>();
                SetAnchors(noticeRect, new Vector2(TRANSFORM_LEFT, 0.245f), new Vector2(leftSectionRight, 0.425f));
                foreach (var noticeText in builtInNotice.GetComponentsInChildren<TMP_Text>(true))
                {
                    noticeText.alignment = TextAlignmentOptions.Center;
                    noticeText.textWrappingMode = TextWrappingModes.Normal;
                    noticeText.overflowMode = TextOverflowModes.Overflow;
                    noticeText.enableAutoSizing = false;
                }
                builtInNotice.SetActive(false);
                Set(menu, "_builtInNotice", builtInNotice);
                var transformLabels = new[]
                {
                    EliteDrumsTransformButtonLayout.ShiftLeftLabel(YARG.Localization.Localize.Key("Menu.EliteLayout.ShiftLeft")),
                    EliteDrumsTransformButtonLayout.ReverseLabel(YARG.Localization.Localize.Key("Menu.EliteLayout.Reverse")),
                    EliteDrumsTransformButtonLayout.ShiftRightLabel(YARG.Localization.Localize.Key("Menu.EliteLayout.ShiftRight"))
                };
                var transformNames = new[] { "Shift Left", "Reverse", "Shift Right" };
                var transformFields = new[] { "_shiftLeft", "_reverseLayout", "_shiftRight" };
                var transformButtons = new Button[transformLabels.Length];
                var transformTexts = new TMP_Text[transformLabels.Length];
                float totalActionWidth = 0f;
                for (int i = 0; i < transformLabels.Length; i++)
                {
                    transformButtons[i] = Button(transformNames[i], root.transform, Vector2.zero,
                        out transformTexts[i], MANAGEMENT_BUTTON_PATH);
                    transformTexts[i].font = EliteDrumsTransformFontBuilder.EnsureTransformFont(transformTexts[i].font);
                    transformTexts[i].text = transformLabels[i];
                    transformTexts[i].fontSize = 24f;
                    transformTexts[i].enableAutoSizing = false;
                    totalActionWidth += transformTexts[i].GetPreferredValues(transformLabels[i]).x + 32f;
                }
                totalActionWidth += ACTION_GAP * (transformLabels.Length - 1);
                float availableActionWidth = (leftSectionRight - TRANSFORM_LEFT) * 1920f;
                // Final equal-width fitting below also handles longer localized captions.
                float actionX = (TRANSFORM_LEFT + leftSectionRight) * 960f - totalActionWidth / 2f;
                for (int i = 0; i < transformLabels.Length; i++)
                {
                    var action = transformButtons[i];
                    float width = transformTexts[i].GetPreferredValues(transformLabels[i]).x + 32f;
                    var wrapper = ButtonWrapperRect(action);
                    SetButtonRect(wrapper, new Vector2((actionX + width / 2f) / 1920f, ACTION_ROW_Y),
                        new Vector2(width, MANAGEMENT_BUTTON_HEIGHT));
                    var buttonRect = action.GetComponent<RectTransform>();
                    buttonRect.anchorMin = new Vector2(0f, 0.5f);
                    buttonRect.anchorMax = new Vector2(1f, 0.5f);
                    buttonRect.anchoredPosition = Vector2.zero;
                    buttonRect.sizeDelta = new Vector2(0f, 50f);
                    transformTexts[i].rectTransform.offsetMin = new Vector2(8f, 2f);
                    transformTexts[i].rectTransform.offsetMax = new Vector2(-8f, -2f);
                    wrapper.gameObject.SetActive(false);
                    Set(menu, transformFields[i], action);
                    actionX += width + ACTION_GAP;
                }
                // An inactive root Canvas has no resolved screen rect during prefab authoring.
                var authoredRootRect = root.GetComponent<RectTransform>();
                var originalRootSize = authoredRootRect.sizeDelta;
                authoredRootRect.sizeDelta = new Vector2(1920f, 1080f);
                if (!EliteDrumsTransformButtonLayout.Apply(transformButtons.Select(ButtonWrapperRect).ToArray(),
                    transformTexts, root.GetComponent<RectTransform>(), TRANSFORM_LEFT, leftSectionRight))
                    throw new System.InvalidOperationException("Transform labels cannot fit at the readable font-size floor.");
                authoredRootRect.sizeDelta = originalRootSize;
                var serializedMenu = new SerializedObject(menu);
                serializedMenu.FindProperty("_transformSectionRight").floatValue = leftSectionRight;
                serializedMenu.ApplyModifiedPropertiesWithoutUndo();
                Set(menu, "_layoutDropdown", layoutDropdown);
                Set(menu, "_previewColor", previewColor);
                Set(menu, "_previewOptions", previewOptions);
                Set(menu, "_previewWorld", world.transform);
                Set(menu, "_previewImage", previewImage);
                popup.transform.SetAsLastSibling();
                popup.SetActive(false);
                Directory.CreateDirectory(Path.GetDirectoryName(PREFAB_PATH));
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                AssetDatabase.SaveAssets();
                // The profile entry point is already authored in its own prefab; generating
                // this editor must not rewrite the source used for its standard controls.
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static RectTransform ConfigurePopupBand(GameObject bandObject, EliteAssignmentPopupSpriteBuilder.Band band)
        {
            var artwork = bandObject.GetComponent<Image>();
            artwork.sprite = band.Artwork;
            artwork.type = Image.Type.Sliced;
            artwork.pixelsPerUnitMultiplier = band.PixelsPerUnitMultiplier();
            artwork.raycastTarget = false;
            var backing = AnchoredRect(bandObject.name + " Opaque Backing", bandObject.transform.parent,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            EditorUtility.CopySerialized(bandObject.GetComponent<RectTransform>(), backing.GetComponent<RectTransform>());
            backing.transform.SetSiblingIndex(bandObject.transform.GetSiblingIndex());
            var image = backing.AddComponent<Image>();
            image.sprite = band.Backing;
            image.color = new Color(1f / 255f, 8f / 255f, 14f / 255f, 1f);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = band.PixelsPerUnitMultiplier();
            image.raycastTarget = false;
            return backing.GetComponent<RectTransform>();
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
            // Keep every editor separator at the same two-pixel stroke, regardless of
            // the caller's legacy thickness. Compact toolbar dividers are centered at 56px.
            if (anchorMin.x == anchorMax.x)
            {
                float center = (offsetMin.x + offsetMax.x) / 2f;
                offsetMin.x = center - 1f;
                offsetMax.x = center + 1f;
                if (name == "Layout Colors Separator" || name == "Lefty Flip Divider")
                {
                    float centerY = (offsetMin.y + offsetMax.y) / 2f;
                    offsetMin.y = centerY - 28f;
                    offsetMax.y = centerY + 28f;
                }
            }
            else
            {
                float center = (offsetMin.y + offsetMax.y) / 2f;
                offsetMin.y = center - 1f;
                offsetMax.y = center + 1f;
            }
            var boundary = AnchoredRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
            var image = boundary.AddComponent<Image>();
            // Use BaseSetting/Divider's muted preset color.
            image.color = new Color(16f / 255f, 28f / 255f, 43f / 255f, 1f);
            image.raycastTarget = false;
        }

        private static float MountLeftyFlipControl(Transform editorRoot, EliteDrumsLayoutMenu menu,
            float rowY)
        {
            const float GRID_RIGHT = ASSIGNMENT_LEFT + 9f * 0.061f + 0.056f;
            const float DIVIDER_GAP = 0.018f;
            const float TOGGLE_RIGHT_INSET = 15f;
            const float LABEL_TOGGLE_GAP = 12f;
            const float LABEL_ROW_PADDING = 20f;
            const string SOURCE_PATH = "Assets/Prefabs/Menu/ProfileList/ProfileListMenu.prefab";
            const string LOCALIZATION_KEY = "Menu.DifficultySelect.LeftyFlip";

            var contents = PrefabUtility.LoadPrefabContents(SOURCE_PATH);
            try
            {
                var sidebar = contents.GetComponentInChildren<YARG.Menu.ProfileList.ProfileSidebar>(true);
                var sidebarData = new SerializedObject(sidebar);
                var sourceToggle = sidebarData.FindProperty("_leftyFlipToggle").objectReferenceValue as Toggle;
                if (sourceToggle == null)
                    throw new System.InvalidOperationException("Profile List must serialize its Lefty Flip toggle.");
                var sourceRow = sourceToggle.transform;
                var sourceContentObject = sidebarData.FindProperty("_sidebarContent").objectReferenceValue;
                var sourceContent = sourceContentObject is GameObject contentObject
                    ? contentObject.transform : ((Component)sourceContentObject).transform;
                while (sourceRow.parent != sourceContent)
                {
                    if (sourceRow.parent == null)
                        throw new System.InvalidOperationException("Lefty Flip must be a direct profile-sidebar row.");
                    sourceRow = sourceRow.parent;
                }

                var row = Object.Instantiate(sourceRow.gameObject, editorRoot, false);
                row.name = "Lefty Flip";
                var toggle = row.GetComponentsInChildren<Toggle>(true).Single();
                var label = row.GetComponentsInChildren<TMP_Text>(true)
                    .FirstOrDefault(text => text.name == "Option Name");
                if (label == null)
                    throw new System.InvalidOperationException("Lefty Flip source row must contain its authored label.");
                // The authored on-state graphic is a sibling of Toggle, not its child.
                // Move it before removing sidebar decoration so Toggle retains both states.
                var onGraphic = toggle.graphic;
                if (onGraphic == null)
                    throw new System.InvalidOperationException("Lefty Flip source toggle must contain its on-state graphic.");
                onGraphic.transform.SetParent(toggle.transform, false);
                var onRect = onGraphic.rectTransform;
                onRect.anchorMin = Vector2.zero;
                onRect.anchorMax = Vector2.one;
                onRect.pivot = new Vector2(0.5f, 0.5f);
                onRect.offsetMin = Vector2.zero;
                onRect.offsetMax = Vector2.zero;
                foreach (var decoration in row.GetComponentsInChildren<Image>(true)
                             .Where(image => image.transform != toggle.transform
                                 && !image.transform.IsChildOf(toggle.transform)))
                    Object.DestroyImmediate(decoration.gameObject);

                var sourceToggleRect = sourceToggle.GetComponent<RectTransform>();
                var sourceGraphicRect = sourceToggle.graphic.rectTransform;
                float toggleWidth = sourceGraphicRect.rect.width * sourceGraphicRect.localScale.x
                    * sourceToggleRect.localScale.x;
                float toggleHeight = sourceGraphicRect.rect.height * sourceGraphicRect.localScale.y
                    * sourceToggleRect.localScale.y;
                float labelPreferredWidth = label.GetPreferredValues(
                    YARG.Localization.Localize.Key(LOCALIZATION_KEY)).x;
                float labelWidth = labelPreferredWidth + LABEL_TOGGLE_GAP + LABEL_ROW_PADDING;
                float rightSectionWidth = (toggleWidth + TOGGLE_RIGHT_INSET + labelWidth) / 1920f;
                float dividerX = GRID_RIGHT - rightSectionWidth - DIVIDER_GAP / 2f;
                float leftSectionRight = dividerX - DIVIDER_GAP / 2f;
                var rowRect = row.GetComponent<RectTransform>();
                rowRect.anchorMin = new Vector2(dividerX + DIVIDER_GAP / 2f, rowY);
                rowRect.anchorMax = new Vector2(GRID_RIGHT, rowY);
                rowRect.pivot = new Vector2(1f, 0.5f);
                rowRect.anchoredPosition = Vector2.zero;
                rowRect.sizeDelta = new Vector2(0f, toggleHeight);

                var toggleEvents = new SerializedObject(toggle);
                var persistentCalls = toggleEvents.FindProperty("onValueChanged.m_PersistentCalls.m_Calls");
                persistentCalls.arraySize = 0;
                toggleEvents.ApplyModifiedPropertiesWithoutUndo();
                foreach (var component in row.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null || component is Toggle) continue;
                    if (component.GetType().Namespace?.StartsWith("YARG.Menu.Navigation") == true)
                        Object.DestroyImmediate(component);
                }

                label.text = YARG.Localization.Localize.Key(LOCALIZATION_KEY);
                var localized = label.GetComponent<YARG.Localization.LocalizeText>() ??
                    label.gameObject.AddComponent<YARG.Localization.LocalizeText>();
                var localizedData = new SerializedObject(localized);
                localizedData.FindProperty("_localizationKey").stringValue = LOCALIZATION_KEY;
                localizedData.ApplyModifiedPropertiesWithoutUndo();

                var toggleRect = toggle.GetComponent<RectTransform>();
                toggleRect.anchorMin = new Vector2(1f, 0.5f);
                toggleRect.anchorMax = new Vector2(1f, 0.5f);
                toggleRect.pivot = new Vector2(1f, 0.5f);
                toggleRect.anchoredPosition = new Vector2(-TOGGLE_RIGHT_INSET, 0f);
                // The source Toggle root is stretched across the sidebar row; its rect is not
                // the checkbox size. Give the copied control the authored graphic's local bounds.
                toggleRect.sizeDelta = sourceGraphicRect.rect.size;
                toggleRect.localScale = sourceToggleRect.localScale;
                var labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(0f, 1f);
                labelRect.pivot = new Vector2(0f, 0.5f);
                labelRect.anchoredPosition = new Vector2(LABEL_ROW_PADDING / 2f, 0f);
                labelRect.sizeDelta = new Vector2(labelWidth - LABEL_ROW_PADDING, 0f);

                var focus = AnchoredRect("Lefty Flip Focus", toggle.transform,
                    Vector2.zero, Vector2.one, new Vector2(-4f, -4f), new Vector2(4f, 4f));
                var focusImage = focus.AddComponent<Image>();
                focusImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Art/Menu/Common/Buttons/ButtonOutline.png");
                focusImage.type = Image.Type.Sliced;
                focusImage.fillCenter = true;
                focusImage.color = RuntimeNavigatable.SelectedTextColor;
                focusImage.raycastTarget = false;
                focus.SetActive(false);

                AddBoundary("Lefty Flip Divider", editorRoot,
                    new Vector2(dividerX, rowY), new Vector2(dividerX, rowY),
                    new Vector2(-1f, -28f), new Vector2(1f, 28f));
                Set(menu, "_leftyFlipToggle", toggle);
                return leftSectionRight;
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
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
