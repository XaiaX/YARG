using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using YARG.Audio.BASS;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Input;
using UnityEngine.Events;
using YARG.Input;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Data;
using YARG.Menu.Dialogs;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings;
using YARG.Menu.Settings.Visuals;
using UnityEngine.EventSystems;

namespace YARG.Menu.ProfileList
{
    public class PartyMicrophoneSetup : MonoBehaviour
    {
        private sealed class MicrophoneRow
        {
            public int Index;
            public TextMeshProUGUI Label;
            public RawImage Needle;
            public RectTransform Level;
            public MicDevice Device;
            public IDisposable Preview;
            public float DisplayLevel;
            public bool? Connected;
            public bool Detailed;
            public RectTransform Threshold;
            public int? DeviceIndex;
        }

        private static readonly Color EDITING_COLOR = RuntimeNavigatable.SelectedTextColor;
        private readonly List<MicrophoneRow> _rows = new();
        private readonly List<List<int>> _rowNavigation = new();
        private readonly List<AsyncOperationHandle<Texture2D>> _textures = new();
        private ProfileBindings _bindings;
        private ListDialog _dialog;
        private NavigationGroup _navigation;
        private ScrollRect _scrollRect;
        private ProfileView _profileView;
        private ValueSlider _sensitivity;
        private bool _editingSensitivity;
        private bool _detail;
        private AsyncOperationHandle<GameObject>? _sliderTemplate;
        private TMP_FontAsset _regularFont;
        private TextMeshProUGUI _sensitivityHelp;
        private Slider _sliderControl;
        private RectTransform _thresholdGhost;
        private float _idleThreshold;
        private float _originalThreshold;
        private int _detailSlot;

        public void Show(ProfileBindings bindings, ProfileView profileView)
        {
            Close();
            _bindings = bindings;
            _profileView = profileView;
            ShowMicrophones();
        }

        public void Close()
        {
            ClearView();
            if (_dialog != null && _dialog.IsOpen && Navigator.Instance != null && DialogManager.Instance != null)
            {
                DialogManager.Instance.ClearDialog();
            }
            _dialog = null;
            _bindings = null;
        }

        private void OnDisable()
        {
            Close();
        }

        private void ClearView()
        {
            if (_sensitivity != null)
            {
                // Commit pending numeric text while this view's callbacks and bindings are still valid.
                var input = _sensitivity.GetComponentInChildren<TMP_InputField>(true);
                input.DeactivateInputField();
                if (EventSystem.current != null && !EventSystem.current.alreadySelecting &&
                    EventSystem.current.currentSelectedGameObject == input.gameObject)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
                input.onSelect.RemoveAllListeners();
                input.onEndEdit.RemoveAllListeners();
                _sensitivity.ValueChanged.RemoveAllListeners();
                var pointer = _sensitivity.GetComponentInChildren<SliderPointerForwarder>(true);
                if (pointer != null)
                {
                    pointer.OnDown = null;
                    pointer.OnUp = null;
                }
                PlayerContainer.SaveProfiles();
            }
            if (_navigation != null) _navigation.SelectionChanged -= RevealSelectedRow;
            _navigation = null;
            _scrollRect = null;
            foreach (var row in _rows)
            {
                row.Preview?.Dispose();
            }
            _rows.Clear();
            _rowNavigation.Clear();
            foreach (var texture in _textures)
            {
                Addressables.Release(texture);
            }
            _textures.Clear();
            if (_sliderTemplate.HasValue) Addressables.Release(_sliderTemplate.Value);
            _sliderTemplate = null;
            _sensitivity = null;
            _sensitivityHelp = null;
            _sliderControl = null;
            _thresholdGhost = null;
            _editingSensitivity = false;
        }

        private void OpenDialog(string title, bool back)
        {
            ClearView();
            if (_dialog != null && _dialog.IsOpen)
            {
                DialogManager.Instance.ClearDialog();
            }
            _sliderTemplate = Addressables.LoadAssetAsync<GameObject>("Setting/Slider");
            _regularFont = _sliderTemplate.Value.WaitForCompletion().GetComponent<SliderSettingVisual>().SettingLabel.font;
            _dialog = DialogManager.Instance.ShowList(title);
            var panel = (RectTransform) _dialog.Title.transform.parent;
            panel.anchorMin = new Vector2(0.5f, panel.anchorMin.y);
            panel.anchorMax = new Vector2(0.5f, panel.anchorMax.y);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Min(900f, ((RectTransform) _dialog.transform).rect.width - 100f));
            if (back)
            {
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Min(panel.rect.height + 80f, ((RectTransform) _dialog.transform).rect.height - 100f));
            }
            _dialog.ClearButtons();
            _navigation = _dialog.GetComponentInChildren<NavigationGroup>();
            _scrollRect = _dialog.GetComponentInChildren<ScrollRect>();
            _navigation.SelectionChanged += RevealSelectedRow;
            _detail = back;
            // Replace the dialog scheme at the same stack depth, so its normal close owns the pop.
            Navigator.Instance.PopScheme();
            Navigator.Instance.PushSchemeImmediate(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up", () => Navigate(-1, false)),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down", () => Navigate(1, false)),
                new NavigationScheme.Entry(MenuAction.Left, "Menu.Common.Left", () => Navigate(-1, true)),
                new NavigationScheme.Entry(MenuAction.Right, "Menu.Common.Right", () => Navigate(1, true)),
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm", () => _navigation.ConfirmSelection()),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", () =>
                {
                    if (_editingSensitivity) SetSensitivityEditing(false);
                    else if (_detail) ShowMicrophones();
                    else Close();
                })
            }, null));
            _dialog.AddDialogButton(back ? "Menu.Common.Back" : "Menu.Common.Close",
                MenuData.Colors.CancelButton, () =>
                {
                    if (back) ShowMicrophones();
                    else Close();
                });
        }

        private void RevealSelectedRow(NavigatableBehaviour selected, SelectionOrigin origin)
        {
            if (origin != SelectionOrigin.Navigation || selected == null || _scrollRect == null ||
                !selected.transform.IsChildOf(_scrollRect.content))
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            var viewport = _scrollRect.viewport != null ? _scrollRect.viewport : (RectTransform) _scrollRect.transform;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, selected.transform);
            float adjustment = 0f;
            if (bounds.max.y > viewport.rect.yMax)
            {
                adjustment = viewport.rect.yMax - bounds.max.y;
            }
            else if (bounds.min.y < viewport.rect.yMin)
            {
                adjustment = viewport.rect.yMin - bounds.min.y;
            }

            var position = _scrollRect.content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + adjustment, 0f, Mathf.Max(0f, _scrollRect.ScrollableHeight()));
            _scrollRect.StopMovement();
            _scrollRect.content.anchoredPosition = position;
        }

        private void Navigate(int direction, bool horizontal)
        {
            if (_editingSensitivity && _sensitivity != null)
            {
                _sensitivity.Value += (horizontal ? direction : -direction) * 0.5f;
                return;
            }
            int current = _navigation.SelectedIndex ?? 0;
            if (!_detail)
            {
                int row = _rowNavigation.FindIndex(indices => indices.Contains(current));
                if (horizontal)
                {
                    if (row >= 0)
                    {
                        var indices = _rowNavigation[row];
                        int column = Mathf.Clamp(indices.IndexOf(current) + direction, 0, indices.Count - 1);
                        _navigation.SelectAt(indices[column], SelectionOrigin.Navigation);
                    }
                }
                else if (_rowNavigation.Count > 0)
                {
                    int nextRow = row < 0 ? (direction > 0 ? 0 : _rowNavigation.Count - 1) : row + direction;
                    _navigation.SelectAt(nextRow < 0 || nextRow >= _rowNavigation.Count ? 0 : _rowNavigation[nextRow][0],
                        SelectionOrigin.Navigation);
                }
                return;
            }
            if (_detail && current >= 2)
            {
                int cell = current - 2;
                int next;
                if (horizontal)
                {
                    next = 2 + cell / 4 * 4 + Mathf.Clamp(cell % 4 + direction, 0, 3);
                }
                else if (direction < 0) next = cell < 4 ? 1 : current - 4;
                else next = cell < 4 ? current + 4 : 0;
                _navigation.SelectAt(next, SelectionOrigin.Navigation);
                return;
            }
            if (horizontal) return;
            if (direction < 0) _navigation.SelectPrevious(false);
            else _navigation.SelectNext(false);
        }

        private void AddNavigation(ColoredButton button)
        {
            // Match the settings row fill while keeping the saved needle marker independent of focus.
            var control = button.GetComponentInChildren<Button>(true);
            var background = (Image) control.targetGraphic;
            var fill = CreateImage("Selected Background", background.transform, new Color(0.035f, 0.3f, 0.42f, 0.9f));
            fill.sprite = background.sprite;
            fill.type = background.type;
            fill.pixelsPerUnitMultiplier = background.pixelsPerUnitMultiplier;
            fill.transform.SetAsFirstSibling();
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            fill.gameObject.SetActive(false);
            button.Text.transform.SetAsLastSibling();
            var nav = button.gameObject.AddComponent<RuntimeNavigatable>();
            nav.SelectionVisual = fill.gameObject.SetActive;
            nav.ConfirmCallback = () =>
            {
                if (button.GetComponentInChildren<Button>(true).interactable) button.OnClick.Invoke();
            };
            _navigation.AddNavigatable(nav);
        }

        private ColoredButton AddAction(string text, Action action, bool navigatable = true)
        {
            var button = _dialog.AddListButton(text, () => action(), closeOnClick: false);
            if (navigatable) AddNavigation(button);
            return button;
        }

        private void ShowMicrophones(int selectedSlot = 0)
        {
            OpenDialog(Localize.Key("Menu.ProfileList.MicrophoneSetup"), false);
            for (int i = 0; i < _bindings.MicrophoneSlots.Count; i++)
            {
                int index = i;
                var slot = _bindings.MicrophoneSlots[i];
                var button = AddAction(string.Empty, () => ShowMicrophone(index));
                _rowNavigation.Add(new List<int> { _navigation.Count - 1 });
                var layout = button.gameObject.AddComponent<LayoutElement>();
                layout.preferredHeight = 80f;
                ((RectTransform) button.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 80f);
                var label = button.Text;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.fontSize = 22f;
                label.enableAutoSizing = false;
                label.rectTransform.offsetMin = new Vector2(80f, 12f);
                label.rectTransform.offsetMax = new Vector2(-120f, -6f);
                var needle = AddNeedle(button, _bindings.GetMicrophoneNeedleId(i));
                var background = CreateImage("Input level", button.transform, new Color(1f, 1f, 1f, 0.15f));
                background.rectTransform.anchorMin = background.rectTransform.anchorMax = new Vector2(0f, 0f);
                background.rectTransform.pivot = Vector2.zero;
                background.rectTransform.anchoredPosition = new Vector2(80f, 8f);
                background.rectTransform.sizeDelta = new Vector2(120f, 4f);
                var level = CreateImage("Level", background.transform, new Color(0f, 0.867f, 0.984f));
                level.rectTransform.anchorMin = level.rectTransform.anchorMax = Vector2.zero;
                level.rectTransform.pivot = Vector2.zero;
                level.rectTransform.sizeDelta = new Vector2(0f, 4f);
                _rows.Add(new MicrophoneRow { Index = i, Label = label, Needle = needle, Level = level.rectTransform });
                AddMoveButton(button, _profileView.MicrophoneMoveUpTemplate, index, -1, -85f);
                AddMoveButton(button, _profileView.MicrophoneMoveDownTemplate, index, 1, -35f);
            }
            if (_rows.Count == 0)
            {
                var empty = AddAction(Localize.Key("Menu.ProfileList.NoMicrophones"), () => { });
                empty.DisableButton();
            }
            _navigation.SelectAt(_rowNavigation.Count > 0 ? _rowNavigation[Mathf.Clamp(selectedSlot, 0, _rowNavigation.Count - 1)][0] : 0);
            Update();
        }

        private void AddMoveButton(ColoredButton row, IconButton template, int index, int direction, float x)
        {
            var button = Instantiate(template, row.transform);
            var control = button.GetComponentInChildren<Button>(true);
            control.onClick = new Button.ButtonClickedEvent();
            control.interactable = index + direction >= 0 && index + direction < _bindings.MicrophoneSlots.Count;
            control.onClick.AddListener(() => Move(index, index + direction));
            var rect = (RectTransform) button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(42f, 42f);
            if (control.interactable)
            {
                var normalColor = button.BackgroundColor;
                var nav = button.gameObject.AddComponent<RuntimeNavigatable>();
                nav.SelectionVisual = selected => button.BackgroundColor = selected
                    ? new Color(0.035f, 0.3f, 0.42f, 0.9f) : normalColor;
                nav.ConfirmCallback = () => control.onClick.Invoke();
                _navigation.AddNavigatable(nav);
                _rowNavigation[index].Add(_navigation.Count - 1);
            }
        }

        private void ShowMicrophone(int index)
        {
            var slot = _bindings.MicrophoneSlots[index];
            OpenDialog(MicrophoneName(index), true);
            _detailSlot = index;
            _idleThreshold = GetThreshold(index);
            AddDetailedMeter(index);
            AddSensitivity(index);
            var grid = AddAction(string.Empty, () => { }, false);
            grid.GetComponentInChildren<Button>(true).interactable = false;
            ((RectTransform) grid.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 160f);
            grid.Text.gameObject.SetActive(false);
            foreach (var background in grid.GetComponentsInChildren<Image>(true)) background.enabled = false;
            var assignments = MicrophoneNeedleAssignments.Resolve(_bindings.MicrophoneSlots);
            for (int i = 0; i <= 7; i++)
            {
                int needleId = i;
                var explicitOwners = new List<int>();
                var automaticOwners = new List<int>();
                for (int mic = 0; i > 0 && mic < assignments.Length; mic++)
                {
                    if (mic == index || assignments[mic] != i) continue;
                    if (_bindings.MicrophoneSlots[mic].NeedleId == i) explicitOwners.Add(mic + 1);
                    else automaticOwners.Add(mic + 1);
                }
                bool unavailable = explicitOwners.Count > 0;
                var button = AddAction(i == 0 ? Localize.Key("Menu.ProfileList.AutomaticNeedle") : string.Empty, () =>
                {
                    if (unavailable) return;
                    _bindings.SetMicrophoneNeedle(index, needleId);
                    PlayerContainer.SaveProfiles();
                    ShowMicrophone(index);
                });
                button.GetComponentInChildren<Button>(true).interactable = !unavailable;
                var rect = (RectTransform) button.transform;
                rect.SetParent(grid.transform, false);
                int column = i % 4;
                int row = i / 4;
                rect.anchorMin = new Vector2(column / 4f, 0.5f - row * 0.5f);
                rect.anchorMax = new Vector2((column + 1) / 4f, 1f - row * 0.5f);
                rect.offsetMin = new Vector2(5f, 5f);
                rect.offsetMax = new Vector2(-5f, -5f);
                SetRegularText(button.Text, i == 0 ? 28f : 20f);
                button.Text.alignment = TextAlignmentOptions.Center;
                if (i > 0)
                {
                    var needle = AddNeedle(button, i);
                    if (unavailable) needle.color = new Color(1f, 1f, 1f, 0.3f);
                    needle.rectTransform.anchorMin = needle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    needle.rectTransform.anchoredPosition = new Vector2(0f, -5f);
                    needle.rectTransform.sizeDelta = new Vector2(63f, 35f);
                }
                bool explicitChoice = slot.NeedleId == i;
                bool automaticChoice = slot.NeedleId == 0 && i == assignments[index];
                if (explicitChoice || automaticChoice || unavailable || automaticOwners.Count > 0)
                {
                    var selected = Instantiate(button.Text, button.transform);
                    selected.text = unavailable ? string.Join(", ", explicitOwners)
                        : explicitChoice || automaticChoice ? "✓" : string.Join(", ", automaticOwners);
                    selected.fontSize = 20f;
                    selected.alignment = TextAlignmentOptions.TopLeft;
                    selected.color = unavailable ? Color.white
                        : explicitChoice ? new Color(0f, 0.867f, 0.984f) : new Color(0.65f, 0.65f, 0.65f, 0.6f);
                    selected.rectTransform.offsetMin = new Vector2(8f, 8f);
                    selected.rectTransform.offsetMax = new Vector2(-8f, -8f);
                }
            }
            _navigation.SelectAt(2 + slot.NeedleId);
            Update();
        }

        private static float MeterPosition(float decibels)
        {
            float normalized = Mathf.Clamp01((decibels + 96f) / 96f);
            return normalized * normalized;
        }

        private void SetRegularText(TextMeshProUGUI text, float size)
        {
            text.font = _regularFont;
            text.fontSharedMaterial = _regularFont.material;
            text.fontStyle = FontStyles.Normal;
            text.enableAutoSizing = false;
            text.fontSize = size;
        }

        private void AddDetailedMeter(int index)
        {
            var panel = AddAction(string.Empty, () => { }, false);
            panel.GetComponentInChildren<Button>(true).interactable = false;
            foreach (var image in panel.GetComponentsInChildren<Image>(true)) image.enabled = false;
            ((RectTransform) panel.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 105f);
            SetRegularText(panel.Text, 20f);
            panel.Text.alignment = TextAlignmentOptions.TopLeft;
            panel.Text.rectTransform.offsetMin = new Vector2(20f, 0f);
            panel.Text.rectTransform.offsetMax = new Vector2(-20f, -4f);
            var background = CreateImage("Input level", panel.transform, new Color(1f, 1f, 1f, 0.15f));
            var rect = background.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(20f, 0f);
            rect.offsetMax = new Vector2(-20f, 12f);
            var level = CreateImage("Level", background.transform, new Color(0f, 0.867f, 0.984f));
            level.rectTransform.anchorMin = Vector2.zero;
            level.rectTransform.anchorMax = Vector2.one;
            level.rectTransform.offsetMin = level.rectTransform.offsetMax = Vector2.zero;
            var ghost = CreateImage("Original pitch threshold", background.transform, new Color(0.9f, 0.9f, 0.85f, 0.65f));
            _thresholdGhost = ghost.rectTransform;
            _thresholdGhost.sizeDelta = new Vector2(3f, 22f);
            ghost.gameObject.SetActive(false);
            var threshold = CreateImage("Pitch threshold", background.transform, MenuData.Colors.ConfirmButton);
            threshold.rectTransform.sizeDelta = new Vector2(3f, 22f);
            int[] ticks = { -96, -60, -36, -18, 0 };
            for (int i = 0; i < ticks.Length; i++)
            {
                var tick = Instantiate(panel.Text, background.transform);
                tick.text = ticks[i].ToString();
                tick.fontSize = 16f;
                tick.alignment = i == 0 ? TextAlignmentOptions.TopLeft : i == ticks.Length - 1 ? TextAlignmentOptions.TopRight : TextAlignmentOptions.Top;
                float position = MeterPosition(ticks[i]);
                tick.rectTransform.anchorMin = tick.rectTransform.anchorMax = new Vector2(position, 0f);
                tick.rectTransform.pivot = new Vector2(i == 0 ? 0f : i == ticks.Length - 1 ? 1f : 0.5f, 1f);
                tick.rectTransform.anchoredPosition = new Vector2(0f, -9f);
                tick.rectTransform.sizeDelta = new Vector2(60f, 24f);
            }
            _rows.Add(new MicrophoneRow { Index = index, Label = panel.Text, Level = level.rectTransform,
                Threshold = threshold.rectTransform, Detailed = true });
        }

        private void AddSensitivity(int index)
        {
            var panel = AddAction(string.Empty, () => SetSensitivityEditing(!_editingSensitivity));
            ((RectTransform) panel.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 140f);
            var field = (RectTransform) panel.GetComponentInChildren<Button>(true).transform;
            field.anchorMin = Vector2.zero;
            field.anchorMax = Vector2.one;
            field.offsetMin = new Vector2(0f, 38f);
            field.offsetMax = new Vector2(0f, -30f);
            panel.Text.transform.SetParent(panel.transform, false);
            SetRegularText(panel.Text, 20f);
            panel.Text.text = Localize.Key("Menu.ProfileList.SensitivityOffset");
            panel.Text.alignment = TextAlignmentOptions.TopLeft;
            panel.Text.rectTransform.anchorMin = new Vector2(0f, 1f);
            panel.Text.rectTransform.anchorMax = Vector2.one;
            panel.Text.rectTransform.offsetMin = new Vector2(20f, -28f);
            panel.Text.rectTransform.offsetMax = new Vector2(-20f, 0f);
            var template = _sliderTemplate.Value.Result.GetComponentInChildren<ValueSlider>(true);
            _sensitivity = Instantiate(template, field);
            _sensitivity.ValueChanged = new UnityEvent<float>();
            _sensitivity.MinimumValue = -50f;
            _sensitivity.MaximumValue = 50f;
            _sensitivity.WholeNumbers = false;
            _sensitivity.FormatString = "F1";
            _sensitivity.SetValueWithoutNotify(_bindings.MicrophoneSlots[index].SensitivityOffsetDb);
            _sensitivity.ValueChanged.AddListener(value =>
            {
                float quantized = Mathf.Round(value * 2f) / 2f;
                _sensitivity.SetValueWithoutNotify(quantized);
                _bindings.SetMicrophoneSensitivity(index, quantized);
            });
            var rect = (RectTransform) _sensitivity.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(20f, 0f);
            rect.offsetMax = new Vector2(-20f, 0f);
            _sensitivityHelp = Instantiate(panel.Text, panel.transform);
            SetRegularText(_sensitivityHelp, 16f);
            _sensitivityHelp.rectTransform.anchorMin = Vector2.zero;
            _sensitivityHelp.rectTransform.anchorMax = new Vector2(1f, 0f);
            _sensitivityHelp.rectTransform.offsetMin = new Vector2(20f, 0f);
            _sensitivityHelp.rectTransform.offsetMax = new Vector2(-20f, 30f);

            _sliderControl = _sensitivity.GetComponentInChildren<Slider>(true);
            var pointer = _sliderControl.gameObject.AddComponent<SliderPointerForwarder>();
            pointer.OnDown = () =>
            {
                _navigation.SelectAt(1, SelectionOrigin.Mouse);
                SetSensitivityEditing(true);
            };
            pointer.OnUp = () => SetSensitivityEditing(false);
            var input = _sensitivity.GetComponentInChildren<TMP_InputField>(true);
            input.onSelect.AddListener(_ =>
            {
                _navigation.SelectAt(1, SelectionOrigin.Mouse);
                SetSensitivityEditing(true);
            });
            input.onEndEdit.AddListener(_ =>
            {
                SetSensitivityEditing(false);
                if (EventSystem.current != null && !EventSystem.current.alreadySelecting &&
                    EventSystem.current.currentSelectedGameObject == input.gameObject)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            });
            SetSensitivityEditing(false);
        }

        private void SetSensitivityEditing(bool editing)
        {
            if (editing && !_editingSensitivity) _originalThreshold = _idleThreshold;
            _editingSensitivity = editing;
            if (_thresholdGhost != null)
            {
                float position = MeterPosition(_originalThreshold);
                _thresholdGhost.anchorMin = _thresholdGhost.anchorMax = new Vector2(position, 0.5f);
                _thresholdGhost.anchoredPosition = Vector2.zero;
                _thresholdGhost.gameObject.SetActive(editing);
            }
            if (_sliderControl != null)
            {
                var colors = _sliderControl.colors;
                colors.normalColor = colors.highlightedColor = colors.selectedColor = colors.pressedColor =
                    editing ? EDITING_COLOR : Color.white;
                _sliderControl.colors = colors;
                _sliderControl.targetGraphic.color = Color.white;
            }
        }

        private float GetThreshold(int index)
        {
            return SettingsManager.Settings.MicrophoneSensitivity.Value +
                _bindings.MicrophoneSlots[index].SensitivityOffsetDb - BassMicAnalyzer.CALIBRATION_GAIN_DB;
        }

        private string MicrophoneName(int index)
        {
            var slot = _bindings.MicrophoneSlots[index];
            var name = Localize.KeyFormat("Menu.ProfileList.MicrophoneSlot", index + 1, slot.Identity.Name);
            int? device = (slot.Device as BassMicDevice)?.DeviceIndex;
            var identity = device.HasValue && slot.IsConnected
                ? Localize.KeyFormat("Menu.ProfileList.MicrophoneDevice", device.Value + 1)
                : Localize.Key("Menu.ProfileList.MicrophoneDeviceUnknown");
            return name + " · " + identity;
        }

        private void Move(int from, int to)
        {
            _bindings.MoveMicrophoneSlot(from, to);
            PlayerContainer.SaveProfiles();
            ShowMicrophones(to);
        }

        private RawImage AddNeedle(ColoredButton button, int id)
        {
            var handle = Addressables.LoadAssetAsync<Texture2D>($"VocalNeedleTexture/{id}");
            _textures.Add(handle);
            var image = new GameObject("Needle", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(button.transform, false);
            image.texture = handle.WaitForCompletion();
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            image.rectTransform.anchoredPosition = new Vector2(40f, 0f);
            image.rectTransform.sizeDelta = new Vector2(54f, 30f);
            return image;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private void Update()
        {
            if (_dialog == null || !_dialog.IsOpen)
            {
                ClearView();
                return;
            }
            if (_sensitivityHelp != null)
            {
                _sensitivityHelp.text = Localize.KeyFormat("Menu.ProfileList.SensitivityHelp",
                    SettingsManager.Settings.MicrophoneSensitivity.Value.ToString("F1"));
            }
            if (_detail && !_editingSensitivity) _idleThreshold = GetThreshold(_detailSlot);
            foreach (var row in _rows)
            {
                var slot = _bindings.MicrophoneSlots[row.Index];
                var device = slot.IsConnected ? slot.Device : null;
                if (!ReferenceEquals(row.Device, device))
                {
                    row.Preview?.Dispose();
                    row.Device = device;
                    row.Connected = null;
                    row.Preview = (device as BassMicDevice)?.BeginLevelPreview();
                    row.DisplayLevel = 0f;
                }
                if (row.Detailed)
                {
                    var bass = device as BassMicDevice;
                    if (row.Connected != slot.IsConnected || row.DeviceIndex != bass?.DeviceIndex)
                    {
                        row.Connected = slot.IsConnected;
                        row.DeviceIndex = bass?.DeviceIndex;
                        _dialog.Title.text = MicrophoneName(row.Index);
                    }
                    float decibels = bass?.PreviewDecibels ?? -160f;
                    float threshold = bass?.EffectiveThresholdDbfs ??
                        SettingsManager.Settings.MicrophoneSensitivity.Value + slot.SensitivityOffsetDb - BassMicAnalyzer.CALIBRATION_GAIN_DB;
                    row.Label.text = Localize.KeyFormat("Menu.ProfileList.InputDecibels", threshold.ToString("F1"));
                    row.Level.anchorMax = new Vector2(MeterPosition(decibels), 1f);
                    float marker = MeterPosition(threshold);
                    row.Threshold.GetComponent<Image>().color = _editingSensitivity ? EDITING_COLOR : MenuData.Colors.ConfirmButton;
                    row.Threshold.anchorMin = row.Threshold.anchorMax = new Vector2(marker, 0.5f);
                    row.Threshold.anchoredPosition = Vector2.zero;
                    row.Threshold.gameObject.SetActive(true);
                }
                else
                {
                    int? deviceIndex = (device as BassMicDevice)?.DeviceIndex;
                    if (row.Connected != slot.IsConnected || row.DeviceIndex != deviceIndex)
                    {
                        row.Connected = slot.IsConnected;
                        row.DeviceIndex = deviceIndex;
                        var name = MicrophoneName(row.Index);
                        var status = Localize.Key(slot.IsConnected ? "Menu.ProfileList.MicrophoneConnected" : "Menu.ProfileList.MicrophoneMissing");
                        row.Label.text = name + "\n<size=75%>" + status + "</size>";
                        row.Needle.color = new Color(1f, 1f, 1f, slot.IsConnected ? 1f : 0.35f);
                    }
                    float level = device is BassMicDevice bass ? bass.PreviewLevel : 0f;
                    row.DisplayLevel = Mathf.Max(level, row.DisplayLevel - Time.unscaledDeltaTime * 2f);
                    row.Level.sizeDelta = new Vector2(120f * row.DisplayLevel, 4f);
                }
            }
        }
    }
}
