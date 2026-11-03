// pattern: Imperative Shell
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using YARG.Menu.Navigation;

namespace YARG.Menu.DifficultySelect
{
    public class ModifierItem : NavigatableBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _title;
        [SerializeField]
        private GameObject _checkedToggle;

        private bool _active;
        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                _checkedToggle.SetActive(_active);
            }
        }

        private Action<bool> _activeChangedCallback;
        public bool Interactable { get; private set; } = true;

        public void Initialize(string title, bool active, Action<bool> activeChangedCallback)
        {
            SetInteractable(true);
            _title.text = title;
            _activeChangedCallback = activeChangedCallback;

            Active = active;
        }

        public void SetInteractable(bool interactable)
        {
            Interactable = interactable;
            var group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = interactable ? 1f : 0.45f;
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactable || !CanReceiveInput) return;
            base.OnPointerDown(eventData);
            Confirm();
        }

        public override void Confirm()
        {
            if (!Interactable || !CanReceiveInput) return;
            base.Confirm();

            Active = !Active;

            _activeChangedCallback?.Invoke(Active);
        }
    }
}
