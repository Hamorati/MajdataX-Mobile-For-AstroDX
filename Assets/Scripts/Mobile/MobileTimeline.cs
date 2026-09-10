#nullable enable

using MajSimai;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 编辑器顶部时间轴（重设计版，对齐桌面 SimaiVisualizer 行为）：
    /// 三档缩放：
    ///  - 整曲总览（默认）：整条曲子平铺在音符区，红色光标从左向右扫动（内容静态，光标移动）；
    ///  - 8 秒 / 4 秒窗口：内容从右向左流过固定指针（指针与渲染器判定中心水平对齐，x=540）。
    /// 播放时间平滑跟随（time += 0.2*(target-time)，桌面同款）；拖动 = 相对拖动定位
    /// （按下即停止播放；按住期间每 0.1 秒确认；松手最终确认）。
    /// </summary>
    public sealed class MobileTimeline : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>定位回调（谱面时间秒；拖动期间每 0.1 秒触发一次）。</summary>
        public event Action<double>? Seeked;

        /// <summary>手指按下时间轴（用于立即停止播放等）。</summary>
        public event Action? DragStarted;

        public enum ZoomMode
        {
            WholeTrack = 0, // 整曲总览
            Window8s = 1,   // 8 秒窗口
            Window4s = 2,   // 4 秒窗口
            WindowCustom = 3 // 自定义窗口（秒数可调，0.25~8s，持久化）
        }

        private const double NoteLeft = 4d;      // 音符区左缘（面板内）
        private const double DiscZone = 178d;    // 右侧圆盘区宽度

        private RectTransform _rect = null!;
        private RectTransform _caretRect = null!;
        private Text _label = null!;
        private MobileNoteLayer _noteLayer = null!;
        private MobileDiscPreview _disc = null!;

        private double _duration = 60d;
        private double _displayTime;   // 平滑跟随的显示时间
        private double _targetTime;    // 目标时间（播放 = NoteTime；拖动 = 瞬时）
        private float _customWindowSec = 8f; // 自定义窗口秒数（0.25~8）
        private bool _dragging;
        private float _dragAccum;
        private double _lastSeeked = double.NaN;
        private Vector2 _lastDragPos;
        private ZoomMode _zoom = ZoomMode.WholeTrack;

        public double Duration => _duration;
        public double DisplayTime => _displayTime;
        public double TargetTime => _targetTime;
        public ZoomMode Zoom => _zoom;

        public static MobileTimeline Create(Transform parent, Font font)
        {
            var go = new GameObject("Timeline", typeof(RectTransform), typeof(Image), typeof(MobileTimeline));
            var tl = go.GetComponent<MobileTimeline>();
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -100f);
            rect.sizeDelta = new Vector2(-20f, 200f);
            go.GetComponent<Image>().color = UiSkin.PanelBg;
            tl._rect = rect;

            // 音符层（波形/小节线/音符；窗口坐标）
            var noteGo = new GameObject("NoteLayer", typeof(RectTransform));
            var noteRect = (RectTransform)noteGo.transform;
            noteRect.SetParent(rect, false);
            noteRect.anchorMin = Vector2.zero;
            noteRect.anchorMax = Vector2.one;
            noteRect.offsetMin = new Vector2(4f, 4f);
            noteRect.offsetMax = new Vector2(-178f, -4f);
            tl._noteLayer = noteGo.AddComponent<MobileNoteLayer>();
            tl._noteLayer.raycastTarget = false;

            // 红色光标/指针（整曲档随播放扫动；窗口档固定在面板中心 = 渲染器判定中心）
            var caretGo = new GameObject("Caret", typeof(RectTransform), typeof(Image));
            tl._caretRect = (RectTransform)caretGo.transform;
            tl._caretRect.SetParent(rect, false);
            tl._caretRect.anchorMin = new Vector2(0.5f, 0f);
            tl._caretRect.anchorMax = new Vector2(0.5f, 1f);
            tl._caretRect.pivot = new Vector2(0.5f, 0.5f);
            tl._caretRect.sizeDelta = new Vector2(3f, 0f);
            var caretImage = caretGo.GetComponent<Image>();
            caretImage.color = new Color(200f / 255f, 0f, 0f, 200f / 255f);
            caretImage.raycastTarget = false;

            // 传感区 slide 圆盘（时间轴右侧，桌面布局）
            var discGo = new GameObject("DiscPreview", typeof(RectTransform));
            var discRect = (RectTransform)discGo.transform;
            discRect.SetParent(rect, false);
            discRect.anchorMin = new Vector2(1f, 0.5f);
            discRect.anchorMax = new Vector2(1f, 0.5f);
            discRect.pivot = new Vector2(0.5f, 0.5f);
            discRect.anchoredPosition = new Vector2(-85f, 0f);
            discRect.sizeDelta = new Vector2(158f, 158f);
            tl._disc = discGo.AddComponent<MobileDiscPreview>();
            tl._disc.raycastTarget = false;

            // 时间标签（圆盘左侧）
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = new Vector2(1f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(1f, 0.5f);
            labelRect.anchoredPosition = new Vector2(-180f, 10f);
            labelRect.sizeDelta = new Vector2(200f, 36f);
            var text = labelGo.GetComponent<Text>();
            text.font = font;
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleRight;
            text.color = UiSkin.TextPrimary;
            text.raycastTarget = false;
            text.text = "0:00 / 0:00";
            tl._label = text;

            return tl;
        }

        /// <summary>音符区宽度（面板宽 − 左缘 − 圆盘区）。</summary>
        private double NoteAreaWidth => Math.Max(1d, _rect.rect.width - NoteLeft - DiscZone);

        /// <summary>当前模式下的可视时间跨度（整曲 = 曲目时长；窗口 = 8s/4s/自定义）。</summary>
        private double VisibleSpan =>
            _zoom == ZoomMode.WholeTrack ? Math.Max(1d, _duration) :
            _zoom == ZoomMode.Window8s ? 8d :
            _zoom == ZoomMode.Window4s ? 4d :
            Math.Max(0.25d, _customWindowSec);

        /// <summary>指针（x=540）在音符区中的横向比例。整曲档光标按时间比例扫动，不使用本值。</summary>
        private double PointerFrac => (_rect.rect.width * 0.5 - NoteLeft) / NoteAreaWidth;

        /// <summary>时间 t → 音符区局部 x（整曲档按曲长比例；窗口档按窗口映射）。</summary>
        private double XOf(double t)
        {
            if (_zoom == ZoomMode.WholeTrack)
                return t / Math.Max(1d, _duration) * NoteAreaWidth;
            var ws = _displayTime - VisibleSpan * PointerFrac;
            return (t - ws) / VisibleSpan * NoteAreaWidth;
        }

        public void SetDuration(double duration)
        {
            _duration = Math.Max(1d, duration);
            _displayTime = 0d;
            _targetTime = 0d;
            Relayout();
        }

        public void SetZoom(ZoomMode mode)
        {
            if (_zoom == mode) return;
            _zoom = mode;
            Relayout();
        }

        /// <summary>设置自定义窗口秒数（0.25~8；当前为自定义档时立即重布局）。</summary>
        public void SetCustomWindowSec(float sec)
        {
            _customWindowSec = Mathf.Clamp(sec, 0.25f, 8f);
            if (_zoom == ZoomMode.WindowCustom)
                Relayout();
        }

        /// <summary>装载谱面（小节边界 + 音符 + 圆盘）。</summary>
        public void SetChart(SimaiChart chart)
        {
            _disc.SetChart(chart);
            var bars = new List<double>();
            var bpms = new List<double>();
            foreach (var tp in chart.CommaTimings)
            {
                bars.Add(tp.Timing);
                bpms.Add(tp.Bpm);
            }
            _noteLayer.SetChart(chart, bars.ToArray(), bpms.ToArray());
            Relayout();
        }

        /// <summary>装载音频波形（有符号原始采样折线 −1~1；trackDuration = 曲目时长，chartOffset = first 偏移）。</summary>
        public void SetWaveform(float[] buckets, double trackDuration, double chartOffset)
        {
            _noteLayer.SetWaveform(buckets, trackDuration, chartOffset);
        }

        /// <summary>缩放切换/装载后重布局（整曲档静态布局；窗口档按当前窗口）。</summary>
        private void Relayout()
        {
            if (_zoom == ZoomMode.WholeTrack)
                _noteLayer.SetWholeTrack(Math.Max(1d, _duration));
            else
                _noteLayer.SetWindow(_displayTime - VisibleSpan * PointerFrac, VisibleSpan);
            UpdateCaret();
            UpdateLabel();
        }

        /// <summary>更新播放目标时间（平滑跟随由 Update 完成；拖动期间忽略）。</summary>
        public void SetPlayhead(double time)
        {
            if (_dragging) return;
            _targetTime = time;
        }

        private void Update()
        {
            // 平滑跟随（桌面同款）；拖动时瞬时
            if (!_dragging && Math.Abs(_targetTime - _displayTime) > 1e-6)
            {
                _displayTime += 0.2 * (_targetTime - _displayTime);
                if (Math.Abs(_targetTime - _displayTime) < 0.001) _displayTime = _targetTime;
                if (_zoom != ZoomMode.WholeTrack)
                    _noteLayer.SetWindow(_displayTime - VisibleSpan * PointerFrac, VisibleSpan);
            }

            UpdateCaret();
            UpdateLabel();
            _disc.SetTime(_displayTime);

            if (!_dragging) return;
            _dragAccum += Time.unscaledDeltaTime;
            if (_dragAccum >= 0.1f)
            {
                _dragAccum = 0f;
                if (double.IsNaN(_lastSeeked) || Math.Abs(_displayTime - _lastSeeked) > 1e-6)
                {
                    _lastSeeked = _displayTime;
                    Seeked?.Invoke(_displayTime);
                }
            }
        }

        private void UpdateCaret()
        {
            if (_caretRect is null) return;
            // 面板局部 x：整曲档按时间比例扫动；窗口档固定在面板中心（= 渲染器判定中心）
            var caretX = _zoom == ZoomMode.WholeTrack ? XOf(_displayTime) + NoteLeft : _rect.rect.width * 0.5;
            _caretRect.anchoredPosition = new Vector2((float)(caretX - _rect.rect.width * 0.5), 0f);
        }

        private void UpdateLabel()
        {
            _label.text = $"{Fmt(_displayTime)} / {Fmt(_duration)}";
        }

        private static string Fmt(double t)
        {
            var total = Math.Max(0, (int)Math.Round(t));
            return $"{total / 60}:{total % 60:00}";
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _dragging = true;
            _dragAccum = 0f;
            _lastSeeked = _displayTime; // 静止按住不触发确认；时间变化才实时定位
            _lastDragPos = eventData.position;
            DragStarted?.Invoke(); // 按下即停止播放（由外部处理）
        }

        public void OnDrag(PointerEventData eventData)
        {
            // 相对拖动：拖动距离 ↔ 时间变化量
            var w = (float)NoteAreaWidth;
            var dx = eventData.position.x - _lastDragPos.x;
            _lastDragPos = eventData.position;
            var span = VisibleSpan;
            var dt = dx * (span / Math.Max(1f, w));
            _targetTime -= dt;
            _displayTime = _targetTime; // 拖动瞬时
            if (_zoom != ZoomMode.WholeTrack)
                _noteLayer.SetWindow(_displayTime - VisibleSpan * PointerFrac, VisibleSpan);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _dragging = false;
            _lastSeeked = double.NaN;
            Seeked?.Invoke(_displayTime); // 松手最终确认
        }
    }
}
