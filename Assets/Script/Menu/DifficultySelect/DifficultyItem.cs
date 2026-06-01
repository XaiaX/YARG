using TMPro;
using UnityEngine;
using UnityEngine.Events;
using YARG.Menu.Navigation;

namespace YARG.Menu.DifficultySelect
{
    public class DifficultyItem : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _header;
        [SerializeField]
        private TextMeshProUGUI _body;

        [field: SerializeField]
        public NavigatableButton Button { get; private set; }

        public void Initialize(string header, string body, UnityAction action)
        {
            _header.gameObject.SetActive(true);
            _header.text = header;

            _body.text = body;
            Button.SetOnClickEvent(action);
        }

        public void Initialize(string body, UnityAction action)
        {
            _header.gameObject.SetActive(false);

            _body.text = body;
            Button.SetOnClickEvent(action);
        }

        /// <summary>
        /// Dims the item and disables interaction (used to show a fixed, non-editable
        /// choice). Adds a CanvasGroup at runtime so no prefab change is needed.
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            var group = GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = interactable ? 1f : 0.3f;
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }
    }
}