// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;
using YARG.Input.Bindings;
using YARG.Menu.Navigation;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Data;
using YARG.Menu.Filters;
using YARG.Menu.Persistent;
using YARG.Menu.ProfileInfo;
using YARG.Player;
using YARG.Scores;
using YARG.Settings.Customization;

namespace YARG.Menu.ProfileList
{
    // This will be cleaned up when we add the new profile overview screen

    public class ProfileSidebar : MonoBehaviour
    {
        private const string NUMBER_FORMAT = "0.0###";

        private static readonly GameMode[] _gameModes =
        {
            GameMode.FiveFretGuitar,
            GameMode.SixFretGuitar,
            GameMode.EliteDrums,
            GameMode.FourLaneDrums,
            GameMode.FiveLaneDrums,
            GameMode.Vocals,
            GameMode.PartyVocals,
            GameMode.ProKeys
        };

        private static readonly StarPowerActivationType[] _starPowerActivationTypes =
        {
            StarPowerActivationType.RightmostNote,
            StarPowerActivationType.AllNotes,
        };

        private static readonly DrumFlamLayout[] _flamLayouts =
        {
            DrumFlamLayout.V,
            DrumFlamLayout.Bucket,
            DrumFlamLayout.Parallel,
        };

        private static readonly OpenLaneDisplayType[] _openLaneDisplayTypes =
        {
            OpenLaneDisplayType.Never,
            OpenLaneDisplayType.IfChartContainsOpens,
            OpenLaneDisplayType.Always,
        };

        [SerializeField]
        private GameObject _contents;
        [SerializeField]
        private TextMeshProUGUI _profileName;
        [SerializeField]
        private TMP_InputField _nameInput;
        [SerializeField]
        private Image _profilePicture;
        [SerializeField]
        private Button[] _profileActionButtons;

        [Space]
        [SerializeField]
        private GameObject _sidebarContent;
        [SerializeField]
        private TMP_Dropdown _gameModeDropdown;
        [SerializeField]
        private TMP_InputField _noteSpeedField;
        [SerializeField]
        private TMP_InputField _highwayLengthField;
        [SerializeField]
        private TMP_InputField _inputCalibrationField;
        [SerializeField]
        private Toggle _leftyFlipToggle;
        [SerializeField]
        private Toggle _rangeDisabledToggle;
        [SerializeField]
        private TMP_Dropdown _openLaneDisplayTypeDropdown;
        [SerializeField]
        private Toggle _useCymbalModelsToggle;
        [SerializeField]
        private TMP_Dropdown _starPowerActivationTypeDropdown;
        [SerializeField]
        private TMP_Dropdown _flamLayoutDropdown;
        [SerializeField]
        private TMP_Dropdown _engineDropdown;
        [SerializeField]
        private TMP_Dropdown _themeDropdown;
        [SerializeField]
        private TMP_Dropdown _colorProfileDropdown;
        [SerializeField]
        private TMP_Dropdown _eliteLayoutDropdown;
        [SerializeField]
        private Button _eliteLayoutConfigureButton;

        [SerializeField] private Button _midiTestButton;
        [SerializeField]
        private TMP_Dropdown _cameraPresetDropdown;
        [SerializeField]
        private TMP_Dropdown _highwayPresetDropdown;
        [SerializeField]
        private TMP_Dropdown _rockMeterPresetDropdown;

        [Space]
        [SerializeField]
        private GameObject _nameContainer;
        [SerializeField]
        private GameObject _editNameContainer;

        [Space]
        [SerializeField]
        private ProfileListMenu _profileListMenu;

        [Space]
        [SerializeField]
        private Sprite _profileGenericSprite;
        [SerializeField]
        private Sprite _profileBotSprite;

        private RectTransform _microphoneSetupRow;
        private Button _microphoneSetupButton;
        private RuntimeNavigatable _microphoneSetupNavigation;
        private MicrophoneSetup _microphoneSetup;
        internal ProfileView MicrophoneSetupOwner => _microphoneSetupNavigation != null &&
            _microphoneSetupNavigation.NavigationGroup?.SelectedBehaviour == _microphoneSetupNavigation ? _profileView : null;

        private ProfileView _profileView;
        private YargProfile _profile;
        private System.Action _saveProfilesAction = () => PlayerContainer.SaveProfiles();

        private readonly List<GameMode> _gameModesByIndex = new();
        private readonly List<OpenLaneDisplayType> _openLaneDisplayTypesByIndex = new();
        private readonly List<StarPowerActivationType> _starPowerActivationTypesByIndex = new();

        private List<Guid> _enginePresetsByIndex;
        private List<Guid> _colorProfilesByIndex;
        private List<Guid> _cameraPresetsByIndex;
        private List<Guid> _themesByIndex;
        private List<Guid> _highwayPresetsByIndex;
        private List<Guid> _rockmeterPresetsByIndex;

        private void Awake()
        {
            _eliteLayoutConfigureButton?.onClick.AddListener(OpenEliteDrumsLayoutConfigurationMenu);
            _midiTestButton?.onClick.AddListener(OpenMidiTestMenu);
            EnsureMicrophoneSetupEntry();

            // Setup dropdown items
            _gameModeDropdown.options.Clear();
            foreach (var gameMode in _gameModes)
            {
                _gameModesByIndex.Add(gameMode);

                // Create the dropdown option
                _gameModeDropdown.options.Add(new(gameMode.ToLocalizedName()));
            }
        }

        private void OnEnable()
        {
            // These things can change, so do it every time it's enabled.

            PopulateDropdownOptions();
        }

        private void PopulateDropdownOptions()
        {
            // Setup preset dropdowns
            _enginePresetsByIndex =
                CustomContentManager.EnginePresets.AddOptionsToDropdown(_engineDropdown)
                    .Select(i => i.Id).ToList();
            _themesByIndex =
                CustomContentManager.ThemePresets.AddOptionsToDropdown(_themeDropdown)
                    .Select(i => i.Id).ToList();
            _colorProfilesByIndex =
                CustomContentManager.ColorProfiles.AddOptionsToDropdown(_colorProfileDropdown)
                    .Select(i => i.Id).ToList();
            _cameraPresetsByIndex =
                CustomContentManager.CameraSettings.AddOptionsToDropdown(_cameraPresetDropdown)
                    .Select(i => i.Id).ToList();
            _highwayPresetsByIndex =
                CustomContentManager.HighwayPresets.AddOptionsToDropdown(_highwayPresetDropdown)
                    .Select(i => i.Id).ToList();
            _rockmeterPresetsByIndex =
                CustomContentManager.RockMeterPresets.AddOptionsToDropdown(_rockMeterPresetDropdown)
                    .Select(i => i.Id).ToList();

            // Set drum star power activation type
            _starPowerActivationTypeDropdown.options.Clear();
            foreach (var starPowerActivationType in _starPowerActivationTypes)
            {
                _starPowerActivationTypesByIndex.Add(starPowerActivationType);
                _starPowerActivationTypeDropdown.options.Add(new(starPowerActivationType.ToLocalizedName()));
            }

            _openLaneDisplayTypeDropdown.options.Clear();
            foreach (var openLaneDisplayType in _openLaneDisplayTypes)
            {
                _openLaneDisplayTypesByIndex.Add(openLaneDisplayType);
                _openLaneDisplayTypeDropdown.options.Add(new(openLaneDisplayType.ToLocalizedName()));
            }
        }

        private void RemoveUnusedDropdownOptions(YargProfile profile)
        {
            // TODO: Refactor presets so that this doesn't have to be so tightly coupled to the preset implementation
            //  We could use reflection to figure out what each alternate default changes and only show ones that
            //  change something relevant to the profile's game mode

            // Solo Taps only changes FiveFretGuitar
            if (profile.GameMode is not GameMode.FiveFretGuitar)
            {
                RemoveDropdownOption(_engineDropdown, _enginePresetsByIndex, EnginePreset.SoloTaps.Id);
            }

            // Casual only changes FiveFretGuitar, SixFretGuitar, and Vocals
            if (profile.GameMode is not (GameMode.FiveFretGuitar or GameMode.Vocals or GameMode.PartyVocals or GameMode.SixFretGuitar))
            {
                RemoveDropdownOption(_engineDropdown, _enginePresetsByIndex, EnginePreset.Casual.Id);
            }

            // Pro keys isn't changed by anything, apparently
            if (profile.GameMode is GameMode.ProKeys)
            {
                // We will have necessarily already removed SoloTaps and Casual, so removing Precision removes all but Default
                RemoveDropdownOption(_engineDropdown, _enginePresetsByIndex, EnginePreset.Precision.Id);
            }
        }

        private void RemoveDropdownOption(TMP_Dropdown dropdown, List<Guid> presetsByIndex, Guid guid)
        {
            for (int i = presetsByIndex.Count - 1; i >= 0; i--)
            {
                if (presetsByIndex[i] == guid)
                {
                    dropdown.options.RemoveAt(i);
                    presetsByIndex.RemoveAt(i);
                    break;
                }
            }
        }

        public void UpdateSidebar(YargProfile profile, ProfileView profileView)
        {
            _microphoneSetup?.Close();
            EnsureMicrophoneSetupEntry();
            _profile = profile;
            if (_eliteLayoutDropdown != null)
            {
                var profileListMenu = _profileListMenu != null
                    ? _profileListMenu
                    : GetComponentInParent<ProfileListMenu>(true);
                profileListMenu?.RefreshEliteLayoutDropdown(_eliteLayoutDropdown, profile);
            }
            _profileView = profileView;

            if (!PlayerContainer.IsProfileTaken(_profile))
            {
                HideContents();
                UpdateMicrophoneSetupEntry(false);
                return;
            }

            PopulateDropdownOptions();
            RemoveUnusedDropdownOptions(profile);

            _contents.SetActive(true);

            // Display the profile's options
            _profileName.text = _profile.Name;
            _gameModeDropdown.value = _gameModesByIndex.IndexOf(profile.GameMode);
            _starPowerActivationTypeDropdown.value = _starPowerActivationTypesByIndex
                .IndexOf(profile.StarPowerActivationType);
            _noteSpeedField.text = profile.NoteSpeed.ToString(NUMBER_FORMAT, CultureInfo.CurrentCulture);
            _highwayLengthField.text = profile.HighwayLength.ToString(NUMBER_FORMAT, CultureInfo.CurrentCulture);
            _inputCalibrationField.text = _profile.InputCalibrationMilliseconds.ToString();
            _leftyFlipToggle.isOn = profile.LeftyFlip;
            _rangeDisabledToggle.isOn = profile.RangeEnabled;
            _openLaneDisplayTypeDropdown.value = _openLaneDisplayTypesByIndex.IndexOf(profile.OpenLaneDisplayType);
            _useCymbalModelsToggle.isOn = profile.UseCymbalModels;
            RefreshFlamLayoutDropdown();

            // Update preset dropdowns
            _engineDropdown.SetValueWithoutNotify(
                _enginePresetsByIndex.IndexOf(profile.EnginePreset));
            _themeDropdown.SetValueWithoutNotify(
                _themesByIndex.IndexOf(profile.ThemePreset));
            _colorProfileDropdown.SetValueWithoutNotify(
                _colorProfilesByIndex.IndexOf(profile.ColorProfile));
            _cameraPresetDropdown.SetValueWithoutNotify(
                _cameraPresetsByIndex.IndexOf(profile.CameraPreset));
            _highwayPresetDropdown.SetValueWithoutNotify(
                _highwayPresetsByIndex.IndexOf(profile.HighwayPreset));
            _openLaneDisplayTypeDropdown.SetValueWithoutNotify(
                _openLaneDisplayTypesByIndex.IndexOf(profile.OpenLaneDisplayType));
            _starPowerActivationTypeDropdown.SetValueWithoutNotify(
                _starPowerActivationTypesByIndex.IndexOf(profile.StarPowerActivationType));
            _rockMeterPresetDropdown.SetValueWithoutNotify(
                _rockmeterPresetsByIndex.IndexOf(profile.RockMeterPreset));

            // Not all game modes support all engine presets.
            // If the current engine doesn't exist for the selected instrument, the above _engineDropdown
            // will be silently set to index 0, but the engine itself will not have been set, so we need
            // to explicitly set it.
            ChangeEngine();

            // Show the proper name container (hide the editing version)
            _nameContainer.SetActive(true);
            _editNameContainer.SetActive(false);

            // Display the proper profile picture
            _profilePicture.sprite = profile.IsBot ? _profileBotSprite : _profileGenericSprite;

            // Enable/disable the edit profile button
            bool interactable = !_profile.IsBot && PlayerContainer.IsProfileTaken(_profile);
            foreach (var button in _profileActionButtons)
            {
                button.interactable = interactable;
            }

            EnableSettingsForGameMode();
            UpdateMicrophoneSetupEntry(true);
        }

        private void EnsureMicrophoneSetupEntry()
        {
            if (_microphoneSetupRow != null || _midiTestButton == null) return;
            _microphoneSetup = GetComponent<MicrophoneSetup>() ?? gameObject.AddComponent<MicrophoneSetup>();
            var row = Instantiate(_midiTestButton.transform.parent.gameObject, _sidebarContent.transform);
            row.name = "Microphone Setup";
            _microphoneSetupRow = (RectTransform) row.transform;
            var label = row.transform.Find("Option Name").GetComponent<TextMeshProUGUI>();
            SetConfigurationLabel(label, "Menu.ProfileList.MicrophoneConfiguration");
            _microphoneSetupButton = row.GetComponentInChildren<Button>(true);
            _microphoneSetupButton.onClick = new Button.ButtonClickedEvent();
            _microphoneSetupButton.onClick.AddListener(OpenMicrophoneSetup);
            // This authored button's caption is a sibling of its hit target.
            var text = row.transform.Find("Configure").GetComponent<TextMeshProUGUI>();
            SetConfigurationLabel(text, "Menu.Common.Configure");
            _microphoneSetupNavigation = row.AddComponent<RuntimeNavigatable>();
            _microphoneSetupNavigation.ConfirmCallback = OpenMicrophoneSetup;
            var normalColor = label.color;
            _microphoneSetupNavigation.SelectionVisual = selected => label.color = selected
                ? RuntimeNavigatable.SelectedTextColor : normalColor;
            row.SetActive(false);
        }

        private static void SetConfigurationLabel(TextMeshProUGUI label, string key)
        {
            // The MIDI row's localization component may not have received Awake
            // yet. Change its key as well as its text before activating the clone.
            var localization = label.GetComponent<LocalizeText>();
            if (localization != null) localization.SetLocalizationKey(key);
            else label.text = Localize.Key(key);
        }

        private void UpdateMicrophoneSetupEntry(bool joined)
        {
            if (_microphoneSetupRow == null) return;
            bool visible = joined && MicrophoneSetup.CanConfigure(_profile);
            _microphoneSetupRow.SetParent(_sidebarContent.transform, false);
            _microphoneSetupRow.SetSiblingIndex(1);
            _microphoneSetupRow.sizeDelta = new Vector2(0f, 94f);
            _microphoneSetupButton.interactable = visible;
            _microphoneSetupRow.gameObject.SetActive(visible);
            var group = visible && _profileView != null ? _profileView.NavigationGroup : null;
            if (_microphoneSetupNavigation.NavigationGroup != group)
            {
                bool focused = MicrophoneSetupOwner != null;
                _microphoneSetupNavigation.NavigationGroup?.RemoveNavigatable(_microphoneSetupNavigation);
                group?.AddNavigatable(_microphoneSetupNavigation);
                if (focused && group == null) _profileView?.NavigationGroup?.Select(_profileView);
            }
        }

        public void FocusMicrophoneSetup()
        {
            if (_microphoneSetupRow != null && _microphoneSetupRow.gameObject.activeInHierarchy)
                _profileView?.NavigationGroup?.Select(_microphoneSetupNavigation, SelectionOrigin.Navigation);
        }

        public void ReturnFromMicrophoneSetup()
        {
            var owner = MicrophoneSetupOwner;
            owner?.NavigationGroup?.Select(owner, SelectionOrigin.Navigation);
        }

        public void OpenMicrophoneSetup()
        {
            if (!MicrophoneSetup.CanConfigure(_profile) || !PlayerContainer.IsProfileTaken(_profile)) return;
            _profileView?.NavigationGroup?.Select(_profileView);
            _microphoneSetup.Show(BindingsContainer.GetBindingsForProfile(_profile), _profileView, _saveProfilesAction);
        }

        private void EnableSettingsForGameMode()
        {
            // The passed dictionary is empty because we don't currently have any conditionalized profile settings (we used to, but they've all been
            // superseded by the highway ordering interface). You can still populate this dictionary to conditionalize certain settings behind certain
            // values of other settings ("hide setting X if setting Y has value Z", etc.).
            var possibleSettings = _profile.GameMode.PossibleProfileSettings(new());

            for (var i = 0; i < _sidebarContent.transform.childCount; i++)
            {
                // Disable if the child's gameObject.name is not found in possibleSettings
                var child = _sidebarContent.transform.GetChild(i);

#nullable enable
                (string setting, string? overrideText)? settingInfo = null;
#nullable disable

                foreach (var possibleSetting in possibleSettings)
                {
                    if (possibleSetting.setting == child.gameObject.name)
                    {
                        settingInfo = possibleSetting;
                        break;
                    }
                }

                if (settingInfo is null)
                {
                    child.gameObject.SetActive(false);
                }
                else
                {
                    child.gameObject.SetActive(true);
                    if (settingInfo.Value.overrideText is not null)
                    {
                        child.gameObject.transform.Find("Option Name").GetComponent<TextMeshProUGUI>().text = settingInfo.Value.overrideText;
                    }
                }
            }

            if (_midiTestButton != null)
            {
                bool visible = CanUseMidiTest();
                _midiTestButton.interactable = visible;
                _midiTestButton.transform.parent.gameObject.SetActive(visible);
            }
        }

        public void HideContents()
        {
            _microphoneSetup?.Close();
            if (_microphoneSetupRow != null) _microphoneSetupRow.gameObject.SetActive(false);
            _contents.SetActive(false);
        }

        public void SetNameEditMode(bool editing)
        {
            _nameContainer.SetActive(!editing);
            _editNameContainer.SetActive(editing);

            if (editing)
            {
                _nameInput.text = _profile.Name;
                _nameInput.Select();
            }
            else
            {
                // Set the name. Make sure to record the name change in the scores.
                _profile.Name = _nameInput.text;
                ScoreContainer.RecordPlayerInfo(_profile.Id, _profile.Name);

                // Update the UI
                _profileName.text = _profile.Name;
                _profileView.UpdateDisplay(_profile);
            }
        }

        public void SetSaveProfilesAction(System.Action saveProfilesAction)
        {
            _saveProfilesAction = saveProfilesAction ?? (() => PlayerContainer.SaveProfiles());
        }

        public void OpenMidiTestMenu()
        {
            if (!CanUseMidiTest()) return;
            var canvas = GetComponentInParent<Canvas>();
            var menu = canvas != null
                ? MenuManager.Instance.ShowMenu(MenuManager.Menu.MidiTest, canvas.rootCanvas.transform)
                : MenuManager.Instance.ShowMenu(MenuManager.Menu.MidiTest);
            menu.GetComponent<YARG.Menu.MidiTest.MidiTestMenu>().Initialize(_profile);
        }

        private bool CanUseMidiTest() => _profile != null && !_profile.IsBot &&
            (_profile.GameMode is GameMode.EliteDrums or GameMode.FourLaneDrums or GameMode.FiveLaneDrums) &&
            PlayerContainer.IsProfileTaken(_profile);

        public void OpenEliteDrumsLayoutConfigurationMenu()
        {
            if (_profile == null || _profile.GameMode != GameMode.EliteDrums)
                return;

            var profile = _profile;
            var layout = CustomContentManager.EliteDrumsLayouts.GetUsablePresetById(profile.EliteDrumsLayout)
                ?? EliteDrumsLayoutPreset.Compact;
            // The sidebar always lives under a Canvas in the running game. When a host
            // has none (isolated editor fixtures), fall back to the manager's own
            // canvas resolution instead of failing.
            var profileCanvas = GetComponentInParent<Canvas>();
            var menu = profileCanvas != null
                ? MenuManager.Instance.ShowMenu(MenuManager.Menu.EliteDrumsLayout,
                    profileCanvas.rootCanvas.transform)
                : MenuManager.Instance.ShowMenu(MenuManager.Menu.EliteDrumsLayout);
            var editor = menu.GetComponent<YARG.Menu.HighwayConfiguration.EliteDrumsLayoutMenu>();
            bool IsCurrentProfile() => PlayerContainer.Profiles.Contains(profile) && ReferenceEquals(_profile, profile);
            EliteDrumsLayoutPreset SaveLayout(EliteDrumsLayoutPreset draft)
            {
                if (!IsCurrentProfile())
                    return null;

                return CustomContentManager.EliteDrumsLayouts.CommitPreset(draft);
            }

            bool SaveColorProfile(ColorProfile colorProfile)
            {
                if (!IsCurrentProfile())
                    return false;

                if (profile.ColorProfile != colorProfile.Id)
                {
                    profile.ColorProfile = colorProfile.Id;
                    _saveProfilesAction();
                }

                return true;
            }

            bool SaveLeftyFlip(bool leftyFlip)
            {
                if (!IsCurrentProfile())
                    return false;

                if (profile.LeftyFlip != leftyFlip)
                {
                    profile.LeftyFlip = leftyFlip;
                    _saveProfilesAction();
                }

                _leftyFlipToggle.SetIsOnWithoutNotify(leftyFlip);
                return true;
            }

            editor.InitializeForProfileWithCommit(layout,
                CustomContentManager.ColorProfiles.GetPresetById(profile.ColorProfile),
                profile.LeftyFlip, SaveLayout, () => MenuManager.Instance.PopMenu(), IsCurrentProfile,
                acceptedLayout =>
                {
                    if (!IsCurrentProfile() || acceptedLayout == null ||
                        !EliteDrumsLayoutValidator.Validate(acceptedLayout).IsRuntimeSupported)
                        return false;

                    if (profile.EliteDrumsLayout != acceptedLayout.Id)
                    {
                        profile.EliteDrumsLayout = acceptedLayout.Id;
                        _saveProfilesAction();
                    }

                    RefreshEliteLayoutDropdown();
                    return true;
                }, SaveColorProfile, SaveLeftyFlip, profile.FlamLayout, profile.KeepOriginalCymbalColors);
        }

        private void RefreshEliteLayoutDropdown()
        {
            if (_eliteLayoutDropdown == null || _profileListMenu == null || _profile == null)
                return;

            _profileListMenu.RefreshEliteLayoutDropdown(_eliteLayoutDropdown, _profile);
        }

        public void EditProfile()
        {
            // Only allow profile editing if it's taken
            if (!PlayerContainer.IsProfileTaken(_profile))
            {
                return;
            }

            var menu = MenuManager.Instance.PushMenu(MenuManager.Menu.ProfileInfo, false);

            menu.GetComponent<ProfileInfoMenu>().CurrentProfile = _profile;
            menu.gameObject.SetActive(true);
        }

        public void AddDevice()
        {
            _profileView.PromptAddDevice().Forget();
        }

        public void RemoveDevice()
        {
            _profileView.PromptRemoveDevice().Forget();
        }

        internal static void ApplyGameModeSelection(YargProfile profile, GameMode gameMode)
        {
            profile.GameMode = gameMode;
            profile.InitializeLiveMidiDrumModifiers();

            // Native Elite is the initial preference; a playable fallback is resolved per song.
            // Other modes retain their existing first-instrument default.
            profile.CurrentInstrument = gameMode == GameMode.EliteDrums
                ? Instrument.EliteDrums
                : gameMode.PossibleInstruments()[0];
            profile.PreferredInstrument = profile.CurrentInstrument;
            profile.EliteDrumsDownchartTarget = null;
        }

        public void ChangeGameMode()
        {
            ApplyGameModeSelection(_profile, _gameModesByIndex[_gameModeDropdown.value]);

            _profileView.UpdateDisplay(_profile);
            FiltersMenu.ResetIntensityFiltersForProfile(_profile);
            // Update sidebar when game mode changes so the correct settings are displayed
            UpdateSidebar(_profile, _profileView);
        }

        public void ChangeNoteSpeed()
        {
            if (float.TryParse(_noteSpeedField.text, out var speed))
            {
                _profile.NoteSpeed = Mathf.Clamp(speed, 0f, 100f);
            }

            // Always format it after
            _noteSpeedField.text = _profile.NoteSpeed.ToString(NUMBER_FORMAT, CultureInfo.CurrentCulture);
        }

        public void ChangeHighwayLength()
        {
            if (float.TryParse(_highwayLengthField.text, out var speed))
            {
                _profile.HighwayLength = Mathf.Clamp(speed, 0.001f, 10f);
            }

            // Always format it after
            _highwayLengthField.text = _profile.HighwayLength.ToString(NUMBER_FORMAT, CultureInfo.CurrentCulture);
        }

        public void ChangeInputCalibration()
        {
            if (long.TryParse(_inputCalibrationField.text, out long calibration))
            {
                _profile.InputCalibrationMilliseconds = calibration;
            }

            // Always format it after
            _inputCalibrationField.text = _profile.InputCalibrationMilliseconds.ToString();
        }

        public void ChangeLeftyFlip()
        {
            _profile.LeftyFlip = _leftyFlipToggle.isOn;
        }

        public void ChangeRangeDisabled()
        {
            _profile.RangeEnabled = _rangeDisabledToggle.isOn;
        }

        public void ChangeUseCymbalModels()
        {
            _profile.UseCymbalModels = _useCymbalModelsToggle.isOn;
        }

        public void ChangeEngine()
        {
            _profile.EnginePreset = _enginePresetsByIndex[_engineDropdown.value];
        }

        public void ChangeOpenLaneDisplayType()
        {
            _profile.OpenLaneDisplayType = _openLaneDisplayTypesByIndex[_openLaneDisplayTypeDropdown.value];
        }

        public void ChangeStarPowerActivationType()
        {
            _profile.StarPowerActivationType = _starPowerActivationTypesByIndex[_starPowerActivationTypeDropdown.value];
        }

        private void RefreshFlamLayoutDropdown()
        {
            if (_profile == null || _flamLayoutDropdown == null) return;
            _flamLayoutDropdown.ClearOptions();
            _flamLayoutDropdown.AddOptions(_flamLayouts
                .Select(layout => Localize.Key("Enum.DrumFlamLayout", layout)).ToList());
            // Selecting a profile must not run the previous dropdown callback or
            // mark its stored preference as edited merely by displaying it.
            _flamLayoutDropdown.SetValueWithoutNotify(Math.Max(0,
                Array.IndexOf(_flamLayouts, _profile.FlamLayout)));
        }

        public void ChangeFlamLayout()
        {
            if (_profile == null || _flamLayoutDropdown == null || _profile.GameMode is not
                (GameMode.EliteDrums or GameMode.FourLaneDrums or GameMode.FiveLaneDrums)) return;
            int index = _flamLayoutDropdown.value;
            if ((uint)index >= _flamLayouts.Length) return;
            _profile.FlamLayout = _flamLayouts[index];
        }

        public void ChangeTheme()
        {
            var themeGuid = _themesByIndex[_themeDropdown.value];

            // Skip if there are no changes
            if (themeGuid == _profile.ThemePreset) return;

            _profile.ThemePreset = themeGuid;

            var themePreset = CustomContentManager.ThemePresets.GetPresetById(themeGuid);

            bool hasPresets = false;
            var presets = string.Empty;

            // Check camera presets
            if (CustomContentManager.CameraSettings
                .TryGetPresetById(themePreset.PreferredCameraPreset, out var cameraPreset))
            {
                hasPresets = true;
                presets += $"<color=yellow>Camera Preset: {cameraPreset.Name}</color>\n";
            }

            // Check color profiles
            if (CustomContentManager.ColorProfiles
                .TryGetPresetById(themePreset.PreferredColorProfile, out var colorProfile))
            {
                hasPresets = true;
                presets += $"<color=yellow>Color Profile: {colorProfile.Name}</color>\n";
            }

            // Skip if there are no preferred presets
            if (!hasPresets) return;

            // Ask user if they'd like to apply the preferred presets
            var dialog = DialogManager.Instance.ShowMessage("Apply Recommended Presets?",
                "This theme has recommended presets. These presets will make the theme look as intended. " +
                "Would you like to apply them?\n\n" + presets.Trim());
            dialog.ClearButtons();

            // Add buttons

            dialog.AddDialogButton("Menu.Common.DontApply", MenuData.Colors.CancelButton,
                () => DialogManager.Instance.ClearDialog());

            dialog.AddDialogButton("Menu.Common.Apply", MenuData.Colors.ConfirmButton, () =>
            {
                ApplyRecommendedPresets(cameraPreset?.Id, colorProfile?.Id);
                UpdateSidebar(_profile, _profileView);
                DialogManager.Instance.SubmitAndClearDialog();
            });
        }

        private void ApplyRecommendedPresets(Guid? cameraPresetId, Guid? colorProfileId)
        {
            _profile.CameraPreset = cameraPresetId ?? CameraPreset.Default.Id;
            _profile.ColorProfile = colorProfileId ?? ColorProfile.Default.Id;
            _cameraPresetDropdown.SetValueWithoutNotify(_cameraPresetsByIndex.IndexOf(_profile.CameraPreset));
            _colorProfileDropdown.SetValueWithoutNotify(_colorProfilesByIndex.IndexOf(_profile.ColorProfile));
        }

        public void ChangeColorProfile()
        {
            _profile.ColorProfile = _colorProfilesByIndex[_colorProfileDropdown.value];
        }

        public void ChangeCameraPreset()
        {
            _profile.CameraPreset = _cameraPresetsByIndex[_cameraPresetDropdown.value];
        }

        public void ChangeHighwayPreset()
        {
            _profile.HighwayPreset = _highwayPresetsByIndex[_highwayPresetDropdown.value];
        }

        public void ChangeRockMeterPreset()
        {
            _profile.RockMeterPreset = _rockmeterPresetsByIndex[_rockMeterPresetDropdown.value];
        }
    }
}
