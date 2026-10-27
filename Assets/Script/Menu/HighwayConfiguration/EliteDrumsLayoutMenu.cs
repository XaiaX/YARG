using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Helpers;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Menu.Data;
using YARG.Settings.Customization;
using YARG.Settings.Metadata;
using PreviewOptionsDropdown = YARG.Settings.Metadata.PreviewOptionsDropdown;

namespace YARG.Menu.HighwayConfiguration
{
    /// <summary>Edits a detached layout. The caller owns persistence and preview rendering.</summary>
    public sealed class EliteDrumsLayoutMenu : MonoBehaviour
    {
        [SerializeField] private NavigationGroup _navigation;
        [SerializeField] private EliteDrumsLayoutCell[] _cells;
        [SerializeField] private GameObject _popup;
        [SerializeField] private AssignmentPopupLayout _popupLayout;
        [SerializeField] private float _transformSectionRight;
        private Vector2 _transformCanvasSize;
        private bool _transformGeometryFits = true;
        [SerializeField] private Button _popupBackdropButton;
        [SerializeField] private NavigationGroup _popupNavigation;
        [SerializeField] private Button[] _choices;
        [SerializeField] private TMP_Text[] _choiceLabels;
        [SerializeField] private Button _save;
        [SerializeField] private Button _back;
        [SerializeField] private TMP_Text _layoutLabel;
        [SerializeField] private TMP_Text _previewColorLabel;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private TMP_Text _colorsNotice;
        [SerializeField] private GameObject _builtInNotice;
        [SerializeField] private Button _shiftLeft;
        [SerializeField] private Button _shiftRight;
        [SerializeField] private Button _reverseLayout;
        private bool _discardConfirmationShowing;
        [SerializeField] private TMP_Dropdown _layoutDropdown;
        private EliteDrumsLayoutPreset[] _layouts;
        private Guid? _temporaryLayoutId;
        private EliteDrumsLayoutPreset _lastAcceptedProfileLayout;
        private Guid? _lastEditorLayoutId;
        private Guid? _profileSelectedLayoutId;
        private EliteDrumsLayoutPreset _pendingManagedSelection;
        [SerializeField] private Button _openLayoutFolder;
        [SerializeField] private Button _renameLayout;
        [SerializeField] private Button _copyLayout;
        [SerializeField] private Button _deleteLayout;
        [SerializeField] private Button _importLayout;
        [SerializeField] private Button _exportLayout;
        private bool _managementInitialized;
        private bool _refreshingManagementList;
        [SerializeField] private TMP_Dropdown _previewColor;
        [SerializeField] private Transform _previewWorld;
        [SerializeField] private RawImage _previewImage;
        private RenderTexture _ownedPreviewTexture;
        private ColorProfile[] _previewColors;
        private ColorProfile _previewColorProfile;
        private TrackPreviewBuilder _previewBuilder;
        [SerializeField] private TMP_Dropdown _previewOptions;
        [SerializeField] private Toggle _leftyFlipToggle;
        private YARG.Settings.Types.DropdownSetting<string> _previewOptionSetting;
        private bool _forceStarPowerNotes;
        private bool _forceStarPower;
        private bool _forceGroove;
        private bool _leftyFlip;
        private static int _nextPreviewWorldId;
        private int _previewWorldId;

        private EliteDrumsLayoutPreset _original;
        private EliteDrumsLayoutPreset _draft;
        private Action<EliteDrumsLayoutPreset> _saveDraft;
        private Func<EliteDrumsLayoutPreset, EliteDrumsLayoutPreset> _commitDraft;
        private Action _closed;
        private Action _pendingDiscard;
        private string[] _choiceIds;
        private int _column;
        private bool _drum;
        private int? _returnFocus;
        private CanvasGroup _backgroundInputGroup;
        private bool _backgroundWasInteractable;
        private int _popupDismissedFrame = -1;
        private bool BackgroundInputAllowed => (_popup == null || !_popup.activeSelf) &&
            (!Application.isPlaying || Time.frameCount != _popupDismissedFrame);
        private bool _initialized;
        private bool _controlsInitialized;
        private bool _newCopy;
        private bool _ownsScheme;
        private Guid? _profileColorId;
        private Func<bool> _profileContextIsCurrent;
        private bool _profileContextInvalidated;
        private Func<EliteDrumsLayoutPreset, bool> _acceptProfileLayout;
        private Func<ColorProfile, bool> _acceptProfileColor;
        private Func<bool, bool> _acceptProfileLeftyFlip;
        private Action<string> _openFolder = FileExplorerHelper.OpenFolder;
        public void SetFolderOpener(Action<string> openFolder) => _openFolder = openFolder ?? FileExplorerHelper.OpenFolder;

        public void OpenLayoutFolder() => _openFolder?.Invoke(CustomContentManager.EliteDrumsLayouts.FullContentDirectory);

        public EliteDrumsLayoutPreset Draft => _draft?.Copy();
        public bool IsDirty { get; private set; }
        public bool CanSave => _draft != null && !_original.DefaultPreset &&
            EliteDrumsLayoutValidator.Validate(_draft).IsRuntimeSupported;
        public event Action<EliteDrumsLayoutPreset> DraftChanged;

        private void Awake() => InitializeControls();

        private void InitializeControls()
        {
            if (_controlsInitialized) return;
            _controlsInitialized = true;
            _popup.SetActive(false);
            _popupBackdropButton.onClick.AddListener(CancelPopupFromOutside);
            for (int i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i];
                cell.Initialize(this, i % EliteDrumsLayoutPreset.SLOT_COUNT,
                    i >= EliteDrumsLayoutPreset.SLOT_COUNT);
                _navigation.AddNavigatable(cell);
            }
            InitializeChoiceLabels();
            for (int i = 0; i < _choices.Length; i++)
            {
                int choice = i;
                Register(_popupNavigation, _choices[i], () => Choose(choice));
            }
            InitializeLabels();
            _colorsNotice.text = Localize.Key("Menu.EliteLayout.ColorsSeparate");
            if (_builtInNotice != null) _builtInNotice.SetActive(false);
            if (_layoutLabel != null)
                _layoutLabel.text = Localize.Key("Menu.EliteLayout.Layout");
            if (_previewColorLabel != null)
                _previewColorLabel.text = Localize.Key("Menu.EliteLayout.Colors").ToUpperInvariant();
            if (_layoutDropdown != null)
            {
                RefreshManagementList(selectLayout: false);
                _layoutDropdown.onValueChanged.AddListener(index =>
                {
                    if (!_refreshingManagementList && index >= 0 && index < _layouts.Length)
                        SelectManagedLayout(_layouts[index]);
                });
                _navigation.AddNavigatable(RuntimeNavigatable.Attach(_layoutDropdown.gameObject,
                    () => RuntimeNavigatable.OpenDropdownList(_layoutDropdown)));
            }
            InitializeManagementControls();
            Register(_navigation, _back, RequestClose);
            if (_previewOptions != null)
            {
                _previewOptionSetting = PreviewOptionsDropdown.Create(GetPreviewOptionsState, OnPreviewOptionSelected,
                    includeLeftyFlip: false);
                var optionValues = ((IEnumerable<string>)_previewOptionSetting).ToArray();
                _previewOptions.ClearOptions();
                _previewOptions.AddOptions(optionValues.Select(value => PreviewOptionsDropdown.DisplayValue(
                    value, GetPreviewOptionsState())).ToList());
                _previewOptions.SetValueWithoutNotify(-1);
                SetPreviewOptionsCaption();
                _previewOptions.onValueChanged.AddListener(index =>
                {
                    if (index < 0 || index >= optionValues.Length) return;
                    ((YARG.Settings.Types.IDropdownSetting)_previewOptionSetting).SelectIndex(index);
                    _previewOptions.SetValueWithoutNotify(-1);
                    SetPreviewOptionsCaption();
                });
                _navigation.AddNavigatable(RuntimeNavigatable.Attach(_previewOptions.gameObject,
                    () => RuntimeNavigatable.OpenDropdownList(_previewOptions)));
            }
            if (_leftyFlipToggle != null)
            {
                _leftyFlipToggle.SetIsOnWithoutNotify(_leftyFlip);
                _leftyFlipToggle.onValueChanged.AddListener(OnLeftyFlipToggleChanged);
                var leftyNavigation = _leftyFlipToggle.gameObject.AddComponent<RuntimeNavigatable>();
                leftyNavigation.ConfirmCallback = () => _leftyFlipToggle.isOn = !_leftyFlipToggle.isOn;
                var focus = _leftyFlipToggle.transform.Find("Lefty Flip Focus");
                if (focus != null) leftyNavigation.SelectionVisual = focus.gameObject.SetActive;
                _navigation.AddNavigatable(leftyNavigation);
            }
            if (_previewColor != null)
            {
                _previewColors = CustomContentManager.ColorProfiles.AddOptionsToDropdown(_previewColor)
                    .Cast<ColorProfile>().ToArray();
                _previewColorProfile = _previewColors.FirstOrDefault(color => color.Id == _profileColorId) ??
                    _previewColors.FirstOrDefault();
                int selectedColor = Array.FindIndex(_previewColors,
                    color => color.Id == _previewColorProfile?.Id);
                if (selectedColor >= 0) _previewColor.SetValueWithoutNotify(selectedColor);
                _previewColor.onValueChanged.AddListener(index =>
                {
                    if (index < 0 || index >= _previewColors.Length) return;
                    UpdateProfileContextControls();
                    if (_profileContextInvalidated) return;
                    var color = _previewColors[index];
                    if (_profileContextIsCurrent != null &&
                        (!_profileContextIsCurrent() || !_acceptProfileColor(color)))
                    {
                        int previous = Array.FindIndex(_previewColors, item => item.Id == _previewColorProfile?.Id);
                        if (previous >= 0) _previewColor.SetValueWithoutNotify(previous);
                        return;
                    }
                    _previewColorProfile = color;
                    RefreshCellIndicators();
                    if (_previewBuilder != null) _previewBuilder.RefreshColors(_previewColorProfile);
                    else RebuildPreview();
                });
                _navigation.AddNavigatable(RuntimeNavigatable.Attach(_previewColor.gameObject,
                    () => RuntimeNavigatable.OpenDropdownList(_previewColor)));
            }
            _backgroundInputGroup = GetComponent<CanvasGroup>();
            if (_backgroundInputGroup == null) _backgroundInputGroup = gameObject.AddComponent<CanvasGroup>();
            var popupInputGroup = _popup.GetComponent<CanvasGroup>();
            if (popupInputGroup == null) popupInputGroup = _popup.AddComponent<CanvasGroup>();
            popupInputGroup.ignoreParentGroups = true;
            foreach (var navigatable in GetComponentsInChildren<NavigatableBehaviour>(true))
            {
                if (!navigatable.transform.IsChildOf(_popup.transform))
                    navigatable.InputAllowed = () => BackgroundInputAllowed;
            }
        }

        private void InitializeLabels()
        {
            SetLabel("Header/Single Header Text", Localize.Key("Menu.EliteLayout.Title"));
            SetButtonLabel(_renameLayout, Localize.Key("Menu.EliteLayout.Rename"));
            SetButtonLabel(_copyLayout, Localize.Key("Menu.EliteLayout.Copy"));
            SetButtonLabel(_deleteLayout, Localize.Key("Menu.Common.Delete"));
            SetButtonLabel(_importLayout, Localize.Key("Menu.EliteLayout.Import"));
            SetButtonLabel(_exportLayout, Localize.Key("Menu.EliteLayout.Export"));
            SetButtonLabel(_shiftLeft, EliteDrumsTransformButtonLayout.ShiftLeftLabel(Localize.Key("Menu.EliteLayout.ShiftLeft")));
            SetButtonLabel(_shiftRight, EliteDrumsTransformButtonLayout.ShiftRightLabel(Localize.Key("Menu.EliteLayout.ShiftRight")));
            SetButtonLabel(_reverseLayout, EliteDrumsTransformButtonLayout.ReverseLabel(Localize.Key("Menu.EliteLayout.Reverse")));
            _transformCanvasSize = Vector2.zero;
            SetButtonLabel(_save, Localize.Key("Menu.Common.Save"));
        }

        private void RefreshTransformGeometry()
        {
            _transformCanvasSize = ((RectTransform) transform).rect.size;
            if (_transformCanvasSize.x <= 0f || _transformSectionRight <= 0f ||
                _shiftLeft == null || _reverseLayout == null || _shiftRight == null) return;
            var actions = new[] { _shiftLeft, _reverseLayout, _shiftRight };
            var wrappers = actions.Select(button => (RectTransform) button.transform.parent).ToArray();
            var labels = wrappers.Select(wrapper => wrapper.GetComponentInChildren<TMP_Text>(true)).ToArray();
            _transformGeometryFits = EliteDrumsTransformButtonLayout.Apply(wrappers, labels,
                (RectTransform) transform, 0.0225f, _transformSectionRight);
            UpdateTransformActions();
        }

        private void InitializeManagementControls()
        {
            if (_managementInitialized) return;
            _managementInitialized = true;
            Register(_navigation, _openLayoutFolder, OpenLayoutFolder);
            Register(_navigation, _renameLayout, RenameSelectedLayout);
            Register(_navigation, _copyLayout, CopySelectedLayout);
            Register(_navigation, _deleteLayout, DeleteSelectedLayout);
            Register(_navigation, _importLayout, ImportLayout);
            Register(_navigation, _exportLayout, ExportSelectedLayout);
            if (_shiftLeft != null) Register(_navigation, _shiftLeft, ShiftLeft);
            if (_shiftRight != null) Register(_navigation, _shiftRight, ShiftRight);
            if (_reverseLayout != null) Register(_navigation, _reverseLayout, ReverseLayout);
            Register(_navigation, _save, Save);
        }

        private void RefreshManagementList(EliteDrumsLayoutPreset selected = null, bool selectLayout = true)
        {
            var layouts = CustomContentManager.EliteDrumsLayouts.DefaultPresets
                .Concat(CustomContentManager.EliteDrumsLayouts.CustomPresets).ToList();
            _temporaryLayoutId = null;
            if (selected != null && layouts.All(layout => layout.Id != selected.Id))
            {
                layouts.Add(selected.Copy());
                _temporaryLayoutId = selected.Id;
            }

            _layouts = layouts.ToArray();
            _refreshingManagementList = true;
            try
            {
                _layoutDropdown.ClearOptions();
                _layoutDropdown.AddOptions(_layouts.Select(layout => layout.Name).ToList());
                int index = selected == null ? 0 : Array.FindIndex(_layouts, layout => layout.Id == selected.Id);
                if (index < 0) index = 0;
                _layoutDropdown.SetValueWithoutNotify(index);
            }
            finally
            {
                _refreshingManagementList = false;
            }
            if (selectLayout && _layouts.Length > 0) SelectManagedLayout(_layouts[_layoutDropdown.value]);
            UpdateManagementActions();
        }

        private EliteDrumsLayoutPreset SelectedLayout => _layouts != null && _layoutDropdown != null &&
            _layoutDropdown.value >= 0 && _layoutDropdown.value < _layouts.Length
                ? _layouts[_layoutDropdown.value]
                : _original;

        private void UpdateManagementActions()
        {
            var selected = SelectedLayout;
            var validation = selected == null ? null : EliteDrumsLayoutValidator.Validate(selected);
            bool supported = validation?.IsRuntimeSupported == true;
            bool isTemporary = selected != null && _temporaryLayoutId == selected.Id;
            if (_renameLayout != null)
                _renameLayout.interactable = selected != null && !isTemporary && !selected.DefaultPreset && validation?.IsComplete == true;
            if (_copyLayout != null) _copyLayout.interactable = selected != null && !isTemporary && validation?.IsComplete == true;
            if (_deleteLayout != null) _deleteLayout.interactable = selected != null && !isTemporary && !selected.DefaultPreset;
            if (_exportLayout != null) _exportLayout.interactable = selected != null && !isTemporary && validation?.IsComplete == true;
            ApplyManagementButtonStyles();
            if (selected != null && !supported && _status != null)
                _status.text = validation.IsComplete
                    ? Localize.Key("Menu.EliteLayout.Unsupported")
                    : Localize.Key("Menu.EliteLayout.IncompleteManagement");
        }

        private void ApplyManagementButtonStyles()
        {
            var colors = MenuData.Instance != null ? MenuData.Colors : null;
            if (colors == null) return;

            StyleManagementButton(_renameLayout, colors.BrightButton);
            StyleManagementButton(_copyLayout, colors.ConfirmButton);
            StyleManagementButton(_deleteLayout, colors.CancelButton);
            StyleManagementButton(_importLayout, colors.BrightButton);
            StyleManagementButton(_exportLayout, colors.BrightButton);
        }

        private static void StyleManagementButton(Button button, Color enabledColor)
        {
            if (button == null) return;
            var color = button.interactable ? enabledColor : Color.gray;
            var coloredButton = button.GetComponentInParent<YARG.Menu.ColoredButton>();
            if (coloredButton != null)
                coloredButton.SetBackgroundAndTextColor(color);
            else if (button.image != null)
                button.image.color = color;
        }

        internal static int FindNextInteractableManagementAction(Button[] buttons, int currentIndex, int direction)
        {
            if (buttons == null || buttons.Length == 0 || direction == 0) return currentIndex;
            for (int offset = 1; offset < buttons.Length; offset++)
            {
                int index = (currentIndex + direction * offset % buttons.Length + buttons.Length) % buttons.Length;
                if (buttons[index] != null && buttons[index].gameObject.activeInHierarchy && buttons[index].interactable)
                    return index;
            }
            return currentIndex;
        }

        private List<List<NavigatableBehaviour>> GetNavigationRows()
        {
            var rows = new List<List<NavigatableBehaviour>>();
            AddRow(_back);
            AddRow(_layoutDropdown, _openLayoutFolder, _previewColor, _previewOptions);
            AddRow(_save, _renameLayout, _copyLayout, _deleteLayout, _importLayout, _exportLayout);
            foreach (bool drum in new[] { false, true })
            {
                var row = _cells.Where(cell => cell.Drum == drum && IsEnabled(cell))
                    .OrderBy(NavigationX).Cast<NavigatableBehaviour>().ToList();
                if (row.Count > 0) rows.Add(row);
            }
            AddRow(_leftyFlipToggle, _shiftLeft, _reverseLayout, _shiftRight);
            return rows;

            void AddRow(params Selectable[] controls)
            {
                var row = controls.Where(control => control != null)
                    .Select(control => control.GetComponent<NavigatableBehaviour>())
                    .Where(target => IsEnabled(target) || IsSelectedInactiveShift(target))
                    .OrderBy(NavigationX).ToList();
                if (row.Count > 0) rows.Add(row);
            }
        }

        private bool IsSelectedInactiveShift(NavigatableBehaviour target)
        {
            if (target == null || !target.isActiveAndEnabled || _navigation.SelectedBehaviour != target)
                return false;
            return (_shiftLeft != null && target.gameObject == _shiftLeft.gameObject) ||
                (_shiftRight != null && target.gameObject == _shiftRight.gameObject);
        }

        private static bool IsEnabled(NavigatableBehaviour target)
        {
            if (target == null || !target.isActiveAndEnabled) return false;
            var selectable = target.GetComponent<Selectable>();
            return selectable != null && selectable.IsInteractable();
        }

        private static float NavigationX(NavigatableBehaviour target)
        {
            var rect = target.transform as RectTransform;
            return rect != null ? rect.TransformPoint(rect.rect.center).x : target.transform.position.x;
        }

        private bool IsUsableStoredLayout(EliteDrumsLayoutPreset preset) => preset != null &&
            _temporaryLayoutId != preset.Id &&
            EliteDrumsLayoutValidator.Validate(preset).IsRuntimeSupported &&
            CustomContentManager.EliteDrumsLayouts.UsablePresets.Any(layout => layout.Id == preset.Id);

        private void RestoreAcceptedProfileLayoutSelectionOnly()
        {
            if (_lastEditorLayoutId == null || _layoutDropdown == null) return;
            int index = Array.FindIndex(_layouts, layout => layout.Id == _lastEditorLayoutId.Value);
            if (index >= 0) _layoutDropdown.SetValueWithoutNotify(index);
        }

        private void RestoreAcceptedProfileLayoutSelection()
        {
            if (_lastAcceptedProfileLayout == null || _layoutDropdown == null) return;
            int index = Array.FindIndex(_layouts, layout => layout.Id == _lastAcceptedProfileLayout.Id);
            if (index < 0) return;
            _layoutDropdown.SetValueWithoutNotify(index);
            var accepted = _lastAcceptedProfileLayout.Copy();
            _original = accepted;
            _draft = accepted.Copy();
            IsDirty = false;
            _newCopy = false;
            Render();
            UpdateManagementActions();
        }

        private void UpdateProfileContextControls()
        {
            if (_profileContextIsCurrent == null || _profileContextIsCurrent() || _profileContextInvalidated) return;
            _profileContextInvalidated = true;
            foreach (var selectable in GetComponentsInChildren<Selectable>(true))
                selectable.interactable = false;
            if (_back != null) _back.interactable = true;
            if (_popup.activeSelf) CancelPopup();
            CancelDiscardConfirmation();
            Close();
        }

        private void OnLeftyFlipToggleChanged(bool leftyFlip)
        {
            UpdateProfileContextControls();
            if (_profileContextInvalidated || _profileContextIsCurrent != null &&
                (!_profileContextIsCurrent() || !_acceptProfileLeftyFlip(leftyFlip)))
            {
                _leftyFlipToggle.SetIsOnWithoutNotify(_leftyFlip);
                return;
            }
            _leftyFlip = leftyFlip;
            UpdateTransformActions();
            RefreshCellIndicators();
            if (_previewBuilder != null) _previewBuilder.LeftyFlip = leftyFlip;
            else RebuildPreview();
        }

        private void SelectManagedLayout(EliteDrumsLayoutPreset preset)
        {
            if (preset == null) return;
            UpdateProfileContextControls();
            if (_profileContextInvalidated || _profileContextIsCurrent != null && !_profileContextIsCurrent())
            {
                RestoreAcceptedProfileLayoutSelection();
                return;
            }
            Action select = () =>
            {
                var validation = EliteDrumsLayoutValidator.Validate(preset);
                bool eligible = IsUsableStoredLayout(preset);
                _lastEditorLayoutId = preset.Id;
                _pendingManagedSelection = null;
                if (!validation.IsStructurallyValid || preset.Merges.Length != 0)
                {
                    _original = preset.Copy();
                    _draft = null;
                    UpdateTransformActions();
                    IsDirty = false;
                    RenderManagementStatus(preset);
                    UpdateManagementActions();
                    ClearPreview();
                }
                else
                {
                    _original = preset.Copy();
                    _draft = preset.Copy();
                    IsDirty = false;
                    _newCopy = false;
                    Render();
                    UpdateManagementActions();
                }

                if (eligible && _profileContextIsCurrent != null && _profileContextIsCurrent() &&
                    _acceptProfileLayout(preset.Copy()))
                {
                    _lastAcceptedProfileLayout = preset.Copy();
                    _profileSelectedLayoutId = preset.Id;
                }
            };
            if (IsDirty)
            {
                _pendingManagedSelection = preset.Copy();
                RequestDiscard(select);
            }
            else select();
        }

        private void CopySelectedLayout()
        {
            var selected = SelectedLayout;
            if (selected == null || !EliteDrumsLayoutValidator.Validate(selected).IsComplete) return;
            RunAfterDiscard(() => DialogManager.Instance.ShowRenameDialog(
                Localize.Key("Menu.EliteLayout.CopyName"), name =>
                {
                    if (string.IsNullOrWhiteSpace(name)) return;
                    try
                    {
                        var copy = (EliteDrumsLayoutPreset)selected.CopyWithNewName(name.Trim());
                        CustomContentManager.EliteDrumsLayouts.CopyPreset(selected, copy);
                        RefreshManagementList(copy, selectLayout: false);
                        SelectManagedLayout(copy);
                    }
                    catch (Exception exception) { ShowManagementError(exception); }
                }));
        }

        private void RenameSelectedLayout()
        {
            var selected = SelectedLayout;
            if (selected == null || selected.DefaultPreset ||
                !EliteDrumsLayoutValidator.Validate(selected).IsComplete) return;
            RunAfterDiscard(() => DialogManager.Instance.ShowRenameDialog(
                Localize.Key("Menu.EliteLayout.RenameTitle"), name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                try
                {
                    CustomContentManager.EliteDrumsLayouts.RenamePreset(selected, name.Trim());
                    RefreshManagementList(selected, selectLayout: false);
                    SelectManagedLayout(selected);
                }
                catch (Exception exception) { ShowManagementError(exception); }
            }));
        }

        private void CompleteLayoutDeletionSelection(bool deletesProfileSelection)
        {
            var fallback = EliteDrumsLayoutPreset.Compact;
            bool fallbackUsable = CustomContentManager.EliteDrumsLayouts.UsablePresets
                .Any(layout => layout.Id == fallback.Id);
            if (deletesProfileSelection && fallbackUsable && _profileContextIsCurrent?.Invoke() == true &&
                _acceptProfileLayout?.Invoke(fallback.Copy()) == true)
            {
                _lastAcceptedProfileLayout = fallback.Copy();
                _profileSelectedLayoutId = fallback.Id;
            }

            var editorSelection = deletesProfileSelection || _profileContextIsCurrent == null
                ? fallback
                : _lastAcceptedProfileLayout ?? fallback;
            RefreshManagementList(editorSelection, selectLayout: false);
            _lastEditorLayoutId = editorSelection.Id;
            _original = editorSelection.Copy();
            _draft = editorSelection.Copy();
            IsDirty = false;
            _newCopy = false;
            Render();
            UpdateManagementActions();
        }

        private void DeleteSelectedLayout()
        {
            var selected = SelectedLayout;
            if (selected == null || selected.DefaultPreset) return;
            bool deletesProfileSelection = _profileContextIsCurrent != null && _profileSelectedLayoutId == selected.Id;
            RunAfterDiscard(() => PresetSubTab.ShowCompactConfirmation(
                Localize.Key("Settings.PresetSetting.Dialog.DeletePreset.Title"),
                Localize.KeyFormat("Settings.PresetSetting.Dialog.DeletePreset.Message", selected.Name),
                "Menu.Common.Delete", MenuData.Colors.CancelButton, () =>
                {
                    DialogManager.Instance.ClearDialog();
                    try
                    {
                        UpdateProfileContextControls();
                        if (_profileContextInvalidated) return;
                        CustomContentManager.EliteDrumsLayouts.DeletePreset(selected);
                        CompleteLayoutDeletionSelection(deletesProfileSelection);
                    }
                    catch (Exception exception) { ShowManagementError(exception); }
                }, cancelColor: MenuData.Colors.BrightButton, armDelaySeconds: 2f));
        }

        private void ImportLayout()
        {
            RunAfterDiscard(() => FileExplorerHelper.OpenChooseFile(null, "json", path =>
            {
                try
                {
                    var layouts = CustomContentManager.EliteDrumsLayouts;
                    var plan = layouts.PlanImport(path);
                    if (!plan.CanImport)
                    {
                        DialogManager.Instance.ShowMessage(Localize.Key("Settings.PresetSetting.Dialog.ImportLayout.Failed.Title"),
                            Localize.KeyFormat("Settings.PresetSetting.Dialog.ImportLayout.Failed.Message",
                                string.Join("\n", plan.Errors)));
                        return;
                    }

                    var imported = layouts.ImportPreset(path) as EliteDrumsLayoutPreset;
                    if (imported == null) return;
                    RefreshManagementList(imported, selectLayout: false);
                    SelectManagedLayout(imported);
                }
                catch (Exception exception) { ShowManagementError(exception); }
            }));
        }

        private void ExportSelectedLayout()
        {
            var selected = SelectedLayout;
            if (selected == null || !EliteDrumsLayoutValidator.Validate(selected).IsComplete) return;
            FileExplorerHelper.OpenSaveFile(null, selected.Name, "json", path =>
            {
                try { CustomContentManager.EliteDrumsLayouts.ExportPreset(selected, path); }
                catch (Exception exception) { ShowManagementError(exception); }
            });
        }

        private void RunAfterDiscard(Action action)
        {
            if (IsDirty) RequestDiscard(action);
            else action();
        }

        private static void ShowManagementError(Exception exception)
        {
            DialogManager.Instance.ShowMessage("Elite Drums Layout", exception.Message);
        }

        private void RenderManagementStatus(EliteDrumsLayoutPreset preset)
        {
            var validation = EliteDrumsLayoutValidator.Validate(preset);
            _status.text = validation.IsComplete
                ? Localize.Key("Menu.EliteLayout.Unsupported")
                : Localize.Key("Menu.EliteLayout.IncompleteManagement");
            _save.interactable = false;
            foreach (var cell in _cells) cell.gameObject.SetActive(false);
            ClearPreview();
        }

        private void SetLabel(string objectName, string value)
        {
            var text = transform.Find(objectName)?.GetComponent<TMP_Text>();
            if (text == null && objectName == "Header/Title")
                text = transform.Find("Title")?.GetComponent<TMP_Text>();
            if (text == null)
                throw new InvalidOperationException($"Elite layout menu is missing its {objectName} label.");
            text.text = value;
        }

        private static void SetButtonLabel(Button button, string value)
        {
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text == null && button.transform.parent != null)
                text = button.transform.parent.GetComponentInChildren<TMP_Text>(true);
            if (text == null)
                throw new InvalidOperationException($"Elite layout button {button.name} has no TMP label.");
            text.text = value;
        }

        private void InitializeChoiceLabels()
        {
            if (_choiceLabels == null || _choiceLabels.Length != _choices.Length)
            {
                _choiceLabels = new TMP_Text[_choices.Length];
            }

            for (int i = 0; i < _choices.Length; i++)
            {
                if (_choiceLabels[i] != null) continue;

                // The production button prefab keeps its label beside the nested Button,
                // not beneath it. Prefer a direct descendant for other button layouts,
                // then look in the wrapper that contains both sibling objects.
                _choiceLabels[i] = _choices[i].GetComponentInChildren<TMP_Text>(true);
                if (_choiceLabels[i] == null)
                {
                    var wrapper = _choices[i].transform.parent;
                    _choiceLabels[i] = wrapper != null ? wrapper.GetComponentInChildren<TMP_Text>(true) : null;
                }
                if (_choiceLabels[i] == null)
                {
                    throw new InvalidOperationException(
                        $"Elite layout choice {_choices[i].name} has no TMP label in its prefab hierarchy.");
                }
            }
        }

        private void Register(NavigationGroup group, Button button, Action action)
        {
            bool Allowed() => button.interactable && (group == _popupNavigation || BackgroundInputAllowed);
            button.onClick.AddListener(() => { if (Allowed()) action(); });
            Action confirm = () => { if (Allowed()) action(); };
            var popupRow = button.GetComponent<AssignmentPopupRow>();
            var navigatable = popupRow != null ? popupRow.AttachNavigation(confirm) :
                RuntimeNavigatable.Attach(button.gameObject, confirm);
            if (popupRow != null) navigatable.InputAllowed = Allowed;
            group.AddNavigatable(navigatable);
        }

        private void ClearProfileContext()
        {
            _profileContextIsCurrent = null;
            _acceptProfileLayout = null;
            _acceptProfileColor = null;
            _acceptProfileLeftyFlip = null;
            _lastAcceptedProfileLayout = null;
            _commitDraft = null;
        }

        public void InitializeForProfile(EliteDrumsLayoutPreset layout, ColorProfile previewColor, bool leftyFlip,
            Action<EliteDrumsLayoutPreset> save, Action closed, Func<bool> isProfileCurrent,
            Func<EliteDrumsLayoutPreset, bool> acceptLayout, Func<ColorProfile, bool> acceptColor,
            Func<bool, bool> acceptLeftyFlip) =>
            InitializeForProfileCore(layout, previewColor, leftyFlip, save, null, closed, isProfileCurrent,
                acceptLayout, acceptColor, acceptLeftyFlip);

        public void InitializeForProfileWithCommit(EliteDrumsLayoutPreset layout, ColorProfile previewColor,
            bool leftyFlip, Func<EliteDrumsLayoutPreset, EliteDrumsLayoutPreset> commit, Action closed,
            Func<bool> isProfileCurrent, Func<EliteDrumsLayoutPreset, bool> acceptLayout,
            Func<ColorProfile, bool> acceptColor, Func<bool, bool> acceptLeftyFlip) =>
            InitializeForProfileCore(layout, previewColor, leftyFlip, null,
                commit ?? throw new ArgumentNullException(nameof(commit)), closed, isProfileCurrent,
                acceptLayout, acceptColor, acceptLeftyFlip);

        private void InitializeForProfileCore(EliteDrumsLayoutPreset layout, ColorProfile previewColor,
            bool leftyFlip, Action<EliteDrumsLayoutPreset> save,
            Func<EliteDrumsLayoutPreset, EliteDrumsLayoutPreset> commit, Action closed,
            Func<bool> isProfileCurrent, Func<EliteDrumsLayoutPreset, bool> acceptLayout,
            Func<ColorProfile, bool> acceptColor, Func<bool, bool> acceptLeftyFlip)
        {
            var current = isProfileCurrent ?? throw new ArgumentNullException(nameof(isProfileCurrent));
            var layoutAccepted = acceptLayout ?? throw new ArgumentNullException(nameof(acceptLayout));
            var colorAccepted = acceptColor ?? throw new ArgumentNullException(nameof(acceptColor));
            var leftyAccepted = acceptLeftyFlip ?? throw new ArgumentNullException(nameof(acceptLeftyFlip));
            var validation = EliteDrumsLayoutValidator.Validate(layout);
            if (!validation.IsStructurallyValid)
                throw new ArgumentException("Only structurally valid layouts can be opened in the editor: " +
                    string.Join(" ", validation.Errors), nameof(layout));
            if (_initialized && IsDirty)
            {
                RequestDiscard(() => InitializeForProfileCore(layout, previewColor, leftyFlip, save, commit,
                    closed, current, layoutAccepted, colorAccepted, leftyAccepted));
                return;
            }

            ClearProfileContext();
            _profileContextIsCurrent = current;
            _profileContextInvalidated = false;
            foreach (var selectable in GetComponentsInChildren<Selectable>(true))
                selectable.interactable = true;
            _acceptProfileLayout = layoutAccepted;
            _acceptProfileColor = colorAccepted;
            _acceptProfileLeftyFlip = leftyAccepted;
            _profileColorId = previewColor?.Id;
            _previewColorProfile = previewColor;
            _leftyFlip = leftyFlip;
            _commitDraft = commit;
            InitializeCore(layout, save, closed);
            _lastAcceptedProfileLayout = layout.Copy();
            _profileSelectedLayoutId = layout.Id;
            _lastEditorLayoutId = layout.Id;
            if (_leftyFlipToggle != null) _leftyFlipToggle.SetIsOnWithoutNotify(leftyFlip);
            if (_previewColor != null && _previewColors != null && previewColor != null)
            {
                int index = Array.FindIndex(_previewColors, color => color.Id == previewColor.Id);
                if (index >= 0) _previewColor.SetValueWithoutNotify(index);
                _previewColorProfile = previewColor;
                RefreshCellIndicators();
                RebuildPreview();
            }
        }

        public void Initialize(EliteDrumsLayoutPreset preset, Action<EliteDrumsLayoutPreset> save,
            Action closed, ColorProfile previewColor)
        {
            ClearProfileContext();
            _profileColorId = previewColor?.Id;
            _previewColorProfile = previewColor;
            Initialize(preset, save, closed);
            if (_previewColor != null && _previewColors != null && previewColor != null)
            {
                int index = Array.FindIndex(_previewColors, color => color.Id == previewColor.Id);
                if (index >= 0) _previewColor.SetValueWithoutNotify(index);
                _previewColorProfile = previewColor;
                RefreshCellIndicators();
                RebuildPreview();
            }
        }

        public void Initialize(EliteDrumsLayoutPreset preset, Action<EliteDrumsLayoutPreset> save, Action closed)
        {
            ClearProfileContext();
            if (_initialized && IsDirty)
            {
                RequestDiscard(() => Initialize(preset, save, closed));
                return;
            }
            InitializeCore(preset, save, closed);
        }

        private void InitializeCore(EliteDrumsLayoutPreset preset, Action<EliteDrumsLayoutPreset> save, Action closed)
        {
            InitializeControls();
            var validation = EliteDrumsLayoutValidator.Validate(preset);
            if (!validation.IsStructurallyValid)
                throw new ArgumentException("Only structurally valid layouts can be opened in the editor: " +
                    string.Join(" ", validation.Errors), nameof(preset));
            if (_profileContextIsCurrent == null)
            {
                _leftyFlip = false;
                _leftyFlipToggle?.SetIsOnWithoutNotify(false);
            }
            _original = preset.Copy();
            _draft = preset.Copy();
            if (_profileContextIsCurrent != null) _lastAcceptedProfileLayout = preset.Copy();
            if (_layoutDropdown != null)
            {
                RefreshManagementList(preset, selectLayout: false);
            }
            else
            {
                _temporaryLayoutId = null;
            }
            _saveDraft = save;
            _closed = closed;
            _initialized = true;
            IsDirty = false;
            _newCopy = false;
            gameObject.SetActive(true);
            _navigation.PushNavGroupToStack();
            Render();
            var firstRow = GetNavigationRows().FirstOrDefault();
            firstRow?.FirstOrDefault()?.SetSelected(true, SelectionOrigin.Programmatically);
        }

        public void OpenCell(int column, bool drum)
        {
            if (!BackgroundInputAllowed || _draft == null || _original.DefaultPreset ||
                !EliteDrumsLayoutValidator.Validate(_draft).IsStructurallyValid || _draft.Merges.Length != 0) return;
            _column = column;
            _drum = drum;
            _returnFocus = _navigation.SelectedIndex;
            _choiceIds = (drum ? new[] { "snare", "tom1", "tom2", "tom3", "hi-hat" } :
                new[] { "left-crash", "ride", "right-crash", "hi-hat" })
                .Concat(EliteDrumsLayoutEditor.GetKickChoices(_draft, column)
                    .Select(kind => kind.ToString().ToLowerInvariant()))
                .Concat(new[] { "clear", "cancel" }).ToArray();
            bool canClear = RowHasAssignment(column, drum);
            for (int i = 0; i < _choices.Length; i++)
            {
                var wrapper = _choices[i].transform.parent;
                bool visible = i < _choiceIds.Length;
                if (wrapper != null) wrapper.gameObject.SetActive(visible);
                else _choices[i].gameObject.SetActive(visible);
                _choiceLabels[i].text = visible
                    ? EliteDrumsAssignmentChoice.FormatLabel(_draft, column, drum, _choiceIds[i],
                        PopupLabel(_choiceIds[i] == "hi-hat" && drum ? "hi-hat-pedal" : _choiceIds[i]))
                    : string.Empty;
                _choices[i].GetComponent<AssignmentPopupRow>()?.ResetSelection();
                _choices[i].interactable = !visible || _choiceIds[i] != "clear" || canClear;
            }
            _backgroundWasInteractable = _backgroundInputGroup.interactable;
            _backgroundInputGroup.interactable = false;
            EventSystem.current?.SetSelectedGameObject(null);
            _popup.transform.SetAsLastSibling();
            _popup.SetActive(true);
            _popupLayout?.SetTitle(Localize.Key("Menu.EliteLayout.AssignmentTitle"));
            _popupLayout?.Refresh();
            // Choices reuse button instances, so restore every active choice's palette on each opening.
            if (MenuData.Instance != null)
            {
                for (int i = 0; i < _choiceIds.Length; i++)
                {
                    var color = _choiceIds[i] == "cancel" ? MenuData.Colors.NavigationYellow :
                        _choiceIds[i] == "clear"
                            ? canClear ? MenuData.Colors.BrightButton : Color.gray
                            : MenuData.Colors.ConfirmButton;
                    var row = _choices[i].GetComponent<AssignmentPopupRow>();
                    if (row != null) row.SetActionStyle(_choiceIds[i] is "cancel" or "clear", color);
                    else StyleManagementButton(_choices[i], color);
                }
            }
            // NavigationGroup skips inactive objects, but not disabled Buttons. Keep Clear visible
            // while omitting it from controller navigation until there is a row to clear.
            _popupNavigation.ClearNavigatables();
            for (int i = 0; i < _choiceIds.Length; i++)
            {
                if (_choices[i].interactable)
                    _popupNavigation.AddNavigatable(_choices[i].GetComponent<RuntimeNavigatable>());
            }
            _popupNavigation.PushNavGroupToStack();
            _popupNavigation.SelectFirst();
        }

        private bool RowHasAssignment(int column, bool drum)
        {
            var slot = _draft.Slots[column];
            if (slot.Kind == EliteDrumsLayoutSlotKind.HiHat) return true;
            if (slot.Kind is EliteDrumsLayoutSlotKind.Kick or EliteDrumsLayoutSlotKind.Kick1 or EliteDrumsLayoutSlotKind.Kick2)
                return drum;
            return drum ? !string.IsNullOrEmpty(slot.Drum) : !string.IsNullOrEmpty(slot.Cymbal);
        }

        private void Choose(int index)
        {
            if (!_popup.activeSelf || index < 0 || index >= _choiceIds.Length || !_choices[index].interactable) return;
            string id = _choiceIds[index];
            if (id == "cancel") { CancelPopup(); return; }
            var changed = id switch
            {
                "clear" => EliteDrumsLayoutEditor.ClearRow(_draft, _column, _drum),
                "kick" => EliteDrumsLayoutEditor.PlaceKick(_draft, _column, EliteDrumsLayoutSlotKind.Kick),
                "kick1" => EliteDrumsLayoutEditor.PlaceKick(_draft, _column, EliteDrumsLayoutSlotKind.Kick1),
                "kick2" => EliteDrumsLayoutEditor.PlaceKick(_draft, _column, EliteDrumsLayoutSlotKind.Kick2),
                _ => EliteDrumsLayoutEditor.Place(_draft, _column, id)
            };
            _draft = changed;
            IsDirty = _newCopy || !Equivalent(_original, _draft);
            CancelPopup();
            Render();
        }

        private bool CanTransformLayout => _draft != null && _original != null && !_original.DefaultPreset &&
            _draft.Merges.Length == 0 && EliteDrumsLayoutValidator.Validate(_draft).IsStructurallyValid;

        private bool HasAssignedColumns => _draft.Slots.Any(slot => slot.Kind != EliteDrumsLayoutSlotKind.Empty);
        private bool CanShiftTowardLowerColumn => CanTransformLayout && HasAssignedColumns &&
            _draft.Slots[0].Kind == EliteDrumsLayoutSlotKind.Empty;
        private bool CanShiftTowardHigherColumn => CanTransformLayout && HasAssignedColumns &&
            _draft.Slots[EliteDrumsLayoutPreset.SLOT_COUNT - 1].Kind == EliteDrumsLayoutSlotKind.Empty;
        public bool CanShiftLeft => _leftyFlip ? CanShiftTowardHigherColumn : CanShiftTowardLowerColumn;
        public bool CanShiftRight => _leftyFlip ? CanShiftTowardLowerColumn : CanShiftTowardHigherColumn;
        public bool CanReverseLayout => CanTransformLayout && HasAssignedColumns;

        public void ShiftLeft() => TransformLayout(_leftyFlip ? 1 : -1, false);
        public void ShiftRight() => TransformLayout(_leftyFlip ? -1 : 1, false);
        public void ReverseLayout() => TransformLayout(0, true);

        private void TransformLayout(int direction, bool reverse)
        {
            bool canShiftInDirection = direction < 0 ? CanShiftTowardLowerColumn : CanShiftTowardHigherColumn;
            if ((reverse ? !CanReverseLayout : !canShiftInDirection) ||
                _popup != null && _popup.activeSelf || _discardConfirmationShowing) return;

            // Permute complete saved columns, not individual sources: rows, gaps and split kicks stay intact.
            var changed = _draft.Copy();
            changed.Slots = EliteDrumsLayoutPreset.CreateEmptySlots();
            for (int column = 0; column < EliteDrumsLayoutPreset.SLOT_COUNT; column++)
            {
                int destination = reverse ? EliteDrumsLayoutPreset.SLOT_COUNT - 1 - column : column + direction;
                if (destination >= 0 && destination < EliteDrumsLayoutPreset.SLOT_COUNT)
                    changed.Slots[destination] = _draft.Slots[column].Copy();
            }
            if (Equivalent(_draft, changed)) return;
            _draft = changed;
            IsDirty = _newCopy || !Equivalent(_original, _draft);
            Render();
            // Keep focus on the invoking action at the boundary. Confirm remains gated by
            // interactability, while directional navigation can leave the inactive action.
        }

        private void UpdateTransformActions()
        {
            SetState(_shiftLeft, CanShiftLeft);
            SetState(_shiftRight, CanShiftRight);
            SetState(_reverseLayout, CanReverseLayout);

            void SetState(Button button, bool enabled)
            {
                if (button == null) return;
                var wrapper = button.GetComponentInParent<YARG.Menu.ColoredButton>(true);
                (wrapper != null ? wrapper.gameObject : button.gameObject).SetActive(CanTransformLayout && _transformGeometryFits);
                button.interactable = enabled;
                if (MenuData.Instance != null) StyleManagementButton(button, MenuData.Colors.BrightButton);
            }
        }

        private static bool Equivalent(EliteDrumsLayoutPreset a, EliteDrumsLayoutPreset b) =>
            a.Slots.Zip(b.Slots, (left, right) => left.Kind == right.Kind &&
                left.Drum == right.Drum && left.Cymbal == right.Cymbal).All(equal => equal);

        public void CancelPopup()
        {
            if (!_popup.activeSelf) return;
            _popup.SetActive(false);
            NavigationGroup.RemoveFromNavigationStack(_popupNavigation);
            if (_backgroundInputGroup != null) _backgroundInputGroup.interactable = _backgroundWasInteractable;
            _popupDismissedFrame = Time.frameCount;
            // Saved column/row identity survives mirrored presentation and navigation reordering.
            var origin = _cells.FirstOrDefault(cell => cell.Column == _column && cell.Drum == _drum);
            if (origin != null)
            {
                _navigation.SelectAt(null);
                origin.SetSelected(true, SelectionOrigin.Programmatically);
                EventSystem.current?.SetSelectedGameObject(origin.gameObject);
            }
            else _navigation.SelectAt(_returnFocus);
        }

        /// <summary>Called by the full-screen picker blocker; its raycast consumes the click before this runs.</summary>
        public void CancelPopupFromOutside() => CancelPopup();

        /// <summary>Route the menu's back action here before closing its owning screen.</summary>
        public void Back()
        {
            if (Application.isPlaying && Time.frameCount == _popupDismissedFrame) return;
            if (_popup.activeSelf) CancelPopup();
            else if (_discardConfirmationShowing)
                CancelDiscardConfirmation();
            else RequestClose();
        }

        public void Save()
        {
            if (!BackgroundInputAllowed) return;
            if (!CanSave || (_profileContextIsCurrent != null && !_profileContextIsCurrent()))
            {
                UpdateProfileContextControls();
                return;
            }
            var savedDraft = _draft.Copy();
            var committed = _commitDraft != null ? _commitDraft(savedDraft) : savedDraft;
            if (_commitDraft == null) _saveDraft?.Invoke(savedDraft);
            if (committed == null) return;
            if (_layoutDropdown != null) RefreshManagementList(committed, selectLayout: false);
            _lastEditorLayoutId = committed.Id;
            if (_profileContextIsCurrent != null && _profileContextIsCurrent() && IsUsableStoredLayout(committed))
            {
                bool alreadyAccepted = _profileSelectedLayoutId == committed.Id;
                if (alreadyAccepted || _acceptProfileLayout(committed.Copy()))
                {
                    _lastAcceptedProfileLayout = committed.Copy();
                    _profileSelectedLayoutId = committed.Id;
                }
            }
            // Saving establishes a new edit baseline; Back owns leaving the editor.
            _original = committed.Copy();
            _draft = committed.Copy();
            IsDirty = false;
            _newCopy = false;
            Render();
            UpdateManagementActions();
        }

        public void RequestClose()
        {
            if (!BackgroundInputAllowed) return;
            if (IsDirty) RequestDiscard(Close);
            else Close();
        }

        private void CancelDiscardConfirmation()
        {
            if (!_discardConfirmationShowing) return;
            DialogManager.Instance?.ClearDialog();
            _discardConfirmationShowing = false;
            _pendingDiscard = null;
            _pendingManagedSelection = null;
            RestoreAcceptedProfileLayoutSelectionOnly();
        }

        private void RequestDiscard(Action action)
        {
            if (_popup.activeSelf)
            {
                CancelPopup();
                return;
            }

            _pendingDiscard = action;
            _discardConfirmationShowing = true;
            PresetSubTab.ShowCompactConfirmation(
                Localize.Key("Menu.EliteLayout.DiscardTitle"),
                Localize.Key("Menu.EliteLayout.DiscardMessage"),
                "Menu.EliteLayout.Discard", MenuData.Colors.CancelButton, () =>
                {
                    DialogManager.Instance.ClearDialog();
                    _discardConfirmationShowing = false;
                    var pending = _pendingDiscard;
                    _pendingDiscard = null;
                    IsDirty = false;
                    pending?.Invoke();
                },
                cancelColor: MenuData.Colors.ConfirmButton,
                armDelaySeconds: 1f,
                cancelKey: "Menu.EliteLayout.KeepEditing",
                onCancel: () =>
                {
                    _discardConfirmationShowing = false;
                    _pendingDiscard = null;
                    _pendingManagedSelection = null;
                    RestoreAcceptedProfileLayoutSelectionOnly();
                });
        }

        private void Close()
        {
            _draft = null;
            _initialized = false;
            gameObject.SetActive(false);
            ClearProfileContext();
            _closed?.Invoke();
            if (_closed == null && MenuManager.Instance != null)
                MenuManager.Instance.PopMenu();
        }

        private void OnEnable()
        {
            if (Navigator.Instance == null) return;
            _ownsScheme = true;
            _ = Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                new(MenuAction.Up, "Menu.Common.Up", () => NavigateGrid(0, -1), hide: true),
                new(MenuAction.Down, "Menu.Common.Down", () => NavigateGrid(0, 1), hide: true),
                new(MenuAction.Left, "Menu.Common.Left", () => NavigateGrid(-1, 0), hide: true),
                new(MenuAction.Right, "Menu.Common.Right", () => NavigateGrid(1, 0), hide: true),
                NavigationScheme.Entry.NavigateSelect,
                new(MenuAction.Red, "Menu.Common.Back", Back)
            }, false));
        }

        public void NavigateGrid(int horizontal, int vertical)
        {
            var current = NavigationGroup.CurrentNavigationGroup;
            if (current != _navigation)
            {
                if (vertical < 0) current?.SelectPrevious();
                else if (vertical > 0) current?.SelectNext();
                return;
            }
            var rows = GetNavigationRows();
            if (rows.Count == 0) return;
            var selected = _navigation.SelectedBehaviour;
            int rowIndex = rows.FindIndex(row => row.Contains(selected));
            if (rowIndex < 0)
            {
                var fallback = selected == null ? rows[0][0] : rows.SelectMany(row => row)
                    .OrderBy(target => Mathf.Abs(NavigationX(target) - NavigationX(selected))).First();
                fallback.SetSelected(true, SelectionOrigin.Navigation);
                return;
            }

            var row = rows[rowIndex];
            NavigatableBehaviour next = selected;
            if (vertical != 0)
            {
                int nextRow = Mathf.Clamp(rowIndex + Math.Sign(vertical), 0, rows.Count - 1);
                if (nextRow != rowIndex)
                    next = rows[nextRow].OrderBy(target => Mathf.Abs(NavigationX(target) - NavigationX(selected))).First();
            }
            else if (horizontal != 0)
            {
                int column = row.IndexOf(selected);
                next = row[Mathf.Clamp(column + Math.Sign(horizontal), 0, row.Count - 1)];
            }
            if (next != selected) next.SetSelected(true, SelectionOrigin.Navigation);
        }

        private void OnDestroy()
        {
            if (_previewWorld == null || _previewWorld.parent != null) return;
            if (Application.isPlaying) Destroy(_previewWorld.gameObject);
            else DestroyImmediate(_previewWorld.gameObject);
        }

        private void OnDisable()
        {
            if (_ownsScheme) Navigator.Instance?.PopScheme();
            _ownsScheme = false;
            ClearPreview();
            NavigationGroup.RemoveFromNavigationStack(_navigation);
            NavigationGroup.RemoveFromNavigationStack(_popupNavigation);
            if (_popup.activeSelf && _backgroundInputGroup != null)
                _backgroundInputGroup.interactable = _backgroundWasInteractable;
            _popup.SetActive(false);
            _popupNavigation.ClearNavigatables();
            _discardConfirmationShowing = false;
            _pendingDiscard = null;
            _pendingManagedSelection = null;
        }

        private void Render()
        {
            if (_draft == null) return;
            if (_builtInNotice != null) _builtInNotice.SetActive(_original.DefaultPreset);
            UpdateTransformActions();
            RefreshCellIndicators();
            _save.interactable = CanSave;
            var missing = EliteDrumsLayoutEditor.GetUnassigned(_draft).Select(Label);
            _status.text = _original.DefaultPreset ? Localize.Key("Menu.EliteLayout.CopyToEdit") :
                CanSave ? Localize.Key("Menu.EliteLayout.Ready") :
                Localize.Key("Menu.EliteLayout.Unassigned") + " " + string.Join(", ", missing);
            string kickBehavior = _draft.Slots.Any(slot => slot.Kind == EliteDrumsLayoutSlotKind.Kick1) ? "Separate" :
                _draft.Slots.Any(slot => slot.Kind == EliteDrumsLayoutSlotKind.Kick) ? "Dedicated" : "Default";
            _status.text += "\n" + Localize.Key("Menu.EliteLayout.KickBehavior") + " " +
                Localize.Key("Menu.EliteLayout.Kick." + kickBehavior);
            DraftChanged?.Invoke(_draft.Copy());
            RebuildPreview();
        }

        private void RefreshCellIndicators()
        {
            if (_draft == null) return;
            bool runtimeSupported = EliteDrumsLayoutValidator.Validate(_draft).IsRuntimeSupported;
            foreach (var cell in _cells)
            {
                cell.gameObject.SetActive(true);
                cell.SetLeftyFlip(_leftyFlip);
                cell.Render(_draft.Slots[cell.Column], _previewColorProfile, _original?.DefaultPreset == true,
                    runtimeSupported);
            }
        }

        private void ClearPreview()
        {
            _previewBuilder = null;
            if (_ownedPreviewTexture != null)
            {
                CameraPreviewTexture.ClearOwnedPreviewTargets(_previewWorld, _ownedPreviewTexture);
                CameraPreviewTexture.ReleaseOwnedPreviewTexture(_ownedPreviewTexture);
                _ownedPreviewTexture = null;
            }

            if (_previewWorld != null)
            {
                // Hide immediately: deferred Destroy must not leave an old frame visible
                // while the replacement world is being initialized.
                for (int i = _previewWorld.childCount - 1; i >= 0; i--)
                {
                    var child = _previewWorld.GetChild(i).gameObject;
                    child.SetActive(false);
                    if (Application.isPlaying) Destroy(child);
                    else DestroyImmediate(child);
                }
            }

            if (_previewImage != null)
            {
                _previewImage.gameObject.SetActive(false);
                _previewImage.texture = null;
            }
        }

        private PreviewOptionsDropdown.State GetPreviewOptionsState() =>
            new(_forceStarPowerNotes, _forceStarPower, _forceGroove, _leftyFlip);

        private void OnPreviewOptionSelected(string option)
        {
            switch (option)
            {
                case "Star Power Notes": _forceStarPowerNotes = !_forceStarPowerNotes; break;
                case "Star Power Active": _forceStarPower = !_forceStarPower; break;
                case "Groove": _forceGroove = !_forceGroove; break;
                case "Lefty Flip": _leftyFlip = !_leftyFlip; break;
                default: return;
            }

            if (_previewBuilder == null) RebuildPreview();
            else
            {
                _previewBuilder.ForceStarPowerNotes = _forceStarPowerNotes;
                _previewBuilder.ForceStarPower = _forceStarPower;
                _previewBuilder.ForceGroove = _forceGroove;
                _previewBuilder.LeftyFlip = _leftyFlip;
            }
            RefreshPreviewOptionsLabels();
        }

        private void SetPreviewOptionsCaption()
        {
            if (_previewOptions == null || _previewOptions.captionText == null) return;
            var placeholder = _previewOptions.placeholder as TMP_Text;
            if (placeholder == null)
            {
                placeholder = Instantiate(_previewOptions.captionText, _previewOptions.captionText.transform.parent);
                placeholder.name = "Placeholder";
                _previewOptions.placeholder = placeholder;
            }
            placeholder.text = PreviewOptionsDropdown.Caption;
            _previewOptions.SetValueWithoutNotify(-1);
            _previewOptions.RefreshShownValue();
        }

        private void RefreshPreviewOptionsLabels()
        {
            if (_previewOptionSetting == null || _previewOptions == null) return;
            var options = ((IEnumerable<string>)_previewOptionSetting).ToArray();
            _previewOptions.ClearOptions();
            _previewOptions.AddOptions(options.Select(value =>
                PreviewOptionsDropdown.DisplayValue(value, GetPreviewOptionsState())).ToList());
            _previewOptions.SetValueWithoutNotify(-1);
            if (_previewOptions.captionText != null)
            {
                _previewOptions.captionText.text = PreviewOptionsDropdown.Caption;
                _previewOptions.RefreshShownValue();
            }
        }

        private void RebuildPreview()
        {
            ClearPreview();
            if (_previewWorld == null || _previewImage == null || _draft == null) return;
            var validation = EliteDrumsLayoutValidator.Validate(_draft);
            if (!validation.IsStructurallyValid || _draft.Merges.Length != 0) return;
            _previewWorldId = _previewWorldId == 0 ? ++_nextPreviewWorldId : _previewWorldId;
            // Rendered geometry must not inherit CanvasScaler changes after camera registration.
            _previewWorld.SetParent(null, false);
            _previewWorld.rotation = Quaternion.identity;
            _previewWorld.localScale = Vector3.one;
            _previewWorld.position = new Vector3(_previewWorldId * 10000f, 0f, 0f);
            foreach (var child in _previewWorld.GetComponentsInChildren<Transform>(true)
                         .Where(child => child.parent == _previewWorld && !child.gameObject.activeSelf))
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            if (_draft.Slots.All(slot => slot.Kind == EliteDrumsLayoutSlotKind.Empty))
            {
                _status.text = Localize.Key("Menu.EliteLayout.EmptyPreview");
                _save.interactable = false;
                _previewImage.gameObject.SetActive(false);
                return;
            }
            _previewBuilder = new TrackPreviewBuilder
            {
                StartingGameMode = YARG.Core.GameMode.EliteDrums,
                EliteLayoutOverride = _draft.Copy(),
                AllowIncompleteEliteLayoutPreview = true,
                ColorProfileOverride = _previewColorProfile,
                ForceStarPowerNotes = _forceStarPowerNotes,
                ForceStarPower = _forceStarPower,
                ForceGroove = _forceGroove,
                LeftyFlip = _leftyFlip
            };
            _ = _previewBuilder.BuildPreviewWorld(_previewWorld);
            var texture = EnsurePreviewTexture();
            CameraPreviewTexture.SetOwnedPreviewTargets(_previewWorld, texture);
            _previewImage.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            TrackPreviewBuilder.ConfigurePreviewImage(_previewImage, texture);
        }

        private void LateUpdate()
        {
            if (!_popup.activeSelf && ((RectTransform) transform).rect.size != _transformCanvasSize)
                RefreshTransformGeometry();
            UpdateProfileContextControls();
            if (_previewWorld == null || _previewImage == null ||
                !_previewImage.gameObject.activeInHierarchy) return;

            var previousTexture = _ownedPreviewTexture;
            var texture = EnsurePreviewTexture();
            if (texture != previousTexture)
            {
                CameraPreviewTexture.SetOwnedPreviewTargets(_previewWorld, texture);
            }

            TrackPreviewBuilder.ConfigurePreviewImage(_previewImage, texture);
        }

        private RenderTexture EnsurePreviewTexture()
        {
            var canvas = _previewImage.canvas;
            var outputCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            int width = Mathf.Max(1, outputCamera != null ? outputCamera.pixelWidth : Screen.width);
            int height = Mathf.Max(1, outputCamera != null ? outputCamera.pixelHeight : Screen.height);
            if (_ownedPreviewTexture == null || _ownedPreviewTexture.width != width ||
                _ownedPreviewTexture.height != height)
            {
                if (_ownedPreviewTexture != null)
                {
                    CameraPreviewTexture.ClearOwnedPreviewTargets(_previewWorld, _ownedPreviewTexture);
                    CameraPreviewTexture.ReleaseOwnedPreviewTexture(_ownedPreviewTexture);
                }

                _ownedPreviewTexture = CameraPreviewTexture.CreateOwnedPreviewTexture(
                    width, height, "Elite drums layout preview");
            }

            return _ownedPreviewTexture;
        }

        internal static string Label(string id) => Localize.Key("Menu.EliteLayout.Item." + id);

        private static string PopupLabel(string id) => Label(id).Replace("\n", " ").ToUpperInvariant();
    }
}
