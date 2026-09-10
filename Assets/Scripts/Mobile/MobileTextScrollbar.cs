#nullable enable

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 文本框滚动条：窄竖条 + 可拖动滑块（桌面编辑器同款交互）。
    /// 指针按下 = 跳转到该位置；拖动滑块 = 连续改变值（0~1）。
    /// </summary>
    public sealed class MobileTextScrollbar : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        /// <summary>值变化回调（0~1，0=顶部）。</summary>
        public Action<float>? ValueChanged;

        private RectTransform _stripRect = null!;
        private RectTransform _thumb = null!;
        private float _value;

        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp01(value);
                UpdateThumb();
            }
        }

        public static MobileTextScrollbar Create(Transform parent, Font font)
        {
            var stripGo = new GameObject("TextScrollbar", typeof(RectTransform), typeof(Image), typeof(MobileTextScrollbar));
            var stripRect = (RectTransform)stripGo.transform;
            stripRect.SetParent(parent, false);
            stripRect.anchorMin = new Vector2(1f, 0f);
            stripRect.anchorMax = new Vector2(1f, 1f);
            stripRect.pivot = new Vector2(1f, 0.5f);
            stripRect.anchoredPosition = new Vector2(-2f, 0f);
            stripRect.sizeDelta = new Vector2(35f, -16f); // 加宽至原 14px 的 2.5 倍
            var stripImage = stripGo.GetComponent<Image>();
            stripImage.color = new Color(0.145f, 0.153f, 0.180f, 0.9f); // 视觉规范：轨道色

            var thumbGo = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
            var thumbRect = (RectTransform)thumbGo.transform;
            thumbRect.SetParent(stripRect, false);
            thumbRect.anchorMin = new Vector2(0f, 1f);
            thumbRect.anchorMax = new Vector2(1f, 1f);
            thumbRect.pivot = new Vector2(0.5f, 1f);
            thumbRect.anchoredPosition = Vector2.zero;
            thumbRect.sizeDelta = new Vector2(0f, 90f);
            var thumbImage = thumbGo.GetComponent<Image>();
            thumbImage.color = new Color(0.42f, 0.66f, 0.66f, 1f); // 低饱和青滑块

            var bar = stripGo.GetComponent<MobileTextScrollbar>();
            bar._stripRect = stripRect;
            bar._thumb = thumbRect;
            bar.UpdateThumb();
            return bar;
        }

        private void UpdateThumb()
        {
            if (_thumb == null) return;
            var track = Mathf.Max(1f, _stripRect.rect.height - _thumb.rect.height);
            _thumb.anchoredPosition = new Vector2(0f, -_value * track);
        }

        private void SetFromPointer(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_stripRect, eventData.position, eventData.pressEventCamera, out var local);
            var track = Mathf.Max(1f, _stripRect.rect.height - _thumb.rect.height);
            var topY = _stripRect.rect.height * 0.5f;
            Value = Mathf.Clamp01((topY - local.y - _thumb.rect.height * 0.5f) / track);
            ValueChanged?.Invoke(_value);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            SetFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            SetFromPointer(eventData);
        }
    }
}
