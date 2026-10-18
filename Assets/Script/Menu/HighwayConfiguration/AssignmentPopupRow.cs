using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Menu.Navigation;

namespace YARG.Menu.HighwayConfiguration
{
    /// <summary>Bridges an authored difficulty row to the assignment popup's Unity Button API.</summary>
    public sealed class AssignmentPopupRow : RuntimeNavigatable
    {
        [SerializeField] private Image _selectionFill;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Sprite _ordinarySelectionSprite;
        [SerializeField] private Color _ordinarySelectionColor = Color.white;
        [SerializeField] private Color _ordinaryTextColor = Color.white;
        [SerializeField] private bool _actionStyle;

        private static readonly Color ACTION_COLOR = new Color32(0x00, 0xD3, 0xFF, 0xFF);
        private static readonly Color ACTION_SELECTED_TEXT = new Color32(0x01, 0x22, 0x27, 0xFF);

        // Called by the editor builder after cloning the production difficulty visuals.
        public void Configure(Image selectionFill, TMP_Text label, Sprite ordinarySelectionSprite,
            Color ordinarySelectionColor)
        {
            _selectionFill = selectionFill;
            _label = label;
            _ordinarySelectionSprite = ordinarySelectionSprite;
            _ordinarySelectionColor = ordinarySelectionColor;
            _ordinaryTextColor = label.color;
            ApplySelection(false);
        }

        protected override void Awake()
        {
            base.Awake();
            SelectOnHover = true;
            SelectionVisual = ApplySelection;
            ApplySelection(false);
        }

        /// <summary>Use instead of RuntimeNavigatable.Attach for popup buttons.</summary>
        public RuntimeNavigatable AttachNavigation(Action confirm)
        {
            ConfirmCallback = confirm;
            return this;
        }

        /// <summary>Actions use the same cyan fill and dark selected text as difficulty-select Done.</summary>
        public void SetActionStyle(bool actionStyle, Color actionColor)
        {
            _actionStyle = actionStyle;
            _actionColor = actionColor;
            ApplySelection(Selected);
        }

        private Color _actionColor = ACTION_COLOR;

        public void ResetSelection()
        {
            SetSelected(false, SelectionOrigin.Programmatically);
            ApplySelection(false);
        }

        private void ApplySelection(bool selected)
        {
            selected &= GetComponent<Button>().interactable;
            if (_selectionFill == null || _label == null) return;
            _selectionFill.sprite = _actionStyle ? null : _ordinarySelectionSprite;
            _selectionFill.color = _actionStyle ? _actionColor : _ordinarySelectionColor;
            _selectionFill.gameObject.SetActive(selected);
            _label.color = _actionStyle
                ? (selected ? ACTION_SELECTED_TEXT : _actionColor)
                : _ordinaryTextColor;
            if (selected) RevealRow();
        }

        public void RevealRow()
        {
            var scroll = GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || scroll.viewport == null || !scroll.vertical) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
            var row = (RectTransform) transform.parent;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, row);
            var viewport = scroll.viewport.rect;
            float offset = bounds.max.y > viewport.yMax ? viewport.yMax - bounds.max.y
                : bounds.min.y < viewport.yMin ? viewport.yMin - bounds.min.y : 0f;
            scroll.StopMovement();
            // Bounds are measured in viewport space; anchoredPosition belongs to the content's parent.
            var worldOffset = scroll.viewport.TransformVector(new Vector3(0f, offset, 0f));
            var parentOffset = scroll.content.parent.InverseTransformVector(worldOffset);
            scroll.content.anchoredPosition += new Vector2(parentOffset.x, parentOffset.y);
            Canvas.ForceUpdateCanvases();
            foreach (var mask in scroll.GetComponentsInChildren<RectMask2D>(true))
            {
                if (mask.isActiveAndEnabled) mask.PerformClipping();
            }
        }
    }
}
