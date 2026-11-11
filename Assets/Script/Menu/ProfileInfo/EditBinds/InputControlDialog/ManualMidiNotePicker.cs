// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using Minis;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YARG.Input;
using YARG.Localization;
using YARG.Menu.Navigation;

namespace YARG.Menu.ProfileInfo
{
    internal sealed class ManualMidiNotePicker : MonoBehaviour
    {
        private const int NOTE_COUNT = 128;

        [SerializeField] private TMP_Dropdown _deviceDropdown;
        [SerializeField] private TMP_Dropdown _noteDropdown;
        [SerializeField] private Button _addButton;

        private MidiDevice[] _devices = Array.Empty<MidiDevice>();
        private ProfileBindings _profileBindings;
        private ControlBinding _binding;

        internal void RegisterControls(NavigationGroup navigation, Action<InputControl> select)
        {
            _deviceDropdown.onValueChanged.AddListener(_ => RefreshAddButton());
            _noteDropdown.onValueChanged.AddListener(_ => RefreshAddButton());
            _addButton.onClick.AddListener(() =>
            {
                if (SelectedControl is { } control) select(control);
            });
            foreach (var dropdown in new[] { _deviceDropdown, _noteDropdown })
                navigation.AddNavigatable(RuntimeNavigatable.Attach(dropdown.gameObject,
                    () => RuntimeNavigatable.OpenDropdownList(dropdown)));
            navigation.AddNavigatable(RuntimeNavigatable.Attach(_addButton.gameObject, () => _addButton.onClick.Invoke()));
        }

        internal bool Prepare(ProfileBindings profileBindings, ControlBinding binding)
        {
            Clear();
            if (binding is not ButtonBinding) return false;

            _devices = profileBindings.InputDevices.OfType<MidiDevice>()
                .Where(device => device.added && binding.IsControlCompatible(device.GetNote(0))).ToArray();
            if (_devices.Length == 0) return false;

            _profileBindings = profileBindings;
            _binding = binding;
            var deviceLabels = _devices.Select(device =>
            {
                string portName = string.IsNullOrEmpty(device.SourcePortName) ? device.displayName : device.SourcePortName;
                if (device.SourcePortId > 0) portName += $" · P{device.SourcePortId}";
                string channel = device.channel < 0 ? Localize.Key("Menu.MidiTest.AllChannels") : $"CH {device.channel + 1}";
                return $"{portName} · {channel}";
            }).ToList();
            SetOptions(_deviceDropdown, deviceLabels);
            _deviceDropdown.gameObject.SetActive(_devices.Length > 1);

            var noteLabels = new List<string>(NOTE_COUNT + 1) { Localize.Key("Menu.ManualMidiBinding.ChooseNote") };
            for (int note = 0; note < NOTE_COUNT; note++)
                noteLabels.Add(_devices[0].GetNote(note).displayName);
            SetOptions(_noteDropdown, noteLabels);
            RefreshAddButton();
            return true;
        }

        internal InputControl SelectedControl
        {
            get
            {
                int deviceIndex = _deviceDropdown.value;
                int note = _noteDropdown.value - 1;
                if (deviceIndex < 0 || deviceIndex >= _devices.Length || note < 0 || note >= NOTE_COUNT)
                    return null;
                var device = _devices[deviceIndex];
                if (!device.added || !_profileBindings.ContainsDevice(device)) return null;
                var control = device.GetNote(note);
                return _binding.ContainsControl(control) ? null : control;
            }
        }

        internal void Clear()
        {
            if (_profileBindings != null) _profileBindings.DeviceRemoved -= OnDeviceRemoved;
            _devices = Array.Empty<MidiDevice>();
            _profileBindings = null;
            _binding = null;
        }

        private static void SetOptions(TMP_Dropdown dropdown, List<string> options)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(0);
        }

        private void RefreshAddButton() => _addButton.interactable = SelectedControl != null;

        private void OnDeviceRemoved(InputDevice _) => RefreshAddButton();

        private void OnEnable()
        {
            if (_profileBindings != null) _profileBindings.DeviceRemoved += OnDeviceRemoved;
        }

        private void OnDisable()
        {
            if (_profileBindings != null) _profileBindings.DeviceRemoved -= OnDeviceRemoved;
        }
    }
}
