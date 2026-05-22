using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Audio;

namespace YARG.Menu.ProfileInfo
{
    public class MicSlotUI : MonoBehaviour
    {
        [SerializeField]
        private TMPro.TextMeshProUGUI _deviceNameText;
        [SerializeField]
        private Button _removeButton;

        public void Setup(MicDevice device, System.Action removeCallback)
        {
            _deviceNameText.text = device.DisplayName;
            _removeButton.onClick.AddListener(() => removeCallback());
        }
    }
}
