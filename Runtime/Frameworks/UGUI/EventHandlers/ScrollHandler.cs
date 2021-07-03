using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ReactUnity.UGUI.EventHandlers
{
    public class ScrollHandler : MonoBehaviour, IScrollHandler, IEventHandler
    {
        public event Action<BaseEventData> OnEvent = default;

        public void OnScroll(PointerEventData eventData)
        {
            OnEvent?.Invoke(eventData);
        }

        public void ClearListeners()
        {
            OnEvent = null;
        }
    }
}
