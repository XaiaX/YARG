using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Localization;
using YARG.Menu.Navigation;

namespace YARG.Menu.ProfileInfo
{
    public class MicrophonePickerUI : MonoBehaviour
    {
        [SerializeField]
        private Transform _contentContainer;
        [SerializeField]
        private GameObject _itemPrefab;
        [SerializeField]
        private Button _confirmButton;
        [SerializeField]
        private Button _cancelButton;
        [SerializeField]
        private TextMeshProUGUI _titleText;
        [SerializeField]
        private TextMeshProUGUI _instructionText;

        private List<MicDevice> _availableDevices;
        private System.Action<MicDevice> _onSelectionCallback;
        private System.Action _onCancelCallback;

        private NavigationGroup _navGroup;

        private void Awake()
        {
            _navGroup = GetComponentInChildren<NavigationGroup>();

            // Wire buttons
            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        public void Initialize(List<MicDevice> availableDevices, System.Action<MicDevice> onSelectionCallback, System.Action onCancelCallback)
        {
            _availableDevices = availableDevices;
            _onSelectionCallback = onSelectionCallback;
            _onCancelCallback = onCancelCallback;

            // Set title and instruction
            _titleText.text = Localize.Key("Menu.ProfileInfo.SelectMicrophone");
            _instructionText.text = Localize.Key("Menu.ProfileInfo.SelectMicrophoneInstruction");

            // Create items for each available microphone
            foreach (var device in _availableDevices)
            {
                var itemGO = Instantiate(_itemPrefab, _contentContainer);
                var item = itemGO.GetComponent<NavigationItem>();

                // Set item text
                var text = itemGO.GetComponentInChildren<TextMeshProUGUI>();
                text.text = device.Name;

                // Add selection callback
                item.OnSelected.AddListener(() => OnDeviceSelected(device));

                _navGroup.AddNavigatable(item);
            }

            // Select first item
            _navGroup.SelectFirst();
        }

        private void OnDeviceSelected(MicDevice device)
        {
            // Highlight selected device
            // This could be done by updating the item's appearance
            _onSelectionCallback?.Invoke(device);
            Destroy(gameObject);
        }

        private void OnConfirmClicked()
        {
            // For simplicity, just cancel if no selection was made
            // In a real implementation, you might want to require selection
            OnCancelClicked();
        }

        private void OnCancelClicked()
        {
            _onCancelCallback?.Invoke();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Clean up navigation group
            if (_navGroup != null)
            {
                _navGroup.ClearNavigatables();
            }
        }
    }
}