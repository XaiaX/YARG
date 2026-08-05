// pattern: Imperative Shell

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace YARG.Menu.Maestro
{
    /// <summary>
    /// Forwards pointer entry over an otherwise empty pane surface to the page's
    /// navigation state. Child controls still receive their own pointer events.
    /// </summary>
    public sealed class MaestroPaneHoverTarget : MonoBehaviour, IPointerEnterHandler
    {
        private Action _callback;

        public void SetCallback(Action callback)
        {
            _callback = callback;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _callback?.Invoke();
        }
    }
}
