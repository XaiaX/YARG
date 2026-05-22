using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Localization;

namespace YARG.Menu.ProfileInfo
{
    public class MicSlotUI : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _deviceNameText;
        [SerializeField]
        private Button _removeButton;
        [SerializeField]
        private GameObject _disconnectButton;

        private MicDevice _micDevice;
        private System.Action _removeCallback;

        public void Setup(MicDevice device, System.Action removeCallback)
        {
            _micDevice = device;
            _removeCallback = removeCallback;

            // Set device name text
            _deviceNameText.text = _micDevice.Name;

            // Wire remove button
            _removeButton.onClick.AddListener(OnRemoveClicked);

            // Show/hide disconnect button based on device connection status
            // This would be populated from the actual device connection status
            var isConnected = CheckDeviceConnectionStatus(_micDevice.Name);
            _disconnectButton.SetActive(isConnected);
        }

        private bool CheckDeviceConnectionStatus(string deviceName)
        {
            // This would check the actual connection status of the microphone
            // For now, assume all are connected
            // In a real implementation, you might use Microphone.GetDeviceState or similar
            return true;
        }

        private void OnRemoveClicked()
        {
            // Remove this microphone
            _removeCallback?.Invoke();
        }

        private void OnDestroy()
        {
            // Clean up button listener
            if (_removeButton != null)
            {
                _removeButton.onClick.RemoveListener(OnRemoveClicked);
            }
        }
    }
}