using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Logging;
using YARG.Menu.Data;
using YARG.Menu.Persistent;

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

        public void Initialize(ProfileBindings bindings, YargProfile profile)
        {
            _bindings = bindings;
            _profile = profile;

            if (_profile.IsBot)
            {
                gameObject.SetActive(false);
                return;
            }

            _addButton.onClick.RemoveListener(OnAddClicked);
            _addButton.onClick.AddListener(OnAddClicked);
            RefreshList();
        }

        private void RefreshList()
        {
            foreach (Transform child in _micListContainer)
                Destroy(child.gameObject);

            for (int i = 0; i < _bindings.Microphones.Count; i++)
            {
                var slot = Instantiate(_micSlotPrefab, _micListContainer);
                var micSlotUI = slot.GetComponent<MicSlotUI>();
                var micDevice = _bindings.Microphones[i];
                int captureIndex = i;
                micSlotUI.Setup(micDevice, () => RemoveMic(captureIndex));
            }

            bool atCap = _bindings.Microphones.Count >= 7;
            _addButton.gameObject.SetActive(!atCap);
            if (_maxReachedLabel != null) _maxReachedLabel.SetActive(atCap);
        }

        private void OnAddClicked()
        {
            var dialog = DialogManager.Instance.List();

            var boundNames = new HashSet<string>();
            foreach (var mic in _bindings.Microphones)
                boundNames.Add(mic.DisplayName);

            bool anyAvailable = false;
            foreach (var (id, name) in GlobalAudioHandler.GetAllInputDevices())
            {
                if (boundNames.Contains(name)) continue;
                anyAvailable = true;
                int deviceId = id;
                string deviceName = name;
                dialog.AddListButton(deviceName, () =>
                {
                    var device = GlobalAudioHandler.CreateInputDevice(deviceId, deviceName);
                    if (device != null)
                    {
                        _bindings.AddMicrophone(device);
                        RefreshList();
                    }
                });
            }

            if (!anyAvailable)
            {
                DialogManager.Instance.ClearDialog();
                YargLogger.LogWarning("No available microphones to add");
                return;
            }

            dialog.AddDialogButton("Menu.Common.Close", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);
        }

        private void RemoveMic(int index)
        {
            if (index >= 0 && index < _bindings.Microphones.Count)
            {
                var micToRemove = _bindings.Microphones[index];
                _bindings.RemoveMicrophone(micToRemove);
                RefreshList();
            }
        }
    }
}
