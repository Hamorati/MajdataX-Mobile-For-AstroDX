#nullable enable

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 简易滚动区（替代 ScrollRect，避免其与内容内按钮/滑条抢占事件）：
    /// 视口滚轮滚动 + 右缘滚动条拖动。内容锚定视口顶部，anchoredPosition.y = 滚动偏移。
    /// </summary>
    public sealed class MobileScrollArea : MonoBehaviour, IScrollHandler
    {
        private RectTransform _content = null!;
        private RectTransform _thumb = null!;
        private float _range;
        private float _viewH;

        public static MobileScrollArea Create(Transform viewport, RectTransform content, float contentHeight, Transform stripParent)
        {
            var area = viewport.gameObject.AddComponent<MobileScrollArea>();
            area._content = content;
            area._viewH = ((RectTransform)viewport).rect.height;
            area._range = Mathf.Max(0f, contentHeight - area._viewH);

            var strip = new GameObject("ScrollStrip", typeof(RectTransform), typeof(Image), typeof(MobileScrollStrip));
            var stripRect = (RectTransform)strip.transform;
            stripRect.SetParent(stripParent, false);
            stripRect.anchorMin = new Vector2(1f, 0.5f);
            stripRect.anchorMax = new Vector2(1f, 0.5f);
            stripRect.pivot = new Vector2(1f, 0.5f);
            stripRect.anchoredPosition = new Vector2(24f, 0f);
            stripRect.sizeDelta = new Vector2(18f, area._viewH);
            strip.GetComponent<Image>().color = new Color(0.145f, 0.153f, 0.180f, 0.9f); // 视觉规范：轨道色

            var thumb = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
            var thumbRect = (RectTransform)thumb.transform;
            thumbRect.SetParent(stripRect, false);
            thumbRect.anchorMin = new Vector2(0f, 1f);
            thumbRect.anchorMax = new Vector2(1f, 1f);
            thumbRect.pivot = new Vector2(0.5f, 1f);
            thumbRect.anchoredPosition = Vector2.zero;
            thumbRect.sizeDelta = new Vector2(0f, area.ThumbSize());
            thumb.GetComponent<Image>().color = new Color(0.42f, 0.66f, 0.66f, 1f); // 低饱和青滑块
            area._thumb = thumbRect;

            strip.GetComponent<MobileScrollStrip>().Bind(area);
            area.Apply(0f);
            return area;
        }

        private float ThumbSize()
        {
            if (_range <= 0f) return _viewH;
            var size = _viewH * (_viewH / (_viewH + _range));
            return Mathf.Max(36f, size);
        }

        public void Apply(float offset)
        {
            var clamped = Mathf.Clamp(offset, 0f, _range);
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, clamped);
            if (_range > 0f)
            {
                var t = clamped / _range;
                var track = _viewH - ThumbSize();
                _thumb.anchoredPosition = new Vector2(0f, -t * track);
            }
        }

        public void ScrollBy(float dy)
        {
            if (_range <= 0f) return;
            Apply(_content.anchoredPosition.y + dy);
        }

        /// <summary>滚动条拖动距离（屏幕像素）→ 内容偏移。</summary>
        public void ScrollByThumbDelta(float dy)
        {
            if (_range <= 0f) return;
            var track = _viewH - ThumbSize();
            ScrollBy(dy * _range / Mathf.Max(1f, track));
        }

        public void OnScroll(PointerEventData eventData)
        {
            ScrollBy(-eventData.scrollDelta.y * 60f);
        }
    }

    /// <summary>滚动条拖动（仅条本身上响应拖动，不干扰内容内按钮/滑条）。</summary>
    public sealed class MobileScrollStrip : MonoBehaviour, IDragHandler
    {
        private MobileScrollArea _area = null!;

        public void Bind(MobileScrollArea area)
        {
            _area = area;
        }

        public void OnDrag(PointerEventData eventData)
        {
            _area.ScrollByThumbDelta(eventData.delta.y);
        }
    }
}
