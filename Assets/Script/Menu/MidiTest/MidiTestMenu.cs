// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Input.Diagnostics;
using YARG.Helpers;
using YARG.Input;
using YARG.Input.Bindings;
using YARG.Input.Diagnostics;
using YARG.Localization;
using YARG.Menu.Navigation;

namespace YARG.Menu.MidiTest
{
    public sealed class MidiTestMenu : MonoBehaviour
    {
        private const float ROW_HEIGHT = 58;
        [SerializeField] private NavigationGroup _navigation;
        [SerializeField] private Button _headerBack, _back, _record, _folder, _clear;
        [SerializeField] private TMP_Dropdown _pedalController;
        [SerializeField] private TMP_Text _profile, _devices, _pedal, _note, _velocity, _details, _matches,
            _latestMessage, _status, _recordLabel;
        [SerializeField] private ScrollRect _history, _detailScroll;
        [SerializeField] private RectTransform _rowTemplate;
        private readonly List<Row> _rows = new();
        private readonly List<IDisposable> _menuInputScopes = new();
        private MidiTestCapture _capture;
        private MidiTestRecord[] _records = Array.Empty<MidiTestRecord>();
        private MidiTestAddress[] _controllers = Array.Empty<MidiTestAddress>();
        private bool _controlsReady, _ownsScheme, _updatingControllers;
        private double _nextRefresh;
        private string _actionError;

        private sealed class Row
        {
            public RectTransform Root;
            public TMP_Text Time, Input, Value, Pedal;
        }

        private static string Label(string name) => Localize.Key("Menu.MidiTest." + name);

        private void Awake() => InitializeControls();

        private void InitializeControls()
        {
            if (_controlsReady || _navigation == null) return;
            _controlsReady = true;
            AddButton(_headerBack, Back);
            AddButton(_back, Back);
            AddButton(_record, ToggleRecording);
            AddButton(_folder, OpenLogFolder);
            AddButton(_clear, ClearHistory);
            _pedalController.onValueChanged.AddListener(index =>
            {
                if (_updatingControllers || _capture == null) return;
                _capture.SelectPedal(index > 0 && index <= _controllers.Length ? _controllers[index - 1] : (MidiTestAddress?)null);
                RefreshDisplay();
            });
            _navigation.AddNavigatable(RuntimeNavigatable.Attach(_pedalController.gameObject,
                () => RuntimeNavigatable.OpenDropdownList(_pedalController)));
            _history.onValueChanged.AddListener(_ => RefreshRows());
            for (int i = 0; i < 24; i++)
            {
                var root = Instantiate(_rowTemplate, _rowTemplate.parent);
                root.name = "History row " + i;
                _rows.Add(new Row { Root = root, Time = root.Find("Time").GetComponent<TMP_Text>(),
                    Input = root.Find("Input").GetComponent<TMP_Text>(), Value = root.Find("Value").GetComponent<TMP_Text>(),
                    Pedal = root.Find("Pedal").GetComponent<TMP_Text>() });
            }
        }

        private void AddButton(Button button, Action action)
        {
            button.onClick.AddListener(() => action());
            _navigation.AddNavigatable(RuntimeNavigatable.Attach(button.gameObject, action));
        }

        public void Initialize(YargProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            ReleaseCapture();
            InitializeControls();
            var bindings = BindingsContainer.GetBindingsForProfile(profile);
            // A drum mapped to Back/Confirm must not operate this diagnostic screen.
            // Keep the same profile's keyboard/gamepad navigation available.
            using (Navigator.Instance?.PushInputBlocker())
            {
                foreach (var binding in bindings.MenuBindings.OfType<ButtonBinding>())
                    _menuInputScopes.Add(binding.SuspendMidiInput());
            }
            _capture = new MidiTestCapture(bindings);
            _profile.text = profile.Name;
            _navigation.PushNavGroupToStack();
            _navigation.SelectFirst();
            RefreshDisplay();
        }

        private void OnEnable()
        {
            if (Navigator.Instance == null) return;
            _ownsScheme = true;
            _ = Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateUp, NavigationScheme.Entry.NavigateDown,
                NavigationScheme.Entry.NavigateSelect, new(MenuAction.Red, "Menu.Common.Back", Back)
            }, false));
        }

        private void Update()
        {
            if (_capture == null || Time.unscaledTimeAsDouble < _nextRefresh) return;
            _nextRefresh = Time.unscaledTimeAsDouble + .05;
            RefreshDisplay();
        }

        public void ToggleRecording()
        {
            if (_capture == null) return;
            _capture.SetRecording(!_capture.IsRecording);
            RefreshDisplay();
        }

        public void OpenLogFolder()
        {
            if (_capture == null) return;
            try
            {
                System.IO.Directory.CreateDirectory(_capture.LogDirectory);
                FileExplorerHelper.OpenFolder(_capture.LogDirectory);
                _actionError = null;
            }
            catch (Exception exception) { _actionError = exception.Message; }
            RefreshDisplay();
        }

        public void ClearHistory()
        {
            _capture?.ClearHistory();
            _history.verticalNormalizedPosition = 1;
            RefreshDisplay();
        }

        private static string NoteName(int number)
        {
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            return names[number % 12] + (number / 12 - 1).ToString(CultureInfo.InvariantCulture);
        }

        private static string InputText(MidiTestRecord record) => record.Kind switch
        {
            MidiTestMessageKind.ControlChange => $"CC {record.Message.Data1}",
            MidiTestMessageKind.ChannelAftertouch => Label("ChannelAftertouch"),
            MidiTestMessageKind.PolyphonicAftertouch => $"{Label("PolyAftertouch")} #{record.Message.Data1}",
            _ => $"{NoteName(record.Message.Data1)}  #{record.Message.Data1}" +
                (record.Kind == MidiTestMessageKind.NoteOff ? " OFF" : "")
        };

        private static string PedalText(MidiTestAddress? address, int? value, bool? closed)
        {
            if (address is not { } controller) return Label("UnknownPedal");
            string state = closed.HasValue ? (closed.Value ? Label("Closed") : Label("Open")) : Label("RawPosition");
            return $"CC {controller.Number}  ·  {value?.ToString(CultureInfo.InvariantCulture) ?? "—"} / 127\n{state}";
        }

        private void RefreshDisplay()
        {
            if (_capture == null) return;
            _devices.text = _capture.ConfiguredDeviceCount == 0 ? Label("NoDevices") :
                string.Join("\n", _capture.Configuration.Endpoints.Select(e =>
                    e.PortName + $" · P{e.PortId} · " + (e.Channel < 0 ? Label("AllChannels") : $"CH {e.Channel + 1}")).Distinct());
            _pedal.text = PedalText(_capture.CurrentPedalAddress, _capture.CurrentPedalValue, _capture.CurrentPedalClosed);
            var note = _capture.LatestNote;
            _note.text = note.HasValue ? $"{NoteName(note.Value.Message.Data1)}   ·   #{note.Value.Message.Data1}" : Label("WaitingNote");
            _velocity.text = note.HasValue ? $"{Label("Velocity")}  {note.Value.Message.Data2} / 127" : Label("StrikePad");
            _details.text = note.HasValue ? $"{note.Value.Message.PortName}\nP{note.Value.Message.PortId} · CH {note.Value.Message.Channel + 1} · {note.Value.Message.ReceiptMilliseconds:F1} ms\nRAW {note.Value.Message.Status:X2} {note.Value.Message.Data1:X2} {note.Value.Message.Data2:X2}" : "";
            _matches.text = note.HasValue ? (note.Value.Matches.Count == 0 ? Label("Unassigned") :
                string.Join("\n", note.Value.Matches.Select(m => m.Name))) : "—";
            var matchRect = _matches.rectTransform;
            matchRect.offsetMin = new Vector2(matchRect.offsetMin.x,
                matchRect.offsetMax.y - Mathf.Max(122, _matches.preferredHeight + 8));
            var latestRect = _latestMessage.rectTransform;
            latestRect.offsetMax = new Vector2(latestRect.offsetMax.x, matchRect.offsetMin.y - 12);
            latestRect.offsetMin = new Vector2(latestRect.offsetMin.x, latestRect.offsetMax.y - 36);
            _detailScroll.content.sizeDelta = new Vector2(0,
                Mathf.Max(_detailScroll.viewport.rect.height, -latestRect.offsetMin.y + 18));
            var latest = _capture.LatestRecord;
            if (latest is { } last)
            {
                string raw = $"{last.Message.Status:X2} {last.Message.Data1:X2}" +
                    (last.Message.DataLength == 2 ? $" {last.Message.Data2:X2}" : "");
                _latestMessage.text = $"{InputText(last)} · {last.Message.Value}\nRAW {raw} · CH {last.Message.Channel + 1}";
            }
            else _latestMessage.text = Label("WaitingMessages");
            RefreshControllers();
            var records = _capture.History;
            bool changed = records.Length != _records.Length ||
                (records.Length > 0 && (_records.Length == 0 || records[records.Length - 1].Sequence != _records[0].Sequence));
            if (changed)
            {
                _records = records.Reverse().ToArray();
                _history.content.sizeDelta = new Vector2(0, _records.Length * ROW_HEIGHT);
                RefreshRows();
            }
            _recordLabel.text = _capture.IsRecording ? Label("RecordingOn") : Label("RecordingOff");
            string status = !string.IsNullOrEmpty(_actionError) ? _actionError : !string.IsNullOrEmpty(_capture.Error) ? _capture.Error :
                string.IsNullOrEmpty(_capture.LogPath) ? Label("LoggingOff") : _capture.LogPath;
            _status.text = _capture.DroppedRecords > 0 ? $"{Label("Dropped")}: {_capture.DroppedRecords} · {status}" : status;
        }

        private void RefreshControllers()
        {
            var observed = _capture.ObservedControllers;
            _updatingControllers = true;
            try
            {
                if (!_controllers.SequenceEqual(observed))
                {
                    _controllers = observed;
                    _pedalController.ClearOptions();
                    var options = new List<string> { Label("ChoosePedal") };
                    options.AddRange(_controllers.Select(a => $"P{a.PortId} · CH {a.Channel + 1} · CC {a.Number} · {_capture.Configuration.Endpoints.FirstOrDefault(e => e.PortId == a.PortId).PortName}"));
                    _pedalController.AddOptions(options);
                }
                int selected = _capture.CurrentPedalAddress is { } current ? Array.IndexOf(_controllers, current) + 1 : 0;
                _pedalController.SetValueWithoutNotify(Math.Max(0, selected));
            }
            finally { _updatingControllers = false; }
        }

        private void RefreshRows()
        {
            int first = Mathf.Max(0, Mathf.FloorToInt(_history.content.anchoredPosition.y / ROW_HEIGHT));
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                int index = first + i;
                row.Root.gameObject.SetActive(index < _records.Length);
                if (index >= _records.Length) continue;
                var record = _records[index];
                row.Root.anchoredPosition = new Vector2(0, -index * ROW_HEIGHT);
                row.Time.text = (record.Message.ReceiptMilliseconds / 1000).ToString("F3", CultureInfo.InvariantCulture);
                string input = InputText(record);
                row.Input.text = $"{input}\nCH {record.Message.Channel + 1} · P{record.Message.PortId} · {record.Message.PortName}";
                row.Value.text = record.Message.Value.ToString(CultureInfo.InvariantCulture);
                string state = record.PedalClosed.HasValue ? (record.PedalClosed.Value ? Label("Closed") : Label("Open")) : Label("Unknown");
                row.Pedal.text = record.PedalController is { } pedal ? $"CC {pedal.Number}: {record.PedalValue?.ToString() ?? "—"}\n{state}" : state;
            }
        }

        public void Back()
        {
            if (MenuManager.Instance != null) MenuManager.Instance.PopMenu();
            else gameObject.SetActive(false);
        }

        private void ReleaseCapture()
        {
            _capture?.Dispose();
            _capture = null;
            using (Navigator.Instance?.PushInputBlocker())
            {
                foreach (var scope in _menuInputScopes) scope.Dispose();
                _menuInputScopes.Clear();
            }
        }

        private void OnDisable()
        {
            ReleaseCapture();
            if (_ownsScheme) Navigator.Instance?.PopScheme();
            _ownsScheme = false;
            NavigationGroup.RemoveFromNavigationStack(_navigation);
        }

        private void OnDestroy() => ReleaseCapture();
    }
}
