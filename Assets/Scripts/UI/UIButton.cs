using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Strategy
{
    /// <summary>Minimal clickable area. Visual states are drawn by the HUD on each refresh.</summary>
    public class UIButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action OnClick;
        public Action<PointerEventData> OnClickAt;
        public Action<bool> OnHover;
        public bool Interactable = true;
        public bool Hovered { get; private set; }

        public void OnPointerEnter(PointerEventData e)
        {
            Hovered = true;
            OnHover?.Invoke(true);
        }

        public void OnPointerExit(PointerEventData e)
        {
            Hovered = false;
            OnHover?.Invoke(false);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !Interactable) return;
            OnClick?.Invoke();
            OnClickAt?.Invoke(e);
        }

        void OnDisable() => Hovered = false;
    }
}
