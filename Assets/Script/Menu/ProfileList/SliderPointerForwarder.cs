// pattern: Imperative Shell
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace YARG.Menu.ProfileList
{
    public sealed class SliderPointerForwarder : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action OnDown;
        public Action OnUp;
        public void OnPointerDown(PointerEventData eventData) => OnDown?.Invoke();
        public void OnPointerUp(PointerEventData eventData) => OnUp?.Invoke();
    }
}
