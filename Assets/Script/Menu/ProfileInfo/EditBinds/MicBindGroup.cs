using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Input;
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

            bool atCap = _bindings.Microphones.Count >= _bindings.MicrophoneCap;
            _addButton.gameObject.SetActive(!atCap);
            if (_maxReachedLabel != null) _maxReachedLabel.SetActive(atCap);
        }

        private void OnAddClicked()
        {
            var dialog = DialogManager.Instance.ShowList("Select Microphone");

            var boundIds = new HashSet<string>();
            foreach (var mic in _bindings.Microphones)
                boundIds.Add(mic.StableId);

            bool anyAvailable = false;
            foreach (var (id, name) in GlobalAudioHandler.GetAllInputDevices())
            {
                string stableId = MicDevice.ComputeStableId(id, name);
                if (boundIds.Contains(stableId)) continue;
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
                        DialogManager.Instance.ClearDialog();
                    }
                    else
                    {
                        YargLogger.LogFormatWarning("Failed to initialize microphone `{0}`.", deviceName);
                        DialogManager.Instance.ClearDialog();
                        DialogManager.Instance.ShowMessage("Microphone Error",
                            $"Failed to initialize microphone:\n\n{deviceName}\n\nPlease try again or choose a different microphone.");
                    }
                }, closeOnClick: false);
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
