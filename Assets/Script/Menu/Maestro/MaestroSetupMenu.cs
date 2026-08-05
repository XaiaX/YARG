// pattern: Imperative Shell

using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Localization;
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
        [Header("Page")]
        [SerializeField] private TMP_Text _songTitle;
        [SerializeField] private TMP_Text _errorText;
        [SerializeField] private TMP_Text _controllerLockText;
        [SerializeField] private Transform _playerRowContainer;
        [SerializeField] private MaestroPlayerRow _playerRowPrefab;
        [SerializeField] private ScrollRect _playerScroll;
        [SerializeField] private NavigationGroup _navigationGroup;

        [Header("Selected player editor")]
        [SerializeField] private TMP_Text _selectedPlayerText;
        [SerializeField] private TMP_Dropdown _gameModeDropdown;
        [SerializeField] private TMP_Dropdown _instrumentDropdown;
        [SerializeField] private TMP_Dropdown _difficultyDropdown;
        [SerializeField] private NavigatableUnityButton _modifierButton;
        [SerializeField] private NavigatableUnityButton _controllerLockButton;

        private readonly Dictionary<Guid, MaestroPlayerRow> _rows = new();
        private Guid _selectedProfileId;
        private IDisposable _controllerLock;
        private NavigationScheme _scheme;
        private bool _leaving;
        private bool _controllerLockEnabled = true;
        private bool _staticNavigationConfigured;
        private GameMode[] _gameModeOptions = Array.Empty<GameMode>();
        private Instrument[] _instrumentOptions = Array.Empty<Instrument>();
        private Difficulty[] _difficultyOptions = Array.Empty<Difficulty>();
        private MaestroDropdownNavigatable _gameModeNavigation;
        private MaestroDropdownNavigatable _instrumentNavigation;
        private MaestroDropdownNavigatable _difficultyNavigation;

        private MaestroSetupSession Session => MaestroSetupSession.Active;

        private void OnEnable()
        {
            if (Session == null)
            {
                gameObject.SetActive(false);
                return;
            }

            _leaving = false;
            _controllerLockEnabled = true;
            AcquireControllerLock();
            if (_navigationGroup != null)
                _navigationGroup.SelectionChanged += OnNavigationSelectionChanged;
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
            ReleaseControllerLock();
            if (_scheme != null && Navigator.Instance != null)
                Navigator.Instance.RemoveScheme(_scheme);
            _scheme = null;
        }

        private void CloseDropdowns()
        {
            _gameModeNavigation?.CloseDropdown();
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
                row.Clicked += SelectPlayer;
                _rows.Add(player.ProfileId, row);
            }

            if (Session.Players.FirstOrDefault() is { } first)
                SelectPlayer(first.ProfileId);
        }

        private void ConfigureButtons()
        {
            ConfigureButton(_modifierButton, ShowModifierPicker);
            ConfigureButton(_controllerLockButton, ToggleControllerLock);
        }

        private void ConfigureDropdowns()
        {
            _gameModeNavigation = MaestroDropdownNavigatable.Attach(_gameModeDropdown);
            _instrumentNavigation = MaestroDropdownNavigatable.Attach(_instrumentDropdown);
            _difficultyNavigation = MaestroDropdownNavigatable.Attach(_difficultyDropdown);

            if (_gameModeDropdown != null)
            {
                _gameModeDropdown.onValueChanged.RemoveAllListeners();
                _gameModeDropdown.onValueChanged.AddListener(index =>
                {
                    if (index >= 0 && index < _gameModeOptions.Length)
                        Session.StageGameMode(_selectedProfileId, _gameModeOptions[index]);
                    RefreshView();
                });
            }
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

            var unityButton = button.GetComponent<Button>();
            if (unityButton == null)
                return;

            unityButton.onClick.RemoveAllListeners();
            unityButton.onClick.AddListener(action);
        }

        private void ConfigureNavigation()
        {
            if (_navigationGroup == null)
                return;

            if (!_staticNavigationConfigured)
            {
                AddNavigatableIfPresent(_gameModeNavigation);
                AddNavigatableIfPresent(_instrumentNavigation);
                AddNavigatableIfPresent(_difficultyNavigation);
                AddNavigatableIfPresent(_modifierButton);
                AddNavigatableIfPresent(_controllerLockButton);
                _staticNavigationConfigured = true;
            }

            foreach (var row in _rows.Values)
                _navigationGroup.AddNavigatable(row);

            if (_navigationGroup.SelectedBehaviour == null)
                _navigationGroup.SelectFirst();
        }

        private void AddNavigatableIfPresent(NavigatableBehaviour navigatable)
        {
            if (navigatable != null)
                _navigationGroup.AddNavigatable(navigatable);
        }

        private void OnNavigationSelectionChanged(NavigatableBehaviour selected,
            SelectionOrigin selectionOrigin)
        {
            if (selected is MaestroPlayerRow row)
                SelectPlayer(row.ProfileId);
        }

        private void PushNavigationScheme()
        {
            _scheme = new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown,
                NavigationScheme.Entry.NavigateSelect,
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Back),
            }, false);
            Navigator.Instance.PushScheme(_scheme);
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

        private void RefreshView()
        {
            if (Session == null)
                return;

            if (_songTitle != null && GlobalVariables.State.CurrentSong != null)
                _songTitle.text = GlobalVariables.State.CurrentSong.Name;
            if (_controllerLockText != null)
                _controllerLockText.text = _controllerLockEnabled
                    ? "Controller navigation locked"
                    : "Controller navigation enabled";

            foreach (var staged in Session.Players)
            {
                if (_rows.TryGetValue(staged.ProfileId, out var row))
                    row.Refresh(staged, staged.ProfileId == _selectedProfileId);
            }

            if (!Session.TryGetPlayer(_selectedProfileId, out var selected))
                return;

            if (_selectedPlayerText != null)
                _selectedPlayerText.text = selected.Name;

            _gameModeOptions = Session.GetAvailableGameModes().ToArray();
            _instrumentOptions = Session.GetAvailableInstruments(_selectedProfileId).ToArray();
            _difficultyOptions = Session.GetAvailableDifficulties(_selectedProfileId).ToArray();
            PopulateDropdown(_gameModeDropdown, _gameModeOptions, selected.GameMode,
                mode => mode.ToLocalizedName());
            PopulateDropdown(_instrumentDropdown, _instrumentOptions, selected.Instrument,
                instrument => instrument.ToLocalizedName());
            PopulateDropdown(_difficultyDropdown, _difficultyOptions, selected.Difficulty,
                difficulty => difficulty.ToLocalizedName());
        }

        private static void PopulateDropdown<T>(TMP_Dropdown dropdown, IReadOnlyList<T> options,
            T selected, Func<T, string> getLabel)
        {
            if (dropdown == null)
                return;

            dropdown.ClearOptions();
            dropdown.AddOptions(options.Select(option => new TMP_Dropdown.OptionData(getLabel(option))).ToList());
            int index = options.ToList().IndexOf(selected);
            dropdown.SetValueWithoutNotify(Math.Max(index, 0));
            dropdown.interactable = options.Count > 0;
            dropdown.RefreshShownValue();
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

            var dialog = DialogManager.Instance.ShowList($"Modifiers — {player.Name}");
            dialog.ClearButtons();
            var buttons = new Dictionary<Modifier, YARG.Menu.ColoredButton>();
            foreach (var modifier in modifiers)
            {
                var option = modifier;
                YARG.Menu.ColoredButton button = null;
                button = dialog.AddListButton(GetModifierPickerLabel(player.Modifiers, option), () =>
                {
                    if (!Session.TryGetPlayer(_selectedProfileId, out var current))
                        return;

                    bool enabled = (current.Modifiers & option) == 0;
                    Session.StageModifier(_selectedProfileId, option, enabled);
                    foreach (var pair in buttons)
                        pair.Value.Text.text = GetModifierPickerLabel(current.Modifiers, pair.Key);
                    RefreshView();
                }, false);
                buttons.Add(option, button);
            }
            dialog.AddDialogButton("Menu.Common.Confirm", DialogManager.Instance.ClearDialog);
        }

        private static string GetModifierPickerLabel(Modifier selected, Modifier option)
        {
            string marker = (selected & option) != 0 ? "☑" : "☐";
            return $"{marker} {option.ToLocalizedName()}";
        }

        private void ToggleControllerLock()
        {
            _controllerLockEnabled = !_controllerLockEnabled;
            AcquireControllerLock();
            RefreshView();
        }

        private void Back()
        {
            if (_leaving || Session == null) return;
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
            if (_leaving || Session == null) return;
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
