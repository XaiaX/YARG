using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Menu.Navigation;
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
        [SerializeField] private TMP_Text _gameModeText;
        [SerializeField] private TMP_Text _instrumentText;
        [SerializeField] private TMP_Text _difficultyText;
        [SerializeField] private TMP_Text _modifierText;
        [SerializeField] private NavigatableButton _gameModeButton;
        [SerializeField] private NavigatableButton _instrumentButton;
        [SerializeField] private NavigatableButton _difficultyButton;
        [SerializeField] private NavigatableButton _modifierButton;
        [SerializeField] private NavigatableButton _controllerLockButton;
        [SerializeField] private NavigatableButton _backButton;
        [SerializeField] private NavigatableButton _continueButton;

        private readonly Dictionary<Guid, MaestroPlayerRow> _rows = new();
        private Guid _selectedProfileId;
        private IDisposable _controllerLock;
        private NavigationScheme _scheme;
        private bool _leaving;
        private bool _controllerLockEnabled = true;

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
            BuildRows();
            ConfigureButtons();
            PushNavigationScheme();
            RefreshView();
        }

        private void OnDisable()
        {
            ReleaseControllerLock();
            if (_scheme != null && Navigator.Instance != null &&
                Navigator.Instance.IsTopScheme(_scheme))
            {
                // MenuManager deactivation normally follows PopMenu, but scene transitions
                // can disable this object directly. Pop only when this page still owns top.
                Navigator.Instance.PopScheme();
            }
            _scheme = null;
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
            _gameModeButton?.SetOnClickEvent(CycleGameMode);
            _instrumentButton?.SetOnClickEvent(CycleInstrument);
            _difficultyButton?.SetOnClickEvent(CycleDifficulty);
            _modifierButton?.SetOnClickEvent(CycleModifier);
            _controllerLockButton?.SetOnClickEvent(ToggleControllerLock);
            _backButton?.SetOnClickEvent(Back);
            _continueButton?.SetOnClickEvent(Continue);
        }

        private void PushNavigationScheme()
        {
            _scheme = new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown,
                NavigationScheme.Entry.NavigateSelect,
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Back),
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm", Continue),
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
            if (_gameModeText != null)
                _gameModeText.text = $"Game mode: {selected.GameMode}";
            if (_instrumentText != null)
                _instrumentText.text = $"Instrument: {selected.Instrument}";
            if (_difficultyText != null)
                _difficultyText.text = $"Difficulty: {selected.Difficulty}";
            if (_modifierText != null)
                _modifierText.text = $"Modifiers: {selected.Modifiers}";
        }

        private void CycleGameMode()
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player)) return;
            var values = EnumExtensions<GameMode>.Values.ToArray();
            int index = Array.IndexOf(values, player.GameMode);
            Session.StageGameMode(_selectedProfileId, values[(index + 1) % values.Length]);
            RefreshView();
        }

        private void CycleInstrument()
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player)) return;
            var values = player.GameMode.PossibleInstrumentsForSong(GlobalVariables.State.CurrentSong);
            if (values == null || values.Length == 0) return;
            int index = Array.IndexOf(values, player.Instrument);
            if (index < 0) index = -1;
            Session.StageInstrument(_selectedProfileId, values[(index + 1) % values.Length]);
            RefreshView();
        }

        private void CycleDifficulty()
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player)) return;
            var values = EnumExtensions<Difficulty>.Values.ToArray();
            int index = Array.IndexOf(values, player.Difficulty);
            Session.StageDifficulty(_selectedProfileId, values[(index + 1) % values.Length]);
            RefreshView();
        }

        private void CycleModifier()
        {
            if (!Session.TryGetPlayer(_selectedProfileId, out var player)) return;
            var (possible, _) = player.GameMode.PossibleModifiers(player.Instrument);
            var values = EnumExtensions<Modifier>.Values
                .Where(modifier => modifier != Modifier.None && (possible & modifier) != 0)
                .ToArray();
            if (values.Length == 0) return;
            Modifier next = values.FirstOrDefault(modifier => (player.Modifiers & modifier) == 0);
            if (next == Modifier.None)
                next = values[0];
            else
                next |= player.Modifiers;
            Session.StageModifiers(_selectedProfileId, next);
            RefreshView();
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
            ReleaseControllerLock();
            if (_scheme != null && Navigator.Instance != null &&
                Navigator.Instance.IsTopScheme(_scheme))
            {
                Navigator.Instance.PopScheme();
                _scheme = null;
            }
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
            ReleaseControllerLock();
            if (_scheme != null && Navigator.Instance != null &&
                Navigator.Instance.IsTopScheme(_scheme))
            {
                Navigator.Instance.PopScheme();
            }
            _scheme = null;
            MaestroSetupSession.ClearActive();
            GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);
        }
    }
}
