// pattern: Imperative Shell

using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Helpers.UI;
using YARG.Menu.Navigation;

namespace YARG.Menu.Maestro
{
    /// <summary>
    /// Keyboard/controller-navigable wrapper for a <see cref="TMP_InputField"/> used in
    /// the Maestro editor.  When selected the field shows a focus border; pressing
    /// Confirm activates the input for typing and pushes a temporary scheme whose
    /// Up/Down increment or decrement the value by a configured step.  Typed input is
    /// handled by the <see cref="TMP_InputField"/> itself; the parent menu listens to
    /// <c>onEndEdit</c> to parse, clamp, and stage the result.
    /// </summary>
    public sealed class MaestroInputNavigatable : NavigatableBehaviour
    {
        private static readonly Color SelectedTextColor =
            new(1f, 0.83137256f, 0.22745098f, 1f);

        [SerializeField] private Image _focusBorder;

        private TMP_InputField _inputField;
        private NavigationScheme _editScheme;
        private Color _defaultTextColor;
        private bool _colorCaptured;

        // Float configuration (speed / length)
        private bool _isFloat = true;
        private float _floatStep = 0.1f;
        private float _floatMin;
        private float _floatMax = 100f;
        private float _floatRound = 0.1f;

        // Integer configuration (calibration)
        private long _intStep = 1;
        private long _intMin = long.MinValue;
        private long _intMax = long.MaxValue;

        public static MaestroInputNavigatable Attach(TMP_InputField inputField)
        {
            if (inputField == null)
                return null;

            var parent = inputField.transform.parent.gameObject;
            var navigatable = parent.GetComponent<MaestroInputNavigatable>();
            if (navigatable == null)
                navigatable = parent.AddComponent<MaestroInputNavigatable>();

            navigatable.Initialize(inputField);
            return navigatable;
        }

        // The prefab does not have the base class's serialized selected visual.
        protected override void Awake()
        {
        }

        private void Initialize(TMP_InputField inputField)
        {
            _inputField = inputField;
            CaptureDefaultColor();

            // We manage navigation ourselves during editing, so the TextFieldNavigationDisabler
            // (which pushes an empty scheme on focus) must be disabled to avoid blocking
            // the increment/decrement scheme.
            var disabler = _inputField.GetComponent<TextFieldNavigationDisabler>();
            if (disabler != null)
                disabler.enabled = false;

            _inputField.onEndEdit.AddListener(OnEndEdit);

            if (_focusBorder == null)
                CreateFocusBorder();
        }

        private void CaptureDefaultColor()
        {
            if (!_colorCaptured && _inputField != null && _inputField.textComponent != null)
            {
                _defaultTextColor = _inputField.textComponent.color;
                _colorCaptured = true;
            }
        }

        private void CreateFocusBorder()
        {
            var borderGo = new GameObject("FocusBorder");
            borderGo.transform.SetParent(transform, false);
            var rt = borderGo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-6, -6);
            rt.offsetMax = new Vector2(6, 6);
            var img = borderGo.AddComponent<Image>();
            img.sprite = SpriteHelper.GetRoundedRect(18, 2);
            img.type = Image.Type.Sliced;
            img.color = SelectedTextColor;
            img.raycastTarget = false;
            _focusBorder = img;
            borderGo.SetActive(false);
        }

        public void ConfigureFloat(float step, float min, float max, float round)
        {
            _isFloat = true;
            _floatStep = step;
            _floatMin = min;
            _floatMax = max;
            _floatRound = round;
        }

        public void ConfigureInteger(long step, long min, long max)
        {
            _isFloat = false;
            _intStep = step;
            _intMin = min;
            _intMax = max;
        }

        protected override void OnSelectionChanged(bool selected)
        {
            CaptureDefaultColor();

            if (_inputField != null && _inputField.textComponent != null)
            {
                var color = selected ? SelectedTextColor : _defaultTextColor;
                color.a = _defaultTextColor.a;
                _inputField.textComponent.color = color;
            }

            if (_focusBorder != null)
                _focusBorder.gameObject.SetActive(selected && _editScheme == null);
        }

        public override void Confirm()
        {
            if (_inputField == null || !_inputField.interactable)
                return;
            EnterEditMode();
        }

        private void EnterEditMode()
        {
            if (_inputField == null || _editScheme != null)
                return;

            _inputField.ActivateInputField();

            if (_focusBorder != null)
                _focusBorder.gameObject.SetActive(false);

            // Only capture Up/Down for increment/decrement.  Green/Red are
            // intentionally absent so that number keys (mapped to fret buttons)
            // pass through to the TMP_InputField for normal text entry.  Enter
            // and Escape are handled by the EventSystem → TMP_InputField, which
            // fires onEndEdit (submit or restore-on-escape) and cleans up the
            // scheme via the listener registered in Initialize.
            _editScheme = new NavigationScheme(new()
            {
                new(MenuAction.Up, "Menu.Common.Up", _ => Adjust(+1)),
                new(MenuAction.Down, "Menu.Common.Down", _ => Adjust(-1)),
            }, null);
            Navigator.Instance?.PushScheme(_editScheme);
        }

        /// <summary>
        /// Called by the TMP_InputField when it loses focus (Enter key, Escape,
        /// click-away, or explicit deactivation).  Ensures the edit scheme is
        /// always cleaned up.  Enter submits the current text; Escape restores
        /// the original (handled internally by TMP_InputField via
        /// m_RestoreOriginalTextOnEscape) before firing this callback.
        /// </summary>
        private void OnEndEdit(string _)
        {
            RemoveEditScheme();
        }

        private void RemoveEditScheme()
        {
            if (_editScheme == null)
                return;

            var scheme = _editScheme;
            _editScheme = null;
            Navigator.Instance?.RemoveScheme(scheme);

            if (_focusBorder != null && Selected)
                _focusBorder.gameObject.SetActive(true);
        }

        private void Adjust(int direction)
        {
            if (_inputField == null)
                return;

            if (_isFloat)
            {
                float current = float.TryParse(_inputField.text, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out float v) ? v : 0f;
                current += direction * _floatStep;
                current = Mathf.Clamp(current, _floatMin, _floatMax);
                current = Mathf.Round(current / _floatRound) * _floatRound;
                _inputField.text = current.ToString("0.0", CultureInfo.CurrentCulture);
            }
            else
            {
                long current = long.TryParse(_inputField.text, NumberStyles.Integer,
                    CultureInfo.CurrentCulture, out long v) ? v : 0;
                current += direction * _intStep;
                current = Math.Clamp(current, _intMin, _intMax);
                _inputField.text = current.ToString(CultureInfo.CurrentCulture);
            }
        }

        protected override void OnDestroy()
        {
            RemoveEditScheme();
            if (_inputField != null)
                _inputField.onEndEdit.RemoveListener(OnEndEdit);
            base.OnDestroy();
        }
    }
}
