using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Helpers.Extensions;
using YARG.Input;
using YARG.Menu.Navigation;
using YARG.Player;

namespace YARG.Menu.ProfileInfo
{
    // TODO: Clean this up when we get real UI
    public class InputControlDialogMenu : MonoBehaviour
    {
        private ProfileBindings _profileBindings;
        private ControlBinding _binding;
        private UniTaskCompletionSource<InputControl> _selection;
        private CancellationTokenSource _cancellationToken;
        private CancellationTokenSource _bindingTokenSource;
        private NavigationScheme _scheme;
        private bool _choosingMidiNote;

        [SerializeField]
        private Transform _controlContainer;
        [SerializeField]
        private GameObject _controlChooseContainer;
        [SerializeField]
        private GameObject _waitingContainer;

        [Space]
        [SerializeField]
        private GameObject _controlEntryPrefab;

        [Space]
        [SerializeField] private ManualMidiNotePicker _manualMidiPicker;
        [SerializeField] private Button _browseMidiButton;
        [SerializeField] private Button _waitingCloseButton;
        [SerializeField] private GameObject _waitingHeader;
        [SerializeField] private NavigationGroup _navigation;

        private void Awake()
        {
            _browseMidiButton.onClick.AddListener(BrowseMidiNotes);
            _navigation.AddNavigatable(RuntimeNavigatable.Attach(_browseMidiButton.gameObject, BrowseMidiNotes));
            _manualMidiPicker.RegisterControls(_navigation, SelectControl);
            var closeNavigation = RuntimeNavigatable.Attach(_waitingCloseButton.gameObject, CancelAndClose);
            closeNavigation.InputAllowed = () => _scheme != null;
            _navigation.AddNavigatable(closeNavigation);
        }

        public async UniTask<bool> Show(YargPlayer player, ControlBinding binding)
        {
            if (_cancellationToken != null) return false;

            using var dialogCancellation = new CancellationTokenSource();
            using var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(dialogCancellation.Token);
            _cancellationToken = dialogCancellation;
            _bindingTokenSource = captureCancellation;
            _profileBindings = player.Bindings;
            _binding = binding;
            _selection = new();
            _choosingMidiNote = false;

            try
            {
                _controlContainer.DestroyChildren();
                _waitingContainer.SetActive(true);
                _waitingHeader.SetActive(true);
                _controlChooseContainer.SetActive(false);
                _manualMidiPicker.gameObject.SetActive(false);
                bool canBrowse = _manualMidiPicker.Prepare(_profileBindings, _binding);
                _browseMidiButton.gameObject.SetActive(canBrowse);
                var closePosition = (RectTransform)_waitingCloseButton.transform.parent;
                closePosition.anchoredPosition = closePosition.anchoredPosition.WithX(canBrowse ? 172 : 0);
                gameObject.SetActive(true);
                if (canBrowse) SetNavigation(false);

                var controls = await InputControlBindingHelper.Instance.GetControl(player, captureCancellation.Token, binding);
                dialogCancellation.Token.ThrowIfCancellationRequested();

                InputControl control;
                // Cancellation can return partially learned controls. Only the picker
                // can complete the assignment once the user has chosen manual entry.
                if (_choosingMidiNote)
                    control = await _selection.Task.AttachExternalCancellation(dialogCancellation.Token);
                else if (controls.Count > 1)
                {
                    ReleaseNavigation();
                    _waitingContainer.SetActive(false);
                    _controlChooseContainer.SetActive(true);
                    PopulateLearnedControls(controls);
                    control = await _selection.Task.AttachExternalCancellation(dialogCancellation.Token);
                }
                else
                    control = controls.Count == 1 ? controls[0] : null;

                return control != null && control.device.added && _profileBindings.ContainsDevice(control.device) &&
                    binding.AddControl(control);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _cancellationToken = null;
                _bindingTokenSource = null;
                _selection = null;
                if (this != null) gameObject.SetActive(false);
                ReleaseNavigation();
                if (_manualMidiPicker != null) _manualMidiPicker.Clear();
                _profileBindings = null;
                _binding = null;
            }
        }

        private void PopulateLearnedControls(List<InputControl> controls)
        {
            foreach (var control in controls)
            {
                var button = Instantiate(_controlEntryPrefab, _controlContainer);
                button.GetComponent<ControlEntry>().Init(control, SelectControl);
            }
        }

        private void BrowseMidiNotes()
        {
            if (_cancellationToken == null || _choosingMidiNote ||
                !_manualMidiPicker.Prepare(_profileBindings, _binding)) return;

            _choosingMidiNote = true;
            _bindingTokenSource.Cancel();
            _waitingHeader.SetActive(false);
            _browseMidiButton.gameObject.SetActive(false);
            _manualMidiPicker.gameObject.SetActive(true);
            SetNavigation(true);
        }

        private void SetNavigation(bool browsing)
        {
            var navigator = Navigator.Instance;
            if (navigator == null) return;

            ReleaseNavigation();
            _navigation.PushNavGroupToStack();
            _navigation.SelectFirst();
            var entries = new List<NavigationScheme.Entry>();
            if (browsing)
            {
                entries.Add(new(MenuAction.Red, "Menu.Common.Back", CancelAndClose));
                entries.Add(NavigationScheme.Entry.NavigateUp);
                entries.Add(NavigationScheme.Entry.NavigateDown);
                entries.Add(NavigationScheme.Entry.NavigateSelect);
            }
            else
            {
                // The fallback keyboard also sends menu Confirm/Back. When the
                // keyboard belongs to this profile, those keys are inputs to learn.
                bool hasAssignedKeyboard = _profileBindings.GetDevicesByType<Keyboard>().Count > 0;
                bool CanNavigate(NavigationContext context) => context.Player == null
                    ? !hasAssignedKeyboard
                    : context.Player.Bindings != _profileBindings;
                entries.Add(new(MenuAction.Red, "Menu.Common.Back", context =>
                {
                    if (CanNavigate(context)) CancelAndClose();
                }, hide: hasAssignedKeyboard));
                entries.Add(new(MenuAction.Green, "Menu.ManualMidiBinding.Browse", context =>
                {
                    if (CanNavigate(context)) BrowseMidiNotes();
                }, hide: hasAssignedKeyboard));
            }
            _scheme = new NavigationScheme(entries, false);
            navigator.PushSchemeImmediate(_scheme);
        }

        private void ReleaseNavigation()
        {
            if (_scheme == null) return;
            _navigation.ClearSelection();
            NavigationGroup.RemoveFromNavigationStack(_navigation);
            Navigator.Instance?.RemoveScheme(_scheme);
            _scheme = null;
        }

        public void CancelAndClose()
        {
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            ReleaseNavigation();
            _cancellationToken?.Cancel();
        }

        private void SelectControl(InputControl control) => _selection?.TrySetResult(control);
    }
}
