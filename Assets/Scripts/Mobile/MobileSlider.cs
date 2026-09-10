#nullable enable

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 修复版滑条：原生 Slider 的按下跳转依赖 pointerDrag（按下时尚未赋值，恒为空），
    /// 导致"点击任意位置定位数值"不生效；此子类在按下时手动按点击位置设置数值。
    /// </summary>
    public sealed class MobileSlider : Slider
    {
        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (!IsActive() || !IsInteractable() || eventData.button != PointerEventData.InputButton.Left)
                return;
            var clickRect = (RectTransform)transform; // 用滑条自身矩形（fillRect 宽度随数值变化，不可作基准）
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    clickRect, eventData.position, eventData.pressEventCamera, out var local))
            {
                var rect = clickRect.rect;
                if (rect.width > 0.0001f)
                {
                    var f = Mathf.Clamp01((local.x - rect.xMin) / rect.width);
                    value = Mathf.Lerp(minValue, maxValue, f);
                }
            }
        }
    }
}
