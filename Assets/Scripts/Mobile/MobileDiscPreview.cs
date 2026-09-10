#nullable enable

using MajSimai;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 传感区 slide 圆盘预览（移植自桌面编辑器 TouchSpacePanel）：
    /// 33 触区底图 + 当前播放到的传感区 slide 轨迹、节点、头部标记与星标位置。
    /// 几何与桌面版逐条一致（环向螺旋/曲线近似采样、弧长插值进度）。
    /// </summary>
    public sealed class MobileDiscPreview : MaskableGraphic
    {
        private SimaiChart _chart = SimaiChart.Empty;
        private double _time;
        private SimaiNote? _activeSlide;
        private double _activeProgress;
        private bool _slideActive;

        public void SetChart(SimaiChart chart)
        {
            _chart = chart;
            _activeSlide = null;
            _slideActive = false;
            _time = -1;
            SetVerticesDirty();
        }

        public void SetTime(double t)
        {
            if (Math.Abs(t - _time) < 0.0001) return;
            _time = t;
            var found = FindActiveSlide(t, out var note, out var progress);
            _activeSlide = note;
            _activeProgress = progress;
            if (found != _slideActive || found)
            {
                _slideActive = found;
                SetVerticesDirty();
            }
        }

        private bool FindActiveSlide(double t, out SimaiNote? note, out double progress)
        {
            note = null;
            progress = 0;
            var tps = _chart.NoteTimings;
            foreach (var tp in tps)
            {
                var notes = tp.Notes;
                for (var i = 0; i < notes.Length; i++)
                {
                    var n = notes[i];
                    if (!n.IsSensorSlide || n.Type != SimaiNoteType.Slide) continue;
                    var start = n.SlideStartTime;
                    var dur = Math.Max(n.SlideTime, 0.001);
                    if (t >= start && t <= start + dur)
                    {
                        note = n;
                        progress = (t - start) / dur;
                        return true;
                    }
                }
            }
            return false;
        }

        private const int CircleSegs = 32;
        private const int RingSegs = 56;

        private static readonly Color32 ColBase = new(0x33, 0x33, 0x33, 0xC0);
        private static readonly Color32 ColRing = new(0x88, 0x88, 0x88, 0xFF);
        private static readonly Color32 ColA = new(0x4F, 0xC3, 0xF7, 0xFF);
        private static readonly Color32 ColB = new(0x02, 0x77, 0xBD, 0xFF);
        private static readonly Color32 ColD = new(0xFF, 0xB3, 0x00, 0xFF);
        private static readonly Color32 ColE = new(0x66, 0xBB, 0x6A, 0xFF);
        private static readonly Color32 ColC = new(0xF0, 0xF0, 0xF0, 0xFF);
        private static readonly Color32 ColBtn = new(0xF0, 0xD0, 0x90, 0xFF);
        private static readonly Color32 ColTrail = new(0xE0, 0xE0, 0xE0, 0xFF);
        private static readonly Color32 ColNode = new(0xFF, 0xFF, 0xFF, 0xCC);
        private static readonly Color32 ColHead = new(0xFF, 0xE0, 0x82, 0xFF);
        private static readonly Color32 ColStar = new(0x8F, 0xF5, 0xFF, 0xFF);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f / 1.12f;
            var center = new Vector2(rect.center.x, rect.center.y);

            // 半透明底板
            AddCircle(vh, center, radius * 1.12f, ColBase);
            // 环线
            AddRing(vh, center, radius, 1.5f, ColRing);
            AddRing(vh, center, radius * 0.45f, 1.5f, ColRing);
            // 33 触区点
            for (var n = 1; n <= 8; n++)
            {
                AddDot(vh, center, radius, SensorPos('A', n), 2.6f, ColA);
                AddDot(vh, center, radius, SensorPos('B', n), 2.6f, ColB);
                AddDot(vh, center, radius, SensorPos('D', n), 2.6f, ColD);
                AddDot(vh, center, radius, SensorPos('E', n), 2.6f, ColE);
            }
            AddDot(vh, center, radius, SensorPos('C', 1), 3.2f, ColC);
            // 8 个按钮圆环
            for (var n = 1; n <= 8; n++)
            {
                var (x, y) = SensorPos('A', n);
                AddRing(vh, ToCanvas(center, radius, x, y), 6.5f, 2f, ColBtn);
            }

            var slide = _activeSlide;
            if (slide is null || !TryParseSensorSlidePath(slide.RawContent, out var nodes, out var shapes))
                return;

            // 轨迹折线
            var poly = new List<Vector2> { ToCanvas(center, radius, nodes[0].x, nodes[0].y) };
            for (var i = 1; i < nodes.Count; i++)
            {
                var prev = nodes[i - 1];
                var cur = nodes[i];
                var shape = i - 1 < shapes.Count ? shapes[i - 1] : '-';
                if (shape is '^' or '>' or '<' && prev.r > 0.01 && cur.r > 0.01)
                    AddArcApprox(poly, center, radius, prev, cur, shape);
                else if (IsCurveShape(shape))
                    AddCurveApprox(poly, center, radius, prev, cur, shape);
                else
                    poly.Add(ToCanvas(center, radius, cur.x, cur.y));
            }
            AddPolyline(vh, poly, 2.2f, ColTrail);

            // 节点点
            foreach (var nd in nodes)
                AddDot(vh, ToCanvas(center, radius, nd.x, nd.y), 2.2f, ColNode);

            // 头部标记
            var head = ToCanvas(center, radius, nodes[0].x, nodes[0].y);
            if (slide.TouchArea != ' ')
            {
                AddRect(vh, head, new Vector2(7f, 7f), ColHead);
                AddStar(vh, head, 2.4f, Color.white);
            }
            else
            {
                AddStar(vh, head, 6f, ColHead);
            }

            // 星标当前位置
            var starPos = PointAtProgress(nodes, shapes, Math.Clamp(_activeProgress, 0, 1));
            AddDot(vh, ToCanvas(center, radius, starPos.x, starPos.y), 3.8f, ColStar);
        }

        // ---- 网格图元 ----

        private static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 color)
        {
            var i = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x, a.y), color, Vector2.zero);
            vh.AddVert(new Vector3(b.x, b.y), color, Vector2.zero);
            vh.AddVert(new Vector3(c.x, c.y), color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        private static void AddCircle(VertexHelper vh, Vector2 center, float radius, Color32 color)
        {
            for (var k = 0; k < CircleSegs; k++)
            {
                var a0 = Mathf.PI * 2f * k / CircleSegs;
                var a1 = Mathf.PI * 2f * (k + 1) / CircleSegs;
                AddTriangle(vh, center,
                    center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius,
                    center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, color);
            }
        }

        private static void AddRing(VertexHelper vh, Vector2 center, float radius, float thickness, Color32 color)
        {
            var half = thickness * 0.5f;
            var r0 = radius - half;
            var r1 = radius + half;
            for (var k = 0; k < RingSegs; k++)
            {
                var a0 = Mathf.PI * 2f * k / RingSegs;
                var a1 = Mathf.PI * 2f * (k + 1) / RingSegs;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                AddTriangle(vh, center + d0 * r0, center + d0 * r1, center + d1 * r0, color);
                AddTriangle(vh, center + d1 * r0, center + d0 * r1, center + d1 * r1, color);
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
        {
            AddTriangle(vh, a, b, c, color);
            AddTriangle(vh, c, b, d, color);
        }

        private static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float thickness, Color32 color)
        {
            var dir = (to - from).normalized;
            var n = new Vector2(-dir.y, dir.x) * thickness * 0.5f;
            AddQuad(vh, from + n, from - n, to + n, to - n, color);
        }

        private static void AddPolyline(VertexHelper vh, List<Vector2> pts, float thickness, Color32 color)
        {
            for (var k = 1; k < pts.Count; k++)
                AddLine(vh, pts[k - 1], pts[k], thickness, color);
        }

        private static void AddDot(VertexHelper vh, Vector2 center, float radius, (double x, double y) pos, float dotRadius, Color32 color)
        {
            AddCircle(vh, ToCanvas(center, radius, pos.x, pos.y), dotRadius, color);
        }

        private static void AddDot(VertexHelper vh, Vector2 center, float radius, Color32 color)
        {
            AddCircle(vh, center, radius, color);
        }

        private static void AddRect(VertexHelper vh, Vector2 center, Vector2 size, Color32 color)
        {
            var hx = size.x * 0.5f;
            var hy = size.y * 0.5f;
            AddQuad(vh, center + new Vector2(-hx, -hy), center + new Vector2(-hx, hy),
                center + new Vector2(hx, hy), center + new Vector2(hx, -hy), color);
        }

        private static void AddStar(VertexHelper vh, Vector2 at, float r, Color32 color)
        {
            // 与桌面一致：四组交叉线构成的星形
            var r2 = r * 1.414f / 2f;
            AddLine(vh, at + new Vector2(-r2, -r2), at + new Vector2(r2, r2), 1.6f, color);
            AddLine(vh, at + new Vector2(r2, -r2), at + new Vector2(-r2, r2), 1.6f, color);
            AddLine(vh, at + new Vector2(0, -r), at + new Vector2(0, r), 1.6f, color);
            AddLine(vh, at + new Vector2(-r, 0), at + new Vector2(r, 0), 1.6f, color);
        }

        private static Vector2 ToCanvas(Vector2 center, float radius, double x, double y) =>
            center + new Vector2((float)(x * radius), (float)(y * radius));

        // ---- 几何（与 TouchSpacePanel 一致）----

        private static double BtnAngle(int button) => Math.PI * (5.0 / 8.0 - button / 4.0);

        private static (double x, double y) Polar(double r, double angle) =>
            (r * Math.Cos(angle), r * Math.Sin(angle));

        private static (double x, double y) SensorPos(char area, int idx)
        {
            switch (area)
            {
                case 'A': return Polar(1.0, BtnAngle(idx));
                case 'B': return Polar(0.45, BtnAngle(idx));
                case 'C': return (0, 0);
                case 'D': return Polar(1.0, BtnAngle(idx) + Math.PI / 8);
                case 'E': return Polar(0.45, BtnAngle(idx) + Math.PI / 8);
                default: return (0, 0);
            }
        }

        private readonly struct PanelNode
        {
            public readonly double x, y, r, a;
            public readonly int button0;
            public PanelNode(double x, double y, double r, double a, int button0)
            {
                this.x = x; this.y = y; this.r = r; this.a = a; this.button0 = button0;
            }
        }

        private static bool TryParseSensorSlidePath(string raw, out List<PanelNode> nodes, out List<char> shapes)
        {
            nodes = new List<PanelNode>();
            shapes = new List<char>();
            if (string.IsNullOrEmpty(raw)) return false;

            var i = 0;
            char area;
            int idx;
            if (raw[0] is >= 'A' and <= 'E')
            {
                area = raw[0];
                i = 1;
                if (area != 'C')
                {
                    if (i >= raw.Length || raw[i] is < '1' or > '8') return false;
                    idx = raw[i] - '0';
                    i++;
                }
                else
                {
                    idx = 1;
                    if (i < raw.Length && char.IsDigit(raw[i])) i++;
                }
            }
            else if (raw[0] is >= '1' and <= '8')
            {
                area = 'A';
                idx = raw[0] - '0';
                i = 1;
            }
            else return false;
            nodes.Add(MakeNode(area, idx));

            while (i < raw.Length)
            {
                var c = raw[i];
                if (c == '[')
                {
                    var close = raw.IndexOf(']', i);
                    if (close < 0) break;
                    i = close + 1;
                    continue;
                }
                if (c is 'f' or 'h') { i++; continue; }
                if (c is '-' or '^' or '>' or '<' or 'v' or 'V' or 'p' or 'q' or 's' or 'z' or 'w')
                {
                    var shape = c;
                    var isDouble = false;
                    i++;
                    if (shape is 'p' or 'q' && i < raw.Length && raw[i] == shape)
                    {
                        isDouble = true;
                        i++;
                    }
                    if (shape == 'V')
                    {
                        if (!ReadLocation(raw, ref i, out var flexArea, out var flexIdx)) return false;
                        nodes.Add(MakeNode(flexArea, flexIdx));
                        shapes.Add('-');
                    }
                    if (!ReadLocation(raw, ref i, out var endArea, out var endIdx)) return false;
                    nodes.Add(MakeNode(endArea, endIdx));
                    var storedShape = shape == 'V' ? '-' :
                        shape is 'p' or 'q' && isDouble ? char.ToUpperInvariant(shape) : shape;
                    shapes.Add(storedShape);
                    continue;
                }
                i++;
            }
            return nodes.Count > 1;
        }

        private static bool ReadLocation(string raw, ref int i, out char area, out int idx)
        {
            area = '\0';
            idx = 0;
            if (i >= raw.Length) return false;
            if (raw[i] is >= '1' and <= '8')
            {
                area = 'A';
                idx = raw[i] - '0';
                i++;
                return true;
            }
            if (raw[i] is >= 'A' and <= 'E')
            {
                area = raw[i];
                i++;
                if (area == 'C')
                {
                    idx = 1;
                    if (i < raw.Length && char.IsDigit(raw[i])) i++;
                    return true;
                }
                if (i >= raw.Length || raw[i] is < '1' or > '8') return false;
                idx = raw[i] - '0';
                i++;
                return true;
            }
            return false;
        }

        private static PanelNode MakeNode(char area, int idx)
        {
            var (x, y) = SensorPos(area, idx);
            var r = area is 'A' or 'D' ? 1.0 : area is 'C' ? 0.0 : 0.45;
            var a = Math.Atan2(y, x);
            var btn = idx - 1;
            if (area == 'C') btn = 0;
            return new PanelNode(x, y, r, a, btn);
        }

        private static void AddArcApprox(List<Vector2> poly, Vector2 center, float radius,
            PanelNode from, PanelNode to, char shape)
        {
            var isCw = shape switch
            {
                '>' => !((from.button0 + 2) % 8 >= 4),
                '<' => (from.button0 + 2) % 8 >= 4,
                _ => ShortestIsCw(from.button0, to.button0),
            };
            var span = RingSpan(from, to, isCw);
            var steps = 14;
            for (var k = 1; k <= steps; k++)
            {
                var t = k / (double)steps;
                var r = from.r + (to.r - from.r) * t;
                var a = isCw ? from.a - span * t : from.a + span * t;
                var (x, y) = Polar(r, a);
                poly.Add(ToCanvas(center, radius, x, y));
            }
            poly.Add(ToCanvas(center, radius, to.x, to.y));
        }

        private static void AddCurveApprox(List<Vector2> poly, Vector2 center, float radius,
            PanelNode from, PanelNode to, char shape)
        {
            var g = CurveGeom(from, to, shape);
            poly.Add(ToCanvas(center, radius, g.sx, g.sy));
            poly.Add(ToCanvas(center, radius, g.txIn, g.tyIn));
            var steps = 14;
            for (var k = 1; k <= steps; k++)
            {
                var t = k / (double)steps;
                var a = g.isCw ? g.tanInAngle - g.span * t : g.tanInAngle + g.span * t;
                var x = g.ccx + g.curveR * Math.Cos(a);
                var y = g.ccy + g.curveR * Math.Sin(a);
                poly.Add(ToCanvas(center, radius, x, y));
            }
            poly.Add(ToCanvas(center, radius, g.ex, g.ey));
        }

        private static bool ShortestIsCw(int fromBtn0, int toBtn0)
        {
            var diff = toBtn0 - fromBtn0;
            var rotation = diff >= 0 ? (diff > 4 ? -1 : 1) : (diff < -4 ? 1 : -1);
            return rotation > 0;
        }

        private static bool IsCurveShape(char shape) => shape is 'p' or 'q' or 'P' or 'Q';
        private static bool IsEdgeCurve(char shape) => shape is 'P' or 'Q';
        private static bool CurveIsCw(char shape) => shape is 'q' or 'Q';

        private static double VertexAngleOf(PanelNode n) => n.r <= 0.001 ? BtnAngle(1) : n.a;

        private static (double sx, double sy, double txIn, double tyIn, double txOut, double tyOut,
            double ex, double ey, double curveR, double span, bool isCw, double tanInAngle, double ccx, double ccy)
            CurveGeom(PanelNode from, PanelNode to, char shape)
        {
            var isEdge = IsEdgeCurve(shape);
            var isCw = CurveIsCw(shape);
            const double ringR = 1.0;
            var curveR = isEdge ? Math.Cos(3 * Math.PI / 8) * 1.2 : Math.Cos(3 * Math.PI / 8);
            var startAngle = VertexAngleOf(from);
            var endAngle = VertexAngleOf(to);

            var (sx, sy) = Polar(ringR, startAngle);
            var (ex, ey) = Polar(ringR, endAngle);

            double ccx = 0, ccy = 0;
            if (isEdge)
            {
                var off = Math.PI / 2.0 - Math.PI / 8.0;
                var ca = isCw ? startAngle + off : startAngle - off;
                (ccx, ccy) = Polar(0.4662, ca);
            }

            var rsx = sx - ccx;
            var rsy = sy - ccy;
            var startMag = Math.Sqrt(rsx * rsx + rsy * rsy);
            var startDelta = Math.Acos(Math.Min(1.0, curveR / startMag));
            var tanIn = Math.Atan2(rsy, rsx) + (isCw ? -startDelta : startDelta);
            var txIn = ccx + curveR * Math.Cos(tanIn);
            var tyIn = ccy + curveR * Math.Sin(tanIn);

            var rex = ex - ccx;
            var rey = ey - ccy;
            var endMag = Math.Sqrt(rex * rex + rey * rey);
            var endDelta = Math.Acos(Math.Min(1.0, curveR / endMag));
            var tanOut = Math.Atan2(rey, rex) + (isCw ? endDelta : -endDelta);
            var txOut = ccx + curveR * Math.Cos(tanOut);
            var tyOut = ccy + curveR * Math.Sin(tanOut);

            var span = isCw ? tanIn - tanOut : tanOut - tanIn;
            span = Math.IEEERemainder(span, 2 * Math.PI);
            if (span < 0) span += 2 * Math.PI;
            var wrap = isEdge ? Math.PI / 4.0 : Math.PI / 16.0;
            if (span <= wrap) span += 2 * Math.PI;

            return (sx, sy, txIn, tyIn, txOut, tyOut, ex, ey, curveR, span, isCw, tanIn, ccx, ccy);
        }

        private static double RingSpan(PanelNode from, PanelNode to, bool isCw)
        {
            var span = from.a - to.a;
            if (isCw)
            {
                if (span <= 0) span += 2 * Math.PI;
            }
            else
            {
                if (span >= 0) span -= 2 * Math.PI;
                span = -span;
            }
            if (span <= Math.PI / 16.0) span += 2 * Math.PI;
            return span;
        }

        private static PanelNode PointAtProgress(List<PanelNode> nodes, List<char> shapes, double t)
        {
            if (nodes.Count <= 1) return nodes[0];
            var segLens = new double[nodes.Count - 1];
            var total = 0.0;
            for (var k = 0; k < segLens.Length; k++)
            {
                segLens[k] = SegmentLength(nodes[k], nodes[k + 1], k < shapes.Count ? shapes[k] : '-');
                total += segLens[k];
            }
            var target = total * t;
            var acc = 0.0;
            for (var k = 0; k < segLens.Length; k++)
            {
                if (target <= acc + segLens[k] || k == segLens.Length - 1)
                {
                    var lt = segLens[k] <= 0 ? 0 : (target - acc) / segLens[k];
                    return LerpNode(nodes[k], nodes[k + 1], k < shapes.Count ? shapes[k] : '-', lt);
                }
                acc += segLens[k];
            }
            return nodes[nodes.Count - 1];
        }

        private static double SegmentLength(PanelNode from, PanelNode to, char shape)
        {
            if (shape is '^' or '>' or '<' && from.r > 0.01 && to.r > 0.01)
            {
                var isCw = shape switch
                {
                    '>' => !((from.button0 + 2) % 8 >= 4),
                    '<' => (from.button0 + 2) % 8 >= 4,
                    _ => ShortestIsCw(from.button0, to.button0),
                };
                return RingSpan(from, to, isCw) * (from.r + to.r) / 2.0;
            }
            if (IsCurveShape(shape))
            {
                var g = CurveGeom(from, to, shape);
                var jump = Math.Sqrt((g.sx - from.x) * (g.sx - from.x) + (g.sy - from.y) * (g.sy - from.y));
                var startLen = Math.Sqrt((g.txIn - g.sx) * (g.txIn - g.sx) + (g.tyIn - g.sy) * (g.tyIn - g.sy));
                var endLen = Math.Sqrt((g.ex - g.txOut) * (g.ex - g.txOut) + (g.ey - g.tyOut) * (g.ey - g.tyOut));
                return jump + startLen + g.span * g.curveR + endLen;
            }
            return Math.Sqrt((to.x - from.x) * (to.x - from.x) + (to.y - from.y) * (to.y - from.y));
        }

        private static PanelNode LerpNode(PanelNode from, PanelNode to, char shape, double t)
        {
            t = Math.Clamp(t, 0, 1);
            if (shape is '^' or '>' or '<' && from.r > 0.01 && to.r > 0.01)
            {
                var isCw = shape switch
                {
                    '>' => !((from.button0 + 2) % 8 >= 4),
                    '<' => (from.button0 + 2) % 8 >= 4,
                    _ => ShortestIsCw(from.button0, to.button0),
                };
                var span = RingSpan(from, to, isCw);
                var r = from.r + (to.r - from.r) * t;
                var a = isCw ? from.a - span * t : from.a + span * t;
                var (x, y) = Polar(r, a);
                return new PanelNode(x, y, r, a, from.button0);
            }
            if (IsCurveShape(shape))
            {
                var g = CurveGeom(from, to, shape);
                var jump = Math.Sqrt((g.sx - from.x) * (g.sx - from.x) + (g.sy - from.y) * (g.sy - from.y));
                var startLen = Math.Sqrt((g.txIn - g.sx) * (g.txIn - g.sx) + (g.tyIn - g.sy) * (g.tyIn - g.sy));
                var curveLen = g.span * g.curveR;
                var endLen = Math.Sqrt((g.ex - g.txOut) * (g.ex - g.txOut) + (g.ey - g.tyOut) * (g.ey - g.tyOut));
                var total = jump + startLen + curveLen + endLen;
                var d = total * t;
                double x, y;
                if (d < jump)
                {
                    var lt = jump <= 0 ? 0 : d / jump;
                    x = from.x + (g.sx - from.x) * lt;
                    y = from.y + (g.sy - from.y) * lt;
                }
                else if (d < jump + startLen)
                {
                    var lt = startLen <= 0 ? 0 : (d - jump) / startLen;
                    x = g.sx + (g.txIn - g.sx) * lt;
                    y = g.sy + (g.tyIn - g.sy) * lt;
                }
                else if (d < jump + startLen + curveLen)
                {
                    var lt = curveLen <= 0 ? 0 : (d - jump - startLen) / curveLen;
                    var a = g.isCw ? g.tanInAngle - g.span * lt : g.tanInAngle + g.span * lt;
                    x = g.ccx + g.curveR * Math.Cos(a);
                    y = g.ccy + g.curveR * Math.Sin(a);
                }
                else
                {
                    var lt = endLen <= 0 ? 0 : (d - jump - startLen - curveLen) / endLen;
                    x = g.txOut + (g.ex - g.txOut) * lt;
                    y = g.tyOut + (g.ey - g.tyOut) * lt;
                }
                var r = Math.Sqrt(x * x + y * y);
                var ang = Math.Atan2(y, x);
                return new PanelNode(x, y, r, ang, from.button0);
            }
            return new PanelNode(
                from.x + (to.x - from.x) * t,
                from.y + (to.y - from.y) * t,
                from.r + (to.r - from.r) * t,
                from.a + (to.a - from.a) * t,
                from.button0);
        }
    }
}
