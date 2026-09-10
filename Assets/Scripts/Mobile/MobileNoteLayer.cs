#nullable enable

using MajSimai;
using UnityEngine;
using UnityEngine.UI;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 时间轴音符层（重设计版，对齐桌面 SimaiVisualizer 样式）：
    ///  - 波形：全亮绿 (0,100,0) 原始采样折线，约 1 点/0.5px，纵贯全高、垂直居中；
    ///  - 节拍线：每拍白线（窗口档），小节线更亮，BPM 变化处黄线（原生同款）；
    ///  - 音符：Tap=圆点/星、Touch=方块、Hold=横线、TouchHold=四色分层线、
    ///    Slide=星标头+虚线身、Hanabi=半透明带；
    ///  - 整曲总览：内容静态平铺整曲；窗口档：内容随窗口滚动。
    /// 局部坐标：rect 中心为原点（x ∈ ±w/2，y ∈ ±h/2，y 向上）。
    /// </summary>
    public sealed class MobileNoteLayer : MaskableGraphic
    {
        private SimaiChart _chart = SimaiChart.Empty;
        private double[] _barTimes = System.Array.Empty<double>();
        private double[] _barBpms = System.Array.Empty<double>();
        private float[] _waveform = System.Array.Empty<float>();
        private double _windowStart;
        private double _windowDur = 8d;
        private double _waveDuration = 1d;
        private double _waveOffset;
        private bool _wholeTrack;          // 整曲总览模式（内容静态，光标扫动）
        private double _wholeDuration = 60d;

        // 桌面 SimaiVisualizer 配色
        private static readonly Color32 TapColor = new(0xFF, 0xB6, 0xC1, 0xFF);
        private static readonly Color32 TouchColor = new(0x00, 0xBF, 0xFF, 0xFF);
        private static readonly Color32 SlideHeadColor = new(0x00, 0xBF, 0xFF, 0xFF);
        private static readonly Color32 SlideBodyColor = new(0x87, 0xCE, 0xEB, 0xFF);
        private static readonly Color32 BreakColor = new(0xFF, 0x45, 0x00, 0xFF);
        private static readonly Color32 EachColor = new(0xFF, 0xD7, 0x00, 0xFF);
        private static readonly Color32 MineColor = new(0x4F, 0x4F, 0x4F, 0xFF);
        private static readonly Color32 MineBreakColor = new(0x83, 0x83, 0x83, 0xFF);
        private static readonly Color32 MineSlideColor = new(0x4F, 0x4F, 0x4F, 0xFF);
        private static readonly Color32 HanabiColor = new(0xE0, 0xE0, 0xE0, 0x30);
        // 原生 SimaiVisualizer：白色拍线（每拍）+ 黄色 BPM 变化线
        private static readonly Color32 BeatLineColor = new(0xFF, 0xFF, 0xFF, 0x55);
        private static readonly Color32 StrongBeatColor = new(0xFF, 0xFF, 0xFF, 0x90);
        private static readonly Color32 BpmLineColor = new(0xFF, 0xFF, 0x00, 0xC0);
        // 原生波形：全亮绿色 (0,100,0) 原始采样折线（曝光不足的实拍下也可见）
        private static readonly Color32 WaveColor = new(0x00, 0x64, 0x00, 0xFF);
        private static readonly Color32[] TouchHoldColors =
        {
            new(0x00, 0xA5, 0xF7, 0xFF),
            new(0x16, 0xAC, 0x6E, 0xFF),
            new(0xF6, 0xEB, 0x00, 0xFF),
            new(0xF7, 0x46, 0x01, 0xFF),
        };

        public void SetChart(SimaiChart chart, double[] barTimes, double[] barBpms)
        {
            _chart = chart;
            _barTimes = barTimes;
            _barBpms = barBpms;
            // 谱面切换即清空旧波形（无音频谱面不显示上一首的残影；有音频时 SetWaveform 随后重建）
            _waveform = System.Array.Empty<float>();
            _waveDuration = 1d;
            SetVerticesDirty();
        }

        public void SetWindow(double windowStart, double windowDur)
        {
            _wholeTrack = false;
            _windowStart = windowStart;
            _windowDur = System.Math.Max(0.5, windowDur);
            SetVerticesDirty();
        }

        /// <summary>整曲总览模式：整条曲子平铺（内容静态，仅光标移动）。</summary>
        public void SetWholeTrack(double duration)
        {
            _wholeTrack = true;
            _wholeDuration = System.Math.Max(1d, duration);
            SetVerticesDirty();
        }

        /// <summary>装载音频波形（有符号原始采样折线 −1~1；chartOffset = first 偏移，用于与谱面时间域对齐）。</summary>
        public void SetWaveform(float[] buckets, double trackDuration, double chartOffset)
        {
            _waveform = buckets ?? System.Array.Empty<float>();
            _waveDuration = System.Math.Max(0.001, trackDuration);
            _waveOffset = chartOffset;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            var w = rect.width;
            var h = rect.height;
            if (w < 4f || h < 4f) return;

            var winStart = _wholeTrack ? 0d : _windowStart;
            var winEnd = _wholeTrack ? _wholeDuration : _windowStart + _windowDur;
            var span = winEnd - winStart;

            // 波形底纹：原生 SimaiVisualizer 同款——全亮绿色折线、原始采样锯齿、纵贯全高、垂直居中
            if (_waveform.Length > 1)
            {
                var per = _waveDuration / _waveform.Length;
                // 按像素密度抽稀（约 1 点/0.5px）
                var stride = System.Math.Max(1, (int)(span / System.Math.Max(1d, _waveDuration) * _waveform.Length / (w * 2.0)));
                var prevX = 0f;
                var prevY = 0f;
                var hasPrev = false;
                for (var b = 0; b < _waveform.Length; b += stride)
                {
                    var t = b * per - _waveOffset;
                    if (t < winStart - per || t > winEnd + per) continue;
                    var x = XOf(t, w, winStart, span);
                    var v = _waveform[b];
                    if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                    v = Mathf.Clamp(v, -1f, 1f);
                    var y = v * (h * 0.5f); // 原生 y=v/65535*h+h/2 在中心坐标系中等价于 v*h/2
                    if (hasPrev)
                    {
                        var gap = Mathf.Abs(x - prevX);
                        if (gap < w * 2f) // 相邻点连线（跨窗口缺口不连）
                            AddLine(vh, new Vector2(prevX, prevY), new Vector2(x, y), 1.2f, WaveColor);
                    }
                    prevX = x;
                    prevY = y;
                    hasPrev = true;
                }
            }

            // 节拍线：每拍白线（仅窗口档，整曲档太密）、小节线更亮、BPM 变化处黄线
            for (var i = 0; i < _barTimes.Length; i++)
            {
                var bt = _barTimes[i];
                var bpm = i < _barBpms.Length ? _barBpms[i] : 120d;
                var isBpm = i > 0 && i < _barBpms.Length && System.Math.Abs(_barBpms[i] - _barBpms[i - 1]) > 0.01;
                var beatLen = 60.0 / System.Math.Max(1.0, bpm);
                for (var k = 0; k < 4; k++)
                {
                    var t = bt + k * beatLen;
                    if (t < winStart || t > winEnd) continue;
                    var x = XOf(t, w, winStart, span);
                    if (k == 0)
                        AddLine(vh, new Vector2(x, -h * 0.5f), new Vector2(x, h * 0.5f),
                            isBpm ? 1.6f : 1.4f, isBpm ? BpmLineColor : StrongBeatColor);
                    else if (!_wholeTrack)
                        AddLine(vh, new Vector2(x, -h * 0.5f), new Vector2(x, h * 0.5f),
                            1.0f, BeatLineColor);
                }
            }

            // 音符（仅可视范围）
            var tps = _chart.NoteTimings;
            foreach (var tp in tps)
            {
                var t = tp.Timing;
                if (t < winStart - 2.0 || t > winEnd + 2.0) continue;
                var notes = tp.Notes;
                var nonSlideHeadCount = 0;
                var slideCount = 0;
                for (var i = 0; i < notes.Length; i++)
                {
                    if (!notes[i].IsSlideNoHead) nonSlideHeadCount++;
                    if (notes[i].Type == SimaiNoteType.Slide) slideCount++;
                }
                var isEach = nonSlideHeadCount > 1;
                var x = XOf(t, w, winStart, span);

                for (var i = 0; i < notes.Length; i++)
                {
                    var n = notes[i];
                    var y = YOf(n.StartPosition, h);
                    DrawNote(vh, n, x, y, w, isEach, slideCount, span, winStart);
                }
            }
        }

        /// <summary>时间 t → 局部 x（rect 中心为原点）。</summary>
        private float XOf(double t, float w, double winStart, double span) =>
            (float)((t - winStart) / span) * w - w * 0.5f;

        private float YOf(int startPos, float h) => -h * 0.5f + 8f + (startPos - 1) * (h - 16f) / 8f;

        private void DrawNote(VertexHelper vh, SimaiNote n, float x, float y, float w, bool isEach, int slideCount, double span, double winStart)
        {
            if (n.IsHanabi)
            {
                var xw = Mathf.Max(3f, (float)(1.0 / span) * w);
                if (n.Type == SimaiNoteType.TouchHold)
                    x += (float)(n.HoldTime / span) * w;
                AddRect(vh, new Vector2(x + xw * 0.5f, y), new Vector2(xw, 4f), HanabiColor);
                return;
            }

            switch (n.Type)
            {
                case SimaiNoteType.Tap:
                    var tapCol = n.IsMine ? (n.IsBreak ? MineBreakColor : MineColor) :
                                 n.IsBreak ? BreakColor :
                                 isEach ? EachColor : TapColor;
                    if (n.IsForceStar)
                        AddStar(vh, new Vector2(x, y), 4.6f, tapCol);
                    else
                        AddCircle(vh, new Vector2(x, y), 4.2f, tapCol);
                    break;

                case SimaiNoteType.Touch:
                    AddRect(vh, new Vector2(x, y), new Vector2(9f, 9f), n.IsMine ? MineColor : isEach ? EachColor : TouchColor);
                    break;

                case SimaiNoteType.Hold:
                    var holdCol = n.IsMine ? (n.IsBreak ? MineBreakColor : MineColor) :
                                  n.IsBreak ? BreakColor :
                                  isEach ? EachColor : TapColor;
                    var xr = x + (float)(n.HoldTime / span) * w;
                    if (xr - x < 1f) xr = x + 7f;
                    AddLine(vh, new Vector2(x, y), new Vector2(xr, y), 4f, holdCol);
                    break;

                case SimaiNoteType.TouchHold:
                    var delta = Mathf.Max(2f, (float)(n.HoldTime / span) * w / 4f);
                    var thColors = n.IsMine
                        ? new[] { MineBreakColor, MineColor, MineBreakColor, MineColor }
                        : TouchHoldColors;
                    for (var j = 0; j < 4; j++)
                        AddLine(vh, new Vector2(x, y), new Vector2(x + delta * (4 - j), y), 3.4f, thColors[j]);
                    break;

                case SimaiNoteType.Slide:
                    if (!n.IsSlideNoHead)
                    {
                        var headCol = n.IsMine ? (n.IsBreak ? MineBreakColor : MineColor) :
                                      n.IsBreak ? BreakColor :
                                      isEach ? EachColor : SlideHeadColor;
                        if (n.IsTapHeadSlide)
                            AddCircle(vh, new Vector2(x, y), 4.2f, headCol);
                        else
                            AddStar(vh, new Vector2(x, y), 5.4f, headCol);
                    }
                    else if (n.IsSensorSlide && n.TouchArea != ' ')
                    {
                        AddRect(vh, new Vector2(x, y), new Vector2(9f, 9f), n.IsMine ? MineColor : isEach ? EachColor : TouchColor);
                        AddStar(vh, new Vector2(x, y), 3.8f, TouchColor);
                    }
                    var bodyCol = n.IsMineSlide ? MineSlideColor :
                                  n.IsSlideBreak ? BreakColor :
                                  slideCount >= 2 ? EachColor : SlideBodyColor;
                    // 本体从启动拍（SlideStartTime = Timing + 等待拍）起画：
                    // 头星（Timing）与启动拍间隔一拍，二者独立绘制（与渲染器/桌面一致）
                    var xBody = XOf(n.SlideStartTime, w, winStart, span);
                    var xsR = xBody + (float)(n.SlideTime / span) * w;
                    if (xsR - xBody < 3f) xsR = xBody + 6f;
                    AddDashedLine(vh, new Vector2(xBody, y), new Vector2(xsR, y), 3.6f, 8f, bodyCol);
                    break;
            }
        }

        // ---- 网格图元（局部坐标，y 向上）----

        private static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 color)
        {
            var i = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x, a.y), color, Vector2.zero);
            vh.AddVert(new Vector3(b.x, b.y), color, Vector2.zero);
            vh.AddVert(new Vector3(c.x, c.y), color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
        {
            AddTriangle(vh, a, b, c, color);
            AddTriangle(vh, c, b, d, color);
        }

        private static void AddCircle(VertexHelper vh, Vector2 center, float r, Color32 color)
        {
            const int segs = 12;
            for (var k = 0; k < segs; k++)
            {
                var a0 = Mathf.PI * 2f * k / segs;
                var a1 = Mathf.PI * 2f * (k + 1) / segs;
                AddTriangle(vh, center,
                    center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r,
                    center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, color);
            }
        }

        private static void AddRect(VertexHelper vh, Vector2 center, Vector2 size, Color32 color)
        {
            var hx = size.x * 0.5f;
            var hy = size.y * 0.5f;
            AddQuad(vh, center + new Vector2(-hx, -hy), center + new Vector2(-hx, hy),
                center + new Vector2(hx, hy), center + new Vector2(hx, -hy), color);
        }

        private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color32 color)
        {
            var dir = (b - a).normalized;
            var n = new Vector2(-dir.y, dir.x) * thickness * 0.5f;
            AddQuad(vh, a + n, a - n, b + n, b - n, color);
        }

        private static void AddDashedLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness, float dash, Color32 color)
        {
            var len = Vector2.Distance(a, b);
            if (len < 0.5f) return;
            var dir = (b - a) / len;
            var pos = 0f;
            while (pos < len)
            {
                var end = Mathf.Min(pos + dash, len);
                AddLine(vh, a + dir * pos, a + dir * end, thickness, color);
                pos = end + dash;
            }
        }

        private static void AddStar(VertexHelper vh, Vector2 at, float r, Color32 color)
        {
            var r2 = r * 1.414f / 2f;
            AddLine(vh, at + new Vector2(-r2, -r2), at + new Vector2(r2, r2), 1.8f, color);
            AddLine(vh, at + new Vector2(r2, -r2), at + new Vector2(-r2, r2), 1.8f, color);
            AddLine(vh, at + new Vector2(0, -r), at + new Vector2(0, r), 1.8f, color);
            AddLine(vh, at + new Vector2(-r, 0), at + new Vector2(r, 0), 1.8f, color);
        }
    }
}
