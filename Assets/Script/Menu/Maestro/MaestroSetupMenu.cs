// pattern: Imperative Shell

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Dialogs;
using YARG.Menu.Data;
using YARG.Menu.DifficultySelect;
using YARG.Menu.Filters;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings;
using YARG.Song;

namespace YARG.Menu.Maestro
{
    /// <summary>
    /// In-game Maestro finalization page. All edits are staged in <see cref="MaestroSetupSession"/>
    /// until Continue validates and commits the complete active-player set.
    /// </summary>
    public sealed class MaestroSetupMenu : MonoBehaviour
    {
        private enum AdjustmentCategory
        {
            Modifiers,
            Accessibility,
        }

        private static readonly OpenLaneDisplayType[] OpenLaneOptions =
        {
            OpenLaneDisplayType.Never,
            OpenLaneDisplayType.Always,
            OpenLaneDisplayType.IfChartContainsOpens,
        };

        private static readonly Dictionary<string, Sprite> InstrumentIconCache = new();
        private const float FocusedPaneBackgroundAlpha = 0.9f;
        private const float UnfocusedPaneBackgroundAlpha = 0.2f;
        private const float UnfocusedEditorBackgroundAlpha = 0.5f;
        private const float UnfocusedEditorContentAlpha = 0.5f;

        [Header("Page")]
        [SerializeField] private TMP_Text _songTitle;
        [SerializeField] private TMP_Text _errorText;
        [SerializeField] private TMP_Text _controllerLockText;
        [SerializeField] private Transform _playerRowContainer;
        [SerializeField] private MaestroPlayerRow _playerRowPrefab;
        [SerializeField] private ScrollRect _playerScroll;
        [SerializeField] private Image _playerPanelBackground;
        [SerializeField] private NavigationGroup _navigationGroup;

        [Header("Selected player editor")]
        [SerializeField] private GameObject _selectedPlayerEditor;
        [SerializeField] private Image _selectedPlayerBackground;
        [SerializeField] private NavigationGroup _rightNavigationGroup;
        [SerializeField] private TMP_Text _selectedPlayerText;
        [SerializeField] private TMP_Dropdown _instrumentDropdown;
        [SerializeField] private TMP_Dropdown _difficultyDropdown;
        [SerializeField] private TMP_InputField _noteSpeedField;
        [SerializeField] private TMP_InputField _highwayLengthField;
        [SerializeField] private TMP_InputField _inputCalibrationField;
        [SerializeField] private TMP_InputField _songSpeedField;
        [SerializeField] private ModifierItem _modifierItemPrefab;
        [SerializeField] private NavigatableUnityButton _modifierButton;
        [SerializeField] private NavigatableUnityButton _accessibilityButton;
        [SerializeField] private NavigatableUnityButton _sitOutButton;
        [SerializeField] private NavigatableUnityButton _playButton;

        private readonly Dictionary<Guid, MaestroPlayerRow> _rows = new();
        private Guid _selectedProfileId;
        private IDisposable _controllerLock;
        private NavigationScheme _scheme;
        private bool _leaving;
        private bool _editingPlayer;
        private bool _controllerLockEnabled = true;
        private Instrument[] _instrumentOptions = Array.Empty<Instrument>();
        private Difficulty[] _difficultyOptions = Array.Empty<Difficulty>();
        private MaestroDropdownNavigatable _instrumentNavigation;
        private MaestroDropdownNavigatable _difficultyNavigation;
        private MaestroInputNavigatable _noteSpeedNavigation;
        private MaestroInputNavigatable _highwayLengthNavigation;
        private MaestroInputNavigatable _calibrationNavigation;
        private CanvasGroup _playButtonCanvasGroup;
        private int? _lastRightSelectionIndex;
        private readonly Dictionary<Graphic, float> _selectedEditorGraphicAlphas = new();
        private Image _headerSourceIcon;
        private Coroutine _adjustmentFocusRestoreCoroutine;

        private MaestroSetupSession Session => MaestroSetupSession.Active;

        private void Awake()
        {
            CaptureSelectedEditorGraphicAlphas();
        }

        private void OnEnable()
        {
            if (Session == null)
            {
                gameObject.SetActive(false);
                return;
            }

            _leaving = false;
            _editingPlayer = false;
            _controllerLockEnabled = true;
            ConfigureHeader();
            SetEditorVisible(true);
            EnsureFocusCanvasGroups();
            ConfigurePaneHoverTargets();
            AcquireControllerLock();
            if (_navigationGroup != null)
                _navigationGroup.SelectionChanged += OnNavigationSelectionChanged;
            if (_rightNavigationGroup != null)
                _rightNavigationGroup.SelectionChanged += OnRightNavigationSelectionChanged;

            ConfigureDropdowns();
            ConfigureInputFields();
            CaptureSelectedEditorGraphicAlphas();
            BuildRows();
            ConfigureButtons();
            ConfigureNavigation();
            PushNavigationScheme();
            RefreshView();
        }

        private void OnDisable()
        {
            CloseDropdowns();
            if (_adjustmentFocusRestoreCoroutine != null)
            {
                StopCoroutine(_adjustmentFocusRestoreCoroutine);
                _adjustmentFocusRestoreCoroutine = null;
            }
            if (_navigationGroup != null)
                _navigationGroup.SelectionChanged -= OnNavigationSelectionChanged;
            if (_rightNavigationGroup != null)
                _rightNavigationGroup.SelectionChanged -= OnRightNavigationSelectionChanged;
            ResetMaestroNavigationStack();
            ReleaseControllerLock();
            if (_scheme != null && Navigator.Instance != null)
                Navigator.Instance.RemoveScheme(_scheme);
            _scheme = null;
        }

        private void ConfigureHeader()
        {
            var header = transform.Find("Header");
            if (header == null)
                return;

            // Maestro's authored Header is the song-info host. The shared visual
            // header is nested beneath it so the song title/icon can remain separate
            // from the centered page title and red back button.
            var sharedHeader = header.Find("SharedHeader") ?? header.Find("Header") ?? header;
            var pageTitle = sharedHeader.Find("Single Header Text")?.GetComponent<TMP_Text>();
            if (pageTitle != null)
            {
                pageTitle.gameObject.SetActive(true);
                pageTitle.text = "Player Settings Summary";
            }

            var backButton = FindHeaderBackButton(sharedHeader);
            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(BackToSongSelect);
            }

            if (_controllerLockText != null)
                _controllerLockText.gameObject.SetActive(false);

            ConfigureHeaderSongInfo();
            ConfigureHeaderSourceIcon(header);
        }

        private void ConfigureHeaderSongInfo()
        {
            if (_songTitle == null)
                return;

            var rectTransform = _songTitle.rectTransform;
            rectTransform.anchorMin = new Vector2(1f, 0.5f);
            rectTransform.anchorMax = new Vector2(1f, 0.5f);
            rectTransform.pivot = new Vector2(1f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(-90f, 0f);
            rectTransform.sizeDelta = new Vector2(450f, 72f);
            _songTitle.alignment = TextAlignmentOptions.Right;
            rectTransform.SetAsLastSibling();
        }

        private static Button FindHeaderBackButton(Transform header)
        {
            foreach (var button in header.GetComponentsInChildren<Button>(true))
            {
                var image = button.GetComponent<Image>();
                var sprite = image != null ? image.sprite : null;
                if (sprite != null &&
                    sprite.name.Contains("RedBackButton", StringComparison.OrdinalIgnoreCase))
                {
                    return button;
                }
            }

            return null;
        }

        private void ConfigureHeaderSourceIcon(Transform header)
        {
            _headerSourceIcon = header.Find("Source Icon")?.GetComponent<Image>();
            if (_headerSourceIcon == null)
            {
                var iconObject = new GameObject("Source Icon",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconObject.transform.SetParent(header, false);
                _headerSourceIcon = iconObject.GetComponent<Image>();

                var rectTransform = iconObject.GetComponent<RectTransform>();
                rectTransform.anchorMin = new Vector2(1f, 0.5f);
                rectTransform.anchorMax = new Vector2(1f, 0.5f);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.anchoredPosition = new Vector2(-40f, 0f);
                rectTransform.sizeDelta = new Vector2(64f, 64f);
                _headerSourceIcon.color = new Color(1f, 1f, 1f, 0.8f);
                _headerSourceIcon.raycastTarget = false;
            }

            var song = GlobalVariables.State.CurrentSong;
            _headerSourceIcon.sprite = song == null ? null : SongSources.SourceToIcon(song.Source);
            _headerSourceIcon.gameObject.SetActive(_headerSourceIcon.sprite != null);
        }

        private void SetEditorVisible(bool visible)
        {
            if (_selectedPlayerEditor != null)
                _selectedPlayerEditor.SetActive(visible);
        }

        private void EnsureFocusCanvasGroups()
        {
            if (_playerPanelBackground == null && _playerScroll != null)
                _playerPanelBackground = _playerScroll.GetComponent<Image>();
            if (_selectedPlayerBackground == null && _selectedPlayerEditor != null)
                _selectedPlayerBackground = _selectedPlayerEditor.GetComponent<Image>();
            if (_playButton != null)
                _playButtonCanvasGroup = _playButton.GetComponent<CanvasGroup>() ??
                    _playButton.gameObject.AddComponent<CanvasGroup>();
        }

        private void CaptureSelectedEditorGraphicAlphas()
        {
            if (_selectedPlayerEditor == null)
                return;

            foreach (var graphic in _selectedPlayerEditor.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic != null && graphic != _selectedPlayerBackground &&
                    !_selectedEditorGraphicAlphas.ContainsKey(graphic))
                {
                    _selectedEditorGraphicAlphas.Add(graphic, graphic.color.a);
                }
            }
        }

        private void UpdateFocusVisual()
        {
            EnsureFocusCanvasGroups();
            bool editorFocused = _editingPlayer;

            SetGraphicAlpha(_playerPanelBackground,
                editorFocused ? UnfocusedPaneBackgroundAlpha : FocusedPaneBackgroundAlpha);
            SetGraphicAlpha(_selectedPlayerBackground,
                editorFocused ? FocusedPaneBackgroundAlpha : UnfocusedEditorBackgroundAlpha);
            SetSelectedEditorContentAlpha(editorFocused ? 1f : UnfocusedEditorContentAlpha);

            if (_playButtonCanvasGroup != null)
                _playButtonCanvasGroup.alpha = editorFocused ? 0.2f : 1f;

            foreach (var pair in _rows)
            {
                if (pair.Value == null) continue;
                bool isNotSelected = pair.Key != _selectedProfileId;
                pair.Value.SetEditorDimmed(editorFocused && isNotSelected);
            }
        }

        private void ResetMaestroNavigationStack()
        {
            NavigationGroup.RemoveFromNavigationStack(_navigationGroup);
            NavigationGroup.RemoveFromNavigationStack(_rightNavigationGroup);
        }

        private void ConfigurePaneHoverTargets()
        {
            AttachPaneHoverTarget(_playerPanelBackground, FocusProfileList);
            AttachPaneHoverTarget(_selectedPlayerBackground, FocusProfileEditor);
        }

        private static void AttachPaneHoverTarget(Image background, Action callback)
        {
            if (background == null)
                return;

            background.raycastTarget = true;
            var hoverTarget = background.GetComponent<MaestroPaneHoverTarget>() ??
                background.gameObject.AddComponent<MaestroPaneHoverTarget>();
            hoverTarget.SetCallback(callback);
        }

        private void FocusProfileList()
        {
            if (_editingPlayer)
                FinishEditingPlayer();
            else
                _navigationGroup?.PushNavGroupToStack();

            UpdateFocusVisual();
        }

        private void FocusProfileEditor()
        {
            if (!_editingPlayer)
                EnterEditorNavigation();

            SelectRememberedRightControl();
            UpdateFocusVisual();
        }

        private void CloseDropdowns()
        {
            _instrumentNavigation?.CloseDropdown();
            _difficultyNavigation?.CloseDropdown();
        }

        private void AcquireControllerLock()
        {
            ReleaseControllerLock();
            if (_controllerLockEnabled && Navigator.Instance != null)
                _controllerLock = Navigator.Instance.AcquireControllerLock();
        }

        private void ReleaseControllerLock()
        {
            _controllerLock?.Dispose();
            _controllerLock = null;
        }

        private void BuildRows()
        {
            foreach (var row in _rows.Values)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }
            _rows.Clear();

            if (_playerRowContainer == null || _playerRowPrefab == null)
                return;

            // Remove the authored Play placeholder before adding dynamic rows;
            // otherwise the vertical layout group leaves an empty row at the top.
            RepositionPlayButton();

            foreach (var player in Session.Players)
            {
                var row = Instantiate(_playerRowPrefab, _playerRowContainer);
                row.Initialize(player);
                row.Confirmed += BeginEditingPlayer;
                _rows.Add(player.ProfileId, row);
            }

            if (_playerRowContainer is RectTransform contentRect)
            {
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.anchoredPosition = Vector2.zero;
                contentRect.offsetMin = new Vector2(contentRect.offsetMin.x, 0f);
                contentRect.offsetMax = new Vector2(contentRect.offsetMax.x, 0f);
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            }

            if (_playerScroll != null)
                _playerScroll.verticalNormalizedPosition = 1f;

            if (Session.Players.FirstOrDefault() is { } first)
                SelectPlayer(first.ProfileId);
        }

        private void RepositionPlayButton()
        {
            if (_playButton == null)
                return;

            var container = _playButton.transform.parent;
            if (container == null || _playerScroll == null)
                return;

            // Target parent: the same parent as the scroll view (Body),
            // so the Play button is a sibling of the scroll, not inside it.
            var targetParent = _playerScroll.transform.parent;
            if (targetParent == null)
                return;

            if (container.parent != targetParent)
                container.SetParent(targetParent, false);

            if (container is RectTransform rt)
            {
                // Match the profile panel's 46% body-column footprint, then
                // inset Play by 10px on both sides so its focus ring stays
                // inside the panel.
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0.46f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(20f, 10f);
                rt.sizeDelta = new Vector2(-60f, 72f);
            }

            if (_playerScroll.transform is RectTransform scrollRectTransform)
            {
                // The scroll view uses a 20px bottom inset. Reserve the Play
                // row plus a 12px gap so the two rounded rectangles do not
                // overlap.
                scrollRectTransform.offsetMin = new Vector2(
                    scrollRectTransform.offsetMin.x, 94f);
            }
        }

        private void ConfigureButtons()
        {
            ConfigureButton(_modifierButton,
                () => ShowAdjustmentPicker(AdjustmentCategory.Modifiers),
                MenuData.Colors.BrightButton);
            ConfigureButton(_accessibilityButton,
                () => ShowAdjustmentPicker(AdjustmentCategory.Accessibility),
                MenuData.Colors.BrightButton);
            ConfigureButton(_sitOutButton, ToggleSittingOut, MenuData.Colors.CancelButton);
            ConfigureButton(_playButton, Continue);
        }

        private void ToggleSittingOut()
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player))
                return;

            if (player.SittingOut && Session.GetAvailableInstruments(_selectedProfileId).Count == 0)
                return;

            Session.StageSittingOut(_selectedProfileId, !player.SittingOut);
            RefreshView();
        }

        private void ConfigureDropdowns()
        {
            _instrumentNavigation = MaestroDropdownNavigatable.Attach(_instrumentDropdown);
            _difficultyNavigation = MaestroDropdownNavigatable.Attach(_difficultyDropdown);

            if (_instrumentDropdown != null)
            {
                _instrumentDropdown.onValueChanged.RemoveAllListeners();
                _instrumentDropdown.onValueChanged.AddListener(index =>
                {
                    if (index >= 0 && index < _instrumentOptions.Length)
                        Session.StageInstrument(_selectedProfileId, _instrumentOptions[index]);
                    RefreshView();
                });
            }
            if (_difficultyDropdown != null)
            {
                _difficultyDropdown.onValueChanged.RemoveAllListeners();
                _difficultyDropdown.onValueChanged.AddListener(index =>
                {
                    if (index >= 0 && index < _difficultyOptions.Length)
                        Session.StageDifficulty(_selectedProfileId, _difficultyOptions[index]);
                    RefreshView();
                });
            }
        }

        private void ConfigureInputFields()
        {
            // Clear and wire onEndEdit handlers BEFORE attaching navigatables,
            // because Attach() adds its own onEndEdit listener for scheme
            // cleanup — RemoveAllListeners must not wipe it.
            if (_noteSpeedField != null)
            {
                _noteSpeedField.onEndEdit.RemoveAllListeners();
                _noteSpeedField.onEndEdit.AddListener(_ => ChangeNoteSpeed());
            }
            if (_highwayLengthField != null)
            {
                _highwayLengthField.onEndEdit.RemoveAllListeners();
                _highwayLengthField.onEndEdit.AddListener(_ => ChangeHighwayLength());
            }
            if (_inputCalibrationField != null)
            {
                _inputCalibrationField.onEndEdit.RemoveAllListeners();
                _inputCalibrationField.onEndEdit.AddListener(_ => ChangeInputCalibration());
            }

            if (_songSpeedField != null)
            {
                _songSpeedField.onEndEdit.RemoveAllListeners();
                _songSpeedField.onEndEdit.AddListener(_ => ChangeSongSpeed());
                _songSpeedField.text = $"{Mathf.RoundToInt(GlobalVariables.State.SongSpeed * 100f)}%";
            }

            _noteSpeedNavigation = MaestroInputNavigatable.Attach(_noteSpeedField);
            _noteSpeedNavigation?.ConfigureFloat(step: 0.5f, min: 0f, max: 100f, round: 0.1f);

            _highwayLengthNavigation = MaestroInputNavigatable.Attach(_highwayLengthField);
            _highwayLengthNavigation?.ConfigureFloat(step: 0.1f, min: 0.1f, max: 10f, round: 0.1f);

            _calibrationNavigation = MaestroInputNavigatable.Attach(_inputCalibrationField);
            _calibrationNavigation?.ConfigureInteger(step: 1, min: long.MinValue, max: long.MaxValue);
        }

        private void ChangeNoteSpeed()
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player))
                return;

            if (float.TryParse(_noteSpeedField.text, NumberStyles.Float,
                CultureInfo.CurrentCulture, out var speed))
            {
                speed = Mathf.Clamp(speed, 0f, 100f);
                speed = Mathf.Round(speed / 0.1f) * 0.1f;
                Session.StageNoteSpeed(_selectedProfileId, speed);
            }

            _noteSpeedField.text = player.NoteSpeed.ToString("0.0", CultureInfo.CurrentCulture);
            RefreshView();
        }

        private void ChangeHighwayLength()
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player))
                return;

            if (float.TryParse(_highwayLengthField.text, NumberStyles.Float,
                CultureInfo.CurrentCulture, out var length))
            {
                length = Mathf.Clamp(length, 0.1f, 10f);
                length = Mathf.Round(length / 0.1f) * 0.1f;
                Session.StageHighwayLength(_selectedProfileId, length);
            }

            _highwayLengthField.text = player.HighwayLength.ToString("0.0", CultureInfo.CurrentCulture);
            RefreshView();
        }

        private void ChangeInputCalibration()
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player))
                return;

            if (long.TryParse(_inputCalibrationField.text, NumberStyles.Integer,
                CultureInfo.CurrentCulture, out var calibration))
            {
                Session.StageInputCalibration(_selectedProfileId, calibration);
            }

            _inputCalibrationField.text =
                player.InputCalibrationMilliseconds.ToString(CultureInfo.CurrentCulture);
            RefreshView();
        }

        private void ChangeSongSpeed()
        {
            if (_songSpeedField == null)
                return;

            if (!float.TryParse(_songSpeedField.text.TrimEnd('%').Trim(),
                NumberStyles.Number, CultureInfo.CurrentCulture, out var speedPercent))
            {
                speedPercent = GlobalVariables.State.SongSpeed * 100f;
            }

            int roundedPercent = Mathf.Clamp(Mathf.RoundToInt(speedPercent), 10, 5000);
            GlobalVariables.State.SongSpeed = roundedPercent / 100f;
            _songSpeedField.SetTextWithoutNotify($"{roundedPercent}%");
        }

        private static void ConfigureButton(NavigatableUnityButton button, UnityAction action,
            Color? backgroundColor = null)
        {
            if (button == null)
                return;

            var unityButton = button.GetComponent<Button>() ?? button.GetComponentInChildren<Button>(true);
            if (unityButton == null)
                return;

            unityButton.onClick.RemoveAllListeners();
            unityButton.onClick.AddListener(action);

            if (backgroundColor.HasValue)
                SetButtonVisual(button, backgroundColor.Value);
        }

        private static void SetButtonState(NavigatableUnityButton button, bool interactable,
            Color backgroundColor)
        {
            if (button == null)
                return;

            var unityButton = button.GetComponent<Button>() ?? button.GetComponentInChildren<Button>(true);
            if (unityButton != null)
                unityButton.interactable = interactable;

            SetButtonVisual(button, backgroundColor);
        }

        private static void SetButtonVisual(NavigatableUnityButton button, Color? backgroundColor)
        {
            if (!backgroundColor.HasValue)
                return;

            // NavigatableUnityButton is attached to the nested Button child;
            // the RoundButton background and selection ring live on its root.
            var visualRoot = button.transform.parent != null
                ? button.transform.parent
                : button.transform;
            foreach (var img in visualRoot.GetComponentsInChildren<Image>(true))
                img.color = backgroundColor.Value;
        }

        private static void SetButtonLabel(NavigatableUnityButton button, string label)
        {
            if (button == null)
                return;

            var textRoot = button.transform.parent != null ? button.transform.parent : button.transform;
            var text = textRoot.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            if (text != null)
                text.text = label;
        }

        private void ConfigureNavigation()
        {
            if (_navigationGroup == null)
                return;

            _navigationGroup.ClearNavigatables();
            _rightNavigationGroup?.ClearNavigatables();

            foreach (var row in _rows.Values)
                _navigationGroup.AddNavigatable(row);
            AddNavigatableIfPresent(_navigationGroup, _playButton);

            AddNavigatableIfPresent(_rightNavigationGroup, _instrumentNavigation);
            AddNavigatableIfPresent(_rightNavigationGroup, _difficultyNavigation);
            AddNavigatableIfPresent(_rightNavigationGroup, _noteSpeedNavigation);
            AddNavigatableIfPresent(_rightNavigationGroup, _highwayLengthNavigation);
            AddNavigatableIfPresent(_rightNavigationGroup, _calibrationNavigation);
            AddNavigatableIfPresent(_rightNavigationGroup, _modifierButton);
            AddNavigatableIfPresent(_rightNavigationGroup, _accessibilityButton);
            AddNavigatableIfPresent(_rightNavigationGroup, _sitOutButton);

            SetRightNavigationHoverSelection(_instrumentNavigation);
            SetRightNavigationHoverSelection(_difficultyNavigation);
            SetRightNavigationHoverSelection(_noteSpeedNavigation);
            SetRightNavigationHoverSelection(_highwayLengthNavigation);
            SetRightNavigationHoverSelection(_calibrationNavigation);
            SetRightNavigationHoverSelection(_modifierButton);
            SetRightNavigationHoverSelection(_accessibilityButton);
            SetRightNavigationHoverSelection(_sitOutButton);
            SetLeftNavigationHoverSelection(_playButton);

            ResetMaestroNavigationStack();
            _navigationGroup.PushNavGroupToStack();
            if (_playButton != null && _navigationGroup.Count > 0)
                _navigationGroup.SelectAt(_navigationGroup.Count - 1);
            else if (_navigationGroup.SelectedBehaviour == null)
                _navigationGroup.SelectFirst();
        }

        private static void AddNavigatableIfPresent(NavigationGroup group,
            NavigatableBehaviour navigatable)
        {
            if (group != null && navigatable != null)
                group.AddNavigatable(navigatable);
        }

        private static void SetRightNavigationHoverSelection(NavigatableBehaviour navigatable)
        {
            navigatable?.SetSelectOnHover(true);
        }

        private static void SetLeftNavigationHoverSelection(NavigatableBehaviour navigatable)
        {
            navigatable?.SetSelectOnHover(true);
        }

        private void OnNavigationSelectionChanged(NavigatableBehaviour selected,
            SelectionOrigin selectionOrigin)
        {
            if (selected is MaestroPlayerRow row)
            {
                if (_editingPlayer)
                    FinishEditingPlayer();
                _playButton?.SetSelected(false, SelectionOrigin.Programmatically);
                SelectPlayer(row.ProfileId);
            }
            else if (selected == _playButton && _editingPlayer)
            {
                // Play button was clicked/hovered while the right-side editor
                // is focused. Exit the editor so the click goes through on the
                // first try instead of requiring a second click.
                FinishEditingPlayer();
            }
        }

        private void OnRightNavigationSelectionChanged(NavigatableBehaviour selected,
            SelectionOrigin _)
        {
            if (selected == null)
                return;

            _lastRightSelectionIndex = _rightNavigationGroup?.SelectedIndex;

            if (!_editingPlayer)
                EnterEditorNavigation();

            UpdateFocusVisual();
        }

        private void EnterEditorNavigation()
        {
            _editingPlayer = true;
            SetEditorVisible(true);
            ResetMaestroNavigationStack();
            _navigationGroup?.PushNavGroupToStack();
            _rightNavigationGroup?.PushNavGroupToStack();
        }

        private void PushNavigationScheme()
        {
            _scheme = CreateNavigationScheme();
            Navigator.Instance?.PushScheme(_scheme);
        }

        private NavigationScheme CreateNavigationScheme()
        {
            string controllerKey = _controllerLockEnabled
                ? "Menu.Common.ControllersLocked"
                : "Menu.Common.ControllersUnlocked";
            return new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown,
                NavigationScheme.Entry.NavigateSelect,
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Back),
                new NavigationScheme.Entry(MenuAction.Yellow,
                    "Settings.Button.ShowMaestroPairingPin", ShowMaestroPin),
                new NavigationScheme.Entry(MenuAction.Blue, controllerKey,
                    ToggleControllerLock),
                new NavigationScheme.Entry(MenuAction.Orange,
                    SettingsManager.Settings.MaestroGoDirectlyToSummary.Value
                        ? "Settings.Button.SkipToMaestroOn"
                        : "Settings.Button.SkipToMaestroOff",
                    ToggleDirectSummary),
            }, false);
        }

        private void UpdateControllerLockHelpBar()
        {
            if (HelpBar.Instance != null)
                HelpBar.Instance.SetInfoFromScheme(CreateNavigationScheme());
        }

        private void SelectPlayer(Guid profileId)
        {
            if (!Session.TryGetPlayer(profileId, out _))
                return;

            _selectedProfileId = profileId;
            foreach (var pair in _rows)
                pair.Value?.SetSelected(pair.Key == profileId);
            RefreshView();
        }

        private void BeginEditingPlayer(Guid profileId)
        {
            if (!Session.TryGetPlayer(profileId, out _))
                return;

            if (_editingPlayer && _selectedProfileId == profileId)
                return;

            if (_editingPlayer)
                FinishEditingPlayer();

            SelectPlayer(profileId);
            EnterEditorNavigation();
            SelectRememberedRightControl();
            RefreshView();
        }

        private void SelectRememberedRightControl()
        {
            if (_rightNavigationGroup == null || _rightNavigationGroup.Count == 0)
                return;

            if (_rightNavigationGroup.SelectedBehaviour != null)
                return;

            int index = Mathf.Clamp(_lastRightSelectionIndex ?? 0, 0,
                _rightNavigationGroup.Count - 1);
            _rightNavigationGroup.SelectAt(index);
        }

        private void FinishEditingPlayer()
        {
            if (!_editingPlayer)
                return;

            CloseDropdowns();
            _editingPlayer = false;
            if (_rightNavigationGroup?.SelectedIndex is { } selectedIndex)
                _lastRightSelectionIndex = selectedIndex;
            _rightNavigationGroup?.ClearSelection();
            ResetMaestroNavigationStack();
            SetEditorVisible(true);
            _navigationGroup?.PushNavGroupToStack();
            RefreshView();
        }

        private void RefreshView()
        {
            if (Session == null)
                return;

            var song = GlobalVariables.State.CurrentSong;
            if (_songTitle != null && song != null)
                _songTitle.text = $"{song.Artist}\n{song.Name}";

            foreach (var staged in Session.Players)
            {
                if (_rows.TryGetValue(staged.ProfileId, out var row))
                {
                    string tierLabel = GetRowTierLabel(song, staged);
                    bool partAvailable = Session.GetAvailableInstruments(staged.ProfileId).Count > 0;
                    row.Refresh(staged, staged.ProfileId == _selectedProfileId, tierLabel,
                        partAvailable);
                }
            }

            if (!Session.TryGetPlayer(_selectedProfileId, out var selected))
                return;

            if (_selectedPlayerText != null)
            {
                string state = selected.SittingOut
                    ? "<color=#FFB636>Sitting Out</color>"
                    : $"Game Mode: {selected.GameMode.ToLocalizedName()}";
                _selectedPlayerText.text = $"{selected.Name}\n<size=18>{state}</size>";
            }

            _instrumentOptions = Session.GetAvailableInstruments(_selectedProfileId).ToArray();
            _difficultyOptions = Session.GetAvailableDifficulties(_selectedProfileId).ToArray();
            PopulateDropdown(_instrumentDropdown, _instrumentOptions, selected.Instrument,
                instrument => GetInstrumentOptionLabel(song, instrument), GetInstrumentIcon);
            PopulateDropdown(_difficultyDropdown, _difficultyOptions, selected.Difficulty,
                difficulty => difficulty.ToLocalizedName());

            // Populate numeric input fields (skip any that are actively being edited)
            bool isInstrumental = selected.GameMode is not GameMode.Vocals
                and not GameMode.PartyVocals;
            if (_noteSpeedField != null && !_noteSpeedField.isFocused)
                _noteSpeedField.text = isInstrumental
                    ? selected.NoteSpeed.ToString("0.0", CultureInfo.CurrentCulture)
                    : "N/A";
            if (_highwayLengthField != null && !_highwayLengthField.isFocused)
                _highwayLengthField.text = isInstrumental
                    ? selected.HighwayLength.ToString("0.0", CultureInfo.CurrentCulture)
                    : "N/A";
            if (_inputCalibrationField != null && !_inputCalibrationField.isFocused)
                _inputCalibrationField.text =
                    selected.InputCalibrationMilliseconds.ToString(CultureInfo.CurrentCulture);

            RefreshEditorControls(selected, _instrumentOptions.Length > 0);
            UpdateFocusVisual();
        }

        private void RefreshEditorControls(MaestroStagedPlayer selected, bool partAvailable)
        {
            bool playerActive = partAvailable && !selected.SittingOut;
            bool isInstrumental = selected.GameMode is not GameMode.Vocals
                and not GameMode.PartyVocals;
            SetDropdownInteractable(_instrumentDropdown, partAvailable);
            SetDropdownInteractable(_difficultyDropdown, playerActive);

            if (_noteSpeedField != null)
                _noteSpeedField.interactable = playerActive && isInstrumental;
            if (_highwayLengthField != null)
                _highwayLengthField.interactable = playerActive && isInstrumental;
            if (_inputCalibrationField != null)
                _inputCalibrationField.interactable = playerActive;

            SetButtonState(_modifierButton, playerActive,
                playerActive ? MenuData.Colors.BrightButton : MenuData.Colors.DeactivatedButton);
            SetButtonState(_accessibilityButton, playerActive,
                playerActive ? MenuData.Colors.BrightButton : MenuData.Colors.DeactivatedButton);

            string sitOutLabel = selected.SittingOut && partAvailable ? "JOIN" : "SIT OUT";
            Color sitOutColor = !partAvailable
                ? MenuData.Colors.DeactivatedButton
                : selected.SittingOut
                    ? MenuData.Colors.ConfirmButton
                    : MenuData.Colors.NavigationYellow;
            SetButtonLabel(_sitOutButton, sitOutLabel);
            SetButtonState(_sitOutButton, partAvailable, sitOutColor);
        }

        private static void SetDropdownInteractable(TMP_Dropdown dropdown, bool interactable)
        {
            if (dropdown != null)
                dropdown.interactable = interactable && dropdown.options.Count > 0;
        }

        private void SetSelectedEditorContentAlpha(float alpha)
        {
            if (_selectedPlayerEditor == null)
                return;

            CaptureSelectedEditorGraphicAlphas();

            foreach (var graphic in _selectedPlayerEditor.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || graphic == _selectedPlayerBackground)
                    continue;

                if (!_selectedEditorGraphicAlphas.TryGetValue(graphic, out float baseAlpha))
                    continue;

                var color = graphic.color;
                color.a = baseAlpha * alpha;
                graphic.color = color;
            }
        }

        private static void SetGraphicAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null)
                return;

            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }

        private static void PopulateDropdown<T>(TMP_Dropdown dropdown, IReadOnlyList<T> options,
            T selected, Func<T, string> getLabel, Func<T, Sprite> getImage = null)
        {
            if (dropdown == null)
                return;

            dropdown.ClearOptions();
            dropdown.AddOptions(options.Select(option =>
                new TMP_Dropdown.OptionData(getLabel(option), getImage?.Invoke(option), Color.white)).ToList());
            int index = options.ToList().IndexOf(selected);
            dropdown.SetValueWithoutNotify(Math.Max(index, 0));
            dropdown.interactable = options.Count > 0;
            dropdown.RefreshShownValue();

            // When the dropdown has no icons (e.g. difficulty), collapse the
            // caption image area so the text starts closer to the left edge
            // instead of leaving a large gap where the icon would be.
            if (getImage == null && dropdown.captionImage != null)
            {
                dropdown.captionImage.gameObject.SetActive(false);
                if (dropdown.captionText != null)
                {
                    var rt = dropdown.captionText.rectTransform;
                    rt.offsetMin = new Vector2(12, rt.offsetMin.y);
                }
            }
            else if (dropdown.captionImage != null)
            {
                dropdown.captionImage.gameObject.SetActive(true);
            }
        }

        // Match Difficulty Select's instrument option presentation, including chart tier.
        private static string GetInstrumentOptionLabel(SongEntry song, Instrument instrument)
        {
            if (instrument is Instrument.Vocals or Instrument.Harmony)
                return GetPartyVocalsChartLabel(song, instrument);

            string chartName = instrument switch
            {
                _ => instrument.ToLocalizedName(),
            };

            if (song == null)
                return chartName;

            return chartName + " - " + GetTierLabel(GetTierValues(song, instrument));
        }

        private static string GetPartyVocalsChartLabel(SongEntry song, Instrument instrument)
        {
            string chartName = instrument == Instrument.Vocals ? "Solo" : "Harmony";
            return song == null
                ? chartName
                : chartName + " - " + GetTierLabel(GetTierValues(song, instrument));
        }

        private static string GetRowTierLabel(SongEntry song, MaestroStagedPlayer player)
        {
            if (song == null || player.SittingOut) return null;
            var values = GetTierValues(song, player.Instrument);
            return GetTierLabel(values);
        }

        private static PartValues GetTierValues(SongEntry song, Instrument instrument)
        {
            var values = song[instrument];
            if ((instrument is Instrument.Harmony or Instrument.PartyVocals) && !values.IsActive())
                values = song[Instrument.Vocals];
            // 5-lane drums are usually a down-converted version of pro drums,
            // and vice versa. Fall back so the tier is always shown.
            if (!values.IsActive())
            {
                values = instrument switch
                {
                    Instrument.FiveLaneDrums => song[Instrument.ProDrums],
                    Instrument.ProDrums or Instrument.FourLaneDrums => song[Instrument.FiveLaneDrums],
                    _ => values,
                };
            }
            return values;
        }

        private static string GetTierLabel(PartValues values)
        {
            if (!values.IsActive() || values.Intensity < 0)
                return "? - " + Localize.Key("Menu.Filters.Intensities.Unknown");

            string text = $"{values.Intensity} - {FiltersMenu.GetIntensityLabelByIndex(values.Intensity)}";
            return values.Intensity switch
            {
                >= 6 => $"<color=#FB443F>{text}</color>",
                5 => $"<color=#FF8400>{text}</color>",
                _ => text,
            };
        }

        private static Sprite GetInstrumentIcon(Instrument instrument)
        {
            string resourceName = instrument.ToResourceName();
            if (string.IsNullOrEmpty(resourceName))
                return null;

            string assetKey = $"InstrumentIcons[{resourceName}]";
            if (!InstrumentIconCache.TryGetValue(assetKey, out var icon))
            {
                icon = Addressables.LoadAssetAsync<Sprite>(assetKey).WaitForCompletion();
                InstrumentIconCache[assetKey] = icon;
            }

            return icon;
        }

        private void ShowAdjustmentPicker(AdjustmentCategory category)
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player) ||
                DialogManager.Instance == null || DialogManager.Instance.IsDialogShowing)
                return;

            int rightSelectionIndex = _rightNavigationGroup?.SelectedIndex ?? 0;

            bool accessibility = category == AdjustmentCategory.Accessibility;
            var modifiers = accessibility
                ? Session.GetAvailableAccessibilityModifiers(_selectedProfileId)
                : GetModifierOptions(player);
            bool hasAccessibilityOptions = accessibility && HasAccessibilityOptions(player);
            if (modifiers.Count == 0 && !hasAccessibilityOptions)
            {
                string title = accessibility ? "Accessibility" : "Modifiers";
                DialogManager.Instance.ShowMessage(title,
                    $"No {title.ToLowerInvariant()} are available for this instrument.");
                RestoreEditorNavigationAfterDialog(rightSelectionIndex);
                return;
            }

            if (_modifierItemPrefab == null)
            {
                DialogManager.Instance.ShowMessage("Modifiers",
                    "Modifier controls are unavailable for this menu.");
                RestoreEditorNavigationAfterDialog(rightSelectionIndex);
                return;
            }

            string dialogTitle = accessibility ? "Accessibility" : "Modifiers";
            var dialog = DialogManager.Instance.ShowList($"{dialogTitle} — {player.Name}");
            dialog.ClearButtons();
            dialog.ClearList();

            if (accessibility)
            {
                AddAccessibilityOptions(dialog, player, modifiers, rightSelectionIndex);
            }
            else
            {
                foreach (var modifier in modifiers)
                {
                    AddModifierToggle(dialog, player, modifier);
                }
            }

            var doneButton = dialog.AddDialogButton("Menu.DifficultySelect.Done",
                MenuData.Colors.BrightButton,
                () =>
                {
                    DialogManager.Instance.ClearDialog();
                    RestoreEditorNavigationAfterDialog(rightSelectionIndex);
                });
            // Enable hover-to-select on the Done button so mouse users get the
            // same focus behaviour as the list entries above it.
            doneButton.GetComponentInChildren<NavigatableBehaviour>()?.SetSelectOnHover(true);
            RestoreEditorNavigationAfterDialog(rightSelectionIndex);
            dialog.SelectLast();
        }

        private void RestoreEditorNavigationAfterDialog(int rightSelectionIndex)
        {
            if (_adjustmentFocusRestoreCoroutine != null)
                StopCoroutine(_adjustmentFocusRestoreCoroutine);

            _adjustmentFocusRestoreCoroutine = StartCoroutine(
                RestoreEditorNavigationAfterDialogCoroutine(rightSelectionIndex));
        }

        private System.Collections.IEnumerator RestoreEditorNavigationAfterDialogCoroutine(
            int rightSelectionIndex)
        {
            yield return new WaitUntil(() => DialogManager.Instance == null ||
                !DialogManager.Instance.IsDialogShowing);
            _adjustmentFocusRestoreCoroutine = null;

            if (!isActiveAndEnabled || Session == null || _rightNavigationGroup == null ||
                _rightNavigationGroup.Count == 0)
                yield break;

            _editingPlayer = true;
            _lastRightSelectionIndex = rightSelectionIndex;
            SetEditorVisible(true);
            ResetMaestroNavigationStack();
            _navigationGroup?.PushNavGroupToStack();
            _rightNavigationGroup.PushNavGroupToStack();
            _rightNavigationGroup.ClearSelection();
            _rightNavigationGroup.SelectAt(Mathf.Clamp(rightSelectionIndex, 0,
                _rightNavigationGroup.Count - 1));
            UpdateFocusVisual();
        }

        private IReadOnlyList<Modifier> GetModifierOptions(MaestroStagedPlayer player)
        {
            var available = Session.GetAvailableModifiers(_selectedProfileId, true);
            if (player.GameMode is not GameMode.Vocals and not GameMode.PartyVocals)
                return available.Where(modifier =>
                    !MaestroSelectionRules.IsAccessibilityModifier(modifier)).ToArray();

            return GetVocalModifierOptions(available);
        }

        private static IReadOnlyList<Modifier> GetVocalModifierOptions(
            IReadOnlyList<Modifier> available)
        {
            var options = new List<Modifier>();
            Modifier[] unpitched =
            {
                Modifier.UnpitchedOnly,
                Modifier.UnpitchedHarm2,
                Modifier.UnpitchedHarm3,
            };

            foreach (var modifier in unpitched)
            {
                if (available.Contains(modifier))
                    options.Add(modifier);
            }

            foreach (var modifier in available)
            {
                if (!options.Contains(modifier))
                    options.Add(modifier);
            }

            return options;
        }

        private void AddModifierToggle(ListDialog dialog, MaestroStagedPlayer player,
            Modifier modifier)
        {
            AddAdjustmentToggle(dialog, modifier.ToLocalizedName(),
                (player.Modifiers & modifier) != 0, enabled =>
            {
                Session.StageModifier(_selectedProfileId, modifier, enabled);
                RefreshView();
            });
        }

        private void AddAccessibilityOptions(ListDialog dialog, MaestroStagedPlayer player,
            IReadOnlyList<Modifier> modifiers, int rightSelectionIndex)
        {
            if (MaestroSelectionRules.SupportsLeftyFlip(player.GameMode))
            {
                AddAdjustmentToggle(dialog, Localize.Key("Menu.DifficultySelect", "LeftyFlip"),
                    player.LeftyFlip, enabled =>
                    {
                        Session.StageLeftyFlip(_selectedProfileId, enabled);
                        RefreshView();
                    });
            }

            if (MaestroSelectionRules.SupportsRangeShifts(player.GameMode))
            {
                AddAdjustmentToggle(dialog,
                    Localize.Key("Menu.DifficultySelect", "NoRangeShifts"),
                    MaestroSelectionRules.HasNoRangeShifts(player), enabled =>
                    {
                        Session.StageRangeEnabled(_selectedProfileId, !enabled);
                        if (Session.GetAvailableModifiers(_selectedProfileId, true)
                            .Contains(Modifier.RangeCompress))
                        {
                            Session.StageModifier(_selectedProfileId, Modifier.RangeCompress, enabled);
                        }
                        RefreshView();
                    });
            }

            foreach (var modifier in modifiers)
            {
                if (modifier == Modifier.RangeCompress)
                    continue;

                AddModifierToggle(dialog, player, modifier);
            }

            if (player.GameMode == GameMode.ProKeys)
            {
                // The three-state OpenLaneDisplayType gets its own sub-picker
                // (matching Difficulty Select) rather than a cycling toggle.
                AddAdjustmentToggle(dialog,
                    GetOpenLaneLabel(player.OpenLaneDisplayType),
                    false, _ =>
                    {
                        DialogManager.Instance.ClearDialog();
                        ShowOpenLanePicker(rightSelectionIndex);
                    });
            }
        }

        private void ShowOpenLanePicker(int rightSelectionIndex)
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player) ||
                DialogManager.Instance == null)
                return;

            string title = Localize.Key("Menu.DifficultySelect", "DedicatedOpenLane");
            var dialog = DialogManager.Instance.ShowList($"{title} — {player.Name}");
            dialog.ClearButtons();
            dialog.ClearList();

            foreach (var option in OpenLaneOptions)
            {
                var capture = option;
                bool selected = player.OpenLaneDisplayType == option;
                AddAdjustmentToggle(dialog, capture.ToLocalizedName(), selected, _ =>
                {
                    Session.StageOpenLaneDisplayType(_selectedProfileId, capture);
                    RefreshView();
                    DialogManager.Instance.ClearDialog();
                    ShowAdjustmentPicker(AdjustmentCategory.Accessibility);
                });
            }

            var doneButton = dialog.AddDialogButton("Menu.DifficultySelect.Done",
                MenuData.Colors.BrightButton,
                () =>
                {
                    DialogManager.Instance.ClearDialog();
                    RestoreEditorNavigationAfterDialog(rightSelectionIndex);
                });
            doneButton.GetComponentInChildren<NavigatableBehaviour>()?.SetSelectOnHover(true);
            dialog.SelectLast();
        }

        private ModifierItem AddAdjustmentToggle(ListDialog dialog, string label, bool active,
            Action<bool> onChanged)
        {
            var item = dialog.AddListEntry(_modifierItemPrefab, true);
            item.Initialize(label, active, onChanged);
            return item;
        }

        private bool HasAccessibilityOptions(MaestroStagedPlayer player)
        {
            return MaestroSelectionRules.SupportsLeftyFlip(player.GameMode) ||
                MaestroSelectionRules.SupportsRangeShifts(player.GameMode) ||
                player.GameMode == GameMode.ProKeys ||
                Session.GetAvailableAccessibilityModifiers(_selectedProfileId).Count > 0;
        }

        private static string GetOpenLaneLabel(OpenLaneDisplayType displayType) =>
            Localize.Key("Menu.DifficultySelect", "DedicatedOpenLane") + ": " +
            displayType.ToLocalizedName();

        private void ToggleControllerLock()
        {
            _controllerLockEnabled = !_controllerLockEnabled;
            AcquireControllerLock();
            RefreshView();
            UpdateControllerLockHelpBar();
        }

        private void ToggleDirectSummary()
        {
            SettingsManager.Settings.MaestroGoDirectlyToSummary.Value =
                !SettingsManager.Settings.MaestroGoDirectlyToSummary.Value;
            UpdateControllerLockHelpBar();
        }

        private static void ShowMaestroPin()
        {
            SettingsManager.Settings.ShowMaestroPairingPin();
        }

        private void Back()
        {
            if (_leaving || Session == null)
                return;

            if (_editingPlayer)
            {
                FinishEditingPlayer();
                return;
            }

            _leaving = true;
            Session.MarkReturningToDifficultySelect();
            CloseDropdowns();
            ReleaseControllerLock();
            if (_scheme != null && Navigator.Instance != null)
                Navigator.Instance.RemoveScheme(_scheme);
            _scheme = null;
            MenuManager.Instance.PopMenu();
        }

        private void BackToSongSelect()
        {
            if (_leaving || Session == null)
                return;

            _leaving = true;
            CloseDropdowns();
            ReleaseControllerLock();
            if (_scheme != null && Navigator.Instance != null)
                Navigator.Instance.RemoveScheme(_scheme);
            _scheme = null;
            MaestroSetupSession.ClearActive();

            if (!MenuManager.Instance.PopToMenu(MenuManager.Menu.MusicLibrary))
            {
                _leaving = false;
                Back();
            }
        }

        private void Continue()
        {
            if (_leaving || Session == null)
                return;

            // Continue() is only reachable from a mouse click on the Play button
            // (keyboard Green calls Confirm() on the current group's selection).
            // If the editor is focused, exit it and fall through to commit —
            // don't eat the click and force a second one. (The Play button's
            // Selected state is already true from initial focus, so
            // OnPointerDown's SetSelected is a no-op and OnNavigationSelectionChanged
            // never fires to trigger FinishEditingPlayer via that path.)
            if (_editingPlayer)
                FinishEditingPlayer();

            var result = Session.TryCommit();
            if (!result.Success)
            {
                if (_errorText != null)
                    _errorText.text = result.GlobalError ?? string.Join("\n", result.PlayerErrors.Values);
                return;
            }

            _leaving = true;
            CloseDropdowns();
            ReleaseControllerLock();
            if (_scheme != null && Navigator.Instance != null)
                Navigator.Instance.RemoveScheme(_scheme);
            _scheme = null;
            MaestroSetupSession.ClearActive();
            GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);
        }
    }
}
