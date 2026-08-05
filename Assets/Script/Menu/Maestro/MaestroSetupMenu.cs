// pattern: Imperative Shell

using System;
using System.Collections.Generic;
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
using YARG.Menu.Data;
using YARG.Menu.DifficultySelect;
using YARG.Menu.Filters;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;

namespace YARG.Menu.Maestro
{
    /// <summary>
    /// In-game Maestro finalization page. All edits are staged in <see cref="MaestroSetupSession"/>
    /// until Continue validates and commits the complete active-player set.
    /// </summary>
    public sealed class MaestroSetupMenu : MonoBehaviour
    {
        private static readonly Dictionary<string, Sprite> InstrumentIconCache = new();

        [Header("Page")]
        [SerializeField] private TMP_Text _songTitle;
        [SerializeField] private TMP_Text _errorText;
        [SerializeField] private TMP_Text _controllerLockText;
        [SerializeField] private Transform _playerRowContainer;
        [SerializeField] private MaestroPlayerRow _playerRowPrefab;
        [SerializeField] private ScrollRect _playerScroll;
        [SerializeField] private NavigationGroup _navigationGroup;

        [Header("Selected player editor")]
        [SerializeField] private GameObject _selectedPlayerEditor;
        [SerializeField] private NavigationGroup _rightNavigationGroup;
        [SerializeField] private TMP_Text _selectedPlayerText;
        [SerializeField] private TMP_Dropdown _instrumentDropdown;
        [SerializeField] private TMP_Dropdown _difficultyDropdown;
        [SerializeField] private ModifierItem _modifierItemPrefab;
        [SerializeField] private NavigatableUnityButton _modifierButton;
        [SerializeField] private NavigatableUnityButton _readyButton;
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
        private CanvasGroup _playerPanelCanvasGroup;
        private CanvasGroup _selectedPlayerCanvasGroup;

        private MaestroSetupSession Session => MaestroSetupSession.Active;

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
            SetEditorVisible(true);
            EnsureFocusCanvasGroups();
            AcquireControllerLock();
            if (_navigationGroup != null)
                _navigationGroup.SelectionChanged += OnNavigationSelectionChanged;
            if (_rightNavigationGroup != null)
                _rightNavigationGroup.SelectionChanged += OnRightNavigationSelectionChanged;

            BuildRows();
            ConfigureDropdowns();
            ConfigureButtons();
            ConfigureNavigation();
            PushNavigationScheme();
            RefreshView();
        }

        private void OnDisable()
        {
            CloseDropdowns();
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

        private void SetEditorVisible(bool visible)
        {
            if (_selectedPlayerEditor != null)
                _selectedPlayerEditor.SetActive(visible);
        }

        private void EnsureFocusCanvasGroups()
        {
            if (_playerScroll != null)
                _playerPanelCanvasGroup = _playerScroll.GetComponent<CanvasGroup>() ??
                    _playerScroll.gameObject.AddComponent<CanvasGroup>();
            if (_selectedPlayerEditor != null)
                _selectedPlayerCanvasGroup = _selectedPlayerEditor.GetComponent<CanvasGroup>() ??
                    _selectedPlayerEditor.AddComponent<CanvasGroup>();
        }

        private void UpdateFocusVisual()
        {
            EnsureFocusCanvasGroups();
            bool editorFocused = _editingPlayer;
            if (_playerPanelCanvasGroup != null)
                _playerPanelCanvasGroup.alpha = editorFocused ? 0.55f : 1f;
            if (_selectedPlayerCanvasGroup != null)
                _selectedPlayerCanvasGroup.alpha = editorFocused ? 1f : 0.55f;
        }

        private void ResetMaestroNavigationStack()
        {
            NavigationGroup.RemoveFromNavigationStack(_navigationGroup);
            NavigationGroup.RemoveFromNavigationStack(_rightNavigationGroup);
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

            foreach (var player in Session.Players)
            {
                var row = Instantiate(_playerRowPrefab, _playerRowContainer);
                row.Initialize(player);
                row.Confirmed += BeginEditingPlayer;
                _rows.Add(player.ProfileId, row);
            }

            _playButton?.transform.parent?.SetAsLastSibling();

            if (Session.Players.FirstOrDefault() is { } first)
                SelectPlayer(first.ProfileId);
        }

        private void ConfigureButtons()
        {
            ConfigureButton(_modifierButton, ShowModifierPicker);
            ConfigureButton(_readyButton, FinishEditingPlayer);
            ConfigureButton(_playButton, Continue);
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

        private static void ConfigureButton(NavigatableUnityButton button, UnityAction action)
        {
            if (button == null)
                return;

            var unityButton = button.GetComponent<Button>() ?? button.GetComponentInChildren<Button>(true);
            if (unityButton == null)
                return;

            unityButton.onClick.RemoveAllListeners();
            unityButton.onClick.AddListener(action);
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
            AddNavigatableIfPresent(_rightNavigationGroup, _modifierButton);
            AddNavigatableIfPresent(_rightNavigationGroup, _readyButton);

            ResetMaestroNavigationStack();
            _navigationGroup.PushNavGroupToStack();
            if (_navigationGroup.SelectedBehaviour == null)
                _navigationGroup.SelectFirst();
        }

        private static void AddNavigatableIfPresent(NavigationGroup group,
            NavigatableBehaviour navigatable)
        {
            if (group != null && navigatable != null)
                group.AddNavigatable(navigatable);
        }

        private void OnNavigationSelectionChanged(NavigatableBehaviour selected,
            SelectionOrigin selectionOrigin)
        {
            if (selected is MaestroPlayerRow row)
            {
                if (selectionOrigin == SelectionOrigin.Mouse && _editingPlayer)
                    FinishEditingPlayer();
                SelectPlayer(row.ProfileId);
            }
        }

        private void OnRightNavigationSelectionChanged(NavigatableBehaviour selected,
            SelectionOrigin selectionOrigin)
        {
            if (selected == null || selectionOrigin != SelectionOrigin.Mouse || _editingPlayer)
                return;

            _editingPlayer = true;
            SetEditorVisible(true);
            ResetMaestroNavigationStack();
            _navigationGroup?.PushNavGroupToStack();
            _rightNavigationGroup?.PushNavGroupToStack();
            UpdateFocusVisual();
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
                new NavigationScheme.Entry(MenuAction.Blue, controllerKey,
                    ToggleControllerLock),
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
            _editingPlayer = true;
            SetEditorVisible(true);
            ResetMaestroNavigationStack();
            _navigationGroup?.PushNavGroupToStack();
            _rightNavigationGroup?.PushNavGroupToStack();
            _rightNavigationGroup?.SelectFirst();
            RefreshView();
        }

        private void FinishEditingPlayer()
        {
            if (!_editingPlayer)
                return;

            CloseDropdowns();
            _editingPlayer = false;
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
            if (_controllerLockText != null)
                _controllerLockText.text = _controllerLockEnabled
                    ? "Controller Navigation Disabled"
                    : "Controller Navigation Enabled";

            foreach (var staged in Session.Players)
            {
                if (_rows.TryGetValue(staged.ProfileId, out var row))
                    row.Refresh(staged, staged.ProfileId == _selectedProfileId);
            }

            if (!Session.TryGetPlayer(_selectedProfileId, out var selected))
                return;

            if (_selectedPlayerText != null)
                _selectedPlayerText.text =
                    $"{selected.Name}\n<size=18>Game Mode: {selected.GameMode.ToLocalizedName()}</size>";

            _instrumentOptions = Session.GetAvailableInstruments(_selectedProfileId).ToArray();
            _difficultyOptions = Session.GetAvailableDifficulties(_selectedProfileId).ToArray();
            PopulateDropdown(_instrumentDropdown, _instrumentOptions, selected.Instrument,
                instrument => GetInstrumentOptionLabel(song, instrument), GetInstrumentIcon);
            PopulateDropdown(_difficultyDropdown, _difficultyOptions, selected.Difficulty,
                difficulty => difficulty.ToLocalizedName());
            UpdateFocusVisual();
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

        private static PartValues GetTierValues(SongEntry song, Instrument instrument)
        {
            var values = song[instrument];
            if ((instrument is Instrument.Harmony or Instrument.PartyVocals) && !values.IsActive())
                values = song[Instrument.Vocals];
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

        private void ShowModifierPicker()
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player) ||
                DialogManager.Instance == null || DialogManager.Instance.IsDialogShowing)
                return;

            var modifiers = Session.GetAvailableModifiers(_selectedProfileId);
            if (modifiers.Count == 0)
            {
                DialogManager.Instance.ShowMessage("Modifiers",
                    "No modifiers are available for this instrument.");
                return;
            }

            if (_modifierItemPrefab == null)
            {
                DialogManager.Instance.ShowMessage("Modifiers",
                    "Modifier controls are unavailable for this menu.");
                return;
            }

            var dialog = DialogManager.Instance.ShowList($"Modifiers — {player.Name}");
            dialog.ClearButtons();
            dialog.ClearList();
            foreach (var modifier in modifiers)
            {
                var option = modifier;
                var item = dialog.AddListEntry(_modifierItemPrefab, true);
                item.Initialize(option.ToLocalizedName(), (player.Modifiers & option) != 0,
                    enabled =>
                    {
                        Session.StageModifier(_selectedProfileId, option, enabled);
                        RefreshView();
                    });
            }
            dialog.AddDialogButton("Menu.Common.Close", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);
            dialog.AddDialogButton("Menu.Common.Confirm", MenuData.Colors.ConfirmButton,
                DialogManager.Instance.ClearDialog);
        }

        private void ToggleControllerLock()
        {
            _controllerLockEnabled = !_controllerLockEnabled;
            AcquireControllerLock();
            RefreshView();
            UpdateControllerLockHelpBar();
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

        private void Continue()
        {
            if (_leaving || Session == null)
                return;

            if (_editingPlayer)
            {
                FinishEditingPlayer();
                return;
            }

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
