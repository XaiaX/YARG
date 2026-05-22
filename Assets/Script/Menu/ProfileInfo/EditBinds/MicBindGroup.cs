// pattern: Imperative Shell
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Localization;
using YARG.Menu.Navigation;

namespace YARG.Menu.ProfileInfo
{
    public class MicBindGroup : MonoBehaviour
    {
        [SerializeField]
        private Transform _micListContainer;
        [SerializeField]
        private GameObject _micSlotPrefab;
        [SerializeField]
        private Button _addButton;
        [SerializeField]
        private GameObject _maxReachedLabel;

        private ProfileBindings _bindings;
        private YargProfile _profile;

        // Event for when microphone list changes
        public delegate void MicrophoneListChangedEvent();
        public event MicrophoneListChangedEvent OnMicrophoneListChanged;

        public void Initialize(ProfileBindings bindings, YargProfile profile)
        {
            _bindings = bindings;
            _profile = profile;

            if (_profile.IsBot)
            {
                // Hide the mic binding section entirely for bot profiles.
                gameObject.SetActive(false);
                return;
            }

            _addButton.onClick.AddListener(OnAddClicked);
            RefreshList();
        }

        private void RefreshList()
        {
            // Clear existing slot UI
            foreach (Transform child in _micListContainer)
                Destroy(child.gameObject);

            // Create a slot for each bound mic
            for (int i = 0; i < _bindings.Microphones.Count; i++)
            {
                var slot = Instantiate(_micSlotPrefab, _micListContainer);
                var micSlotUI = slot.GetComponent<MicSlotUI>();

                // Set device name, wire remove button
                var micDevice = _bindings.Microphones[i];
                micSlotUI.Setup(micDevice, () => RemoveMic(i));
            }

            // Show/hide add button based on cap
            bool atCap = _bindings.Microphones.Count >= 7;
            _addButton.gameObject.SetActive(!atCap);
            if (_maxReachedLabel != null) _maxReachedLabel.SetActive(atCap);

            // Notify listeners that the list changed
            OnMicrophoneListChanged?.Invoke();
        }

        private void OnAddClicked()
        {
            // Show device picker dialog with available mics not already bound
            ShowMicrophonePicker();
        }

        private void ShowMicrophonePicker()
        {
            // Get available microphones
            var availableDevices = Microphone.devices;
            var boundDeviceNames = new HashSet<string>(_bindings.Microphones.Count);
            foreach (var mic in _bindings.Microphones)
            {
                boundDeviceNames.Add(mic.Name);
            }

            // Filter out already bound devices
            var unboundDevices = new List<MicDevice>();
            foreach (var deviceName in availableDevices)
            {
                if (!boundDeviceNames.Contains(deviceName))
                {
                    // Create MicDevice instance
                    var micDevice = new MicDevice(deviceName);
                    unboundDevices.Add(micDevice);
                }
            }

            if (unboundDevices.Count == 0)
            {
                // Show message that no devices are available
                ShowNotification(Localize.Key("Menu.ProfileInfo.NoAvailableMicrophones"));
                return;
            }

            // Create simple picker UI
            var pickerGO = new GameObject("MicrophonePicker");
            pickerGO.transform.SetParent(transform.parent, false);

            var picker = pickerGO.AddComponent<MicrophonePickerUI>();
            picker.Initialize(unboundDevices, OnMicrophoneSelected, () => Destroy(pickerGO));
        }

        private void OnMicrophoneSelected(MicDevice selectedDevice)
        {
            // Add the microphone to the bindings
            _bindings.AddMicrophone(selectedDevice);
            RefreshList();
        }

        private void RemoveMic(int index)
        {
            var micToRemove = _bindings.Microphones[index];
            _bindings.RemoveMicrophone(micToRemove.Name);
            RefreshList();
        }

        private void ShowNotification(string message)
        {
            // This would ideally use the game's notification system
            // For now, log to console and consider implementing a toast notification
            Debug.Log(message);
        }
    }
}