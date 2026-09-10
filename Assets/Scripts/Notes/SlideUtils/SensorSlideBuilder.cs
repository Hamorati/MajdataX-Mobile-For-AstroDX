using MajdataViewX.Base;
using MajdataViewX.Types.Input;
using System;
using System.Collections.Generic;
using System.Numerics;
using Unity.Mathematics;

namespace MajdataViewX.Notes.SlideUtils
{
    /// <summary>
    /// 传感区 slide（AstroDX/SimaiSharp 语法）的解析与 metadata 构建。
    /// 路径经过各传感区 Touch 位置；判定区 = 沿路径的传感区序列（每个传感区一个判定段，
    /// 按压即可推进，与 AstroDX RegularSlideSegmentHandler 的交互模型一致）。
    /// 本文件不依赖 UnityEngine，可被独立测试工程直接链接验证。
    /// </summary>
    public static class SensorSlideBuilder
    {
        public readonly struct SensorSlideSeg
        {
            public readonly char Shape;          // '-' '^' '>' '<' 'v' 等
            public readonly bool IsDoubleChar;   // pp/qq（EdgeCurve）双字符形状
            public readonly Complex End;         // 终点坐标
            public readonly SensorType EndSensor; // 终点判定区（按钮→A 区；传感区→其 Touch 区）
            public readonly int EndBtn0;         // 终点按键 0-based（用于环向判定；C→0）

            public SensorSlideSeg(char shape, bool isDoubleChar, Complex end, SensorType endSensor, int endBtn0)
            {
                Shape = shape;
                IsDoubleChar = isDoubleChar;
                End = end;
                EndSensor = endSensor;
                EndBtn0 = endBtn0;
            }
        }

        /// <summary>传感区字母 + 1-8 索引 → SensorType（与 MajCtx.GetSensor 同语义，独立实现便于测试）。</summary>
        public static SensorType SensorOf(char area, int idx)
        {
            return area switch
            {
                'A' => (SensorType)(idx - 1),
                'B' => (SensorType)(idx + 7),
                'C' => SensorType.C,
                'D' => (SensorType)(idx + 16),
                'E' => (SensorType)(idx + 24),
                _ => SensorType.A1,
            };
        }

        /// <summary>
        /// 解析传感区 slide 的 RawContent（如 6-B6-B8-B2-2[8:1]、E3-E5-D7-D1>4b[4:11]、
        /// D2-C-D4^B8[4:5]、8x>E7-C[16:27]）。RawContent 中 b/m/x 等标志已被 MajSimai 移除，f/h 保留。
        /// </summary>
        public static bool TryParse(
            ReadOnlySpan<char> rawContent,
            out SensorType headSensor,
            out int startPos,
            out Complex startPoint,
            out List<SensorSlideSeg> segs,
            out int endPos)
        {
            headSensor = SensorType.Invalid;
            startPos = 0;
            startPoint = Complex.Zero;
            segs = new List<SensorSlideSeg>();
            endPos = 0;
            if (rawContent.Length == 0) return false;

            var i = 0;
            if (rawContent[0] is >= 'A' and <= 'E')
            {
                var headArea = rawContent[0];
                i = 1;
                if (headArea != 'C' && i < rawContent.Length && rawContent[i] is >= '1' and <= '8')
                {
                    var idx = rawContent[i] - '0';
                    startPos = idx;
                    i++;
                }
                else if (headArea == 'C')
                {
                    startPos = 8;
                    if (i < rawContent.Length && char.IsDigit(rawContent[i])) i++;
                }
                else
                {
                    return false;
                }
                headSensor = SensorOf(headArea, startPos);
                startPoint = ToComplex(MajPos.GetAreaPos(headSensor));
            }
            else if (rawContent[0] is >= '1' and <= '8')
            {
                startPos = rawContent[0] - '0';
                headSensor = (SensorType)(startPos - 1);
                startPoint = MajGeo.PointGroupA(startPos);
                i = 1;
            }
            else
            {
                return false;
            }

            // 跳过头部的残留修饰符（f/h）
            while (i < rawContent.Length && (rawContent[i] == 'f' || rawContent[i] == 'h')) i++;

            endPos = startPos;

            while (i < rawContent.Length)
            {
                var c = rawContent[i];
                if (c == '[')
                {
                    var close = rawContent[i..].IndexOf(']');
                    if (close < 0) break;
                    i += close + 1;
                    continue;
                }
                if (c is 'f' or 'h')
                {
                    i++;
                    continue;
                }
                if (c is '-' or '^' or '>' or '<' or 'v' or 'V' or 'p' or 'q' or 's' or 'z' or 'w')
                {
                    var shape = c;
                    var isDouble = false;
                    i++;
                    // pp/qq 双字符形状（EdgeCurve，与 SimaiSharp 词法一致）
                    if (shape is 'p' or 'q' && i < rawContent.Length && rawContent[i] == shape)
                    {
                        isDouble = true;
                        i++;
                    }
                    // V 带折点：先折点后终点，按两段直线处理
                    if (shape == 'V')
                    {
                        if (!TryReadSlideLocation(rawContent, ref i, out var flexion, out var flexSensor, out var flexBtn0, out var flexButton))
                            return false;
                        segs.Add(new SensorSlideSeg('-', false, flexion, flexSensor, flexBtn0));
                        endPos = flexButton;
                    }
                    if (!TryReadSlideLocation(rawContent, ref i, out var endPt, out var endSensor, out var endBtn0, out var endButton))
                        return false;
                    segs.Add(new SensorSlideSeg(shape == 'V' ? '-' : shape, isDouble, endPt, endSensor, endBtn0));
                    endPos = endButton;
                    continue;
                }
                // 未识别字符（不应出现）：跳过
                i++;
            }

            return segs.Count > 0;
        }

        private static bool TryReadSlideLocation(
            ReadOnlySpan<char> rawContent,
            ref int i,
            out Complex pos,
            out SensorType sensor,
            out int btn0,
            out int button)
        {
            pos = Complex.Zero;
            sensor = SensorType.Invalid;
            btn0 = 0;
            button = 0;
            if (i >= rawContent.Length) return false;

            if (rawContent[i] is >= '1' and <= '8')
            {
                button = rawContent[i] - '0';
                i++;
                btn0 = button - 1;
                sensor = (SensorType)(button - 1);
                pos = MajGeo.PointGroupA(button);
                return true;
            }
            if (rawContent[i] is >= 'A' and <= 'E')
            {
                var area = rawContent[i];
                i++;
                if (area == 'C')
                {
                    if (i < rawContent.Length && char.IsDigit(rawContent[i])) i++;
                    sensor = SensorType.C;
                    btn0 = 0;
                    button = 8;
                    pos = ToComplex(MajPos.GetAreaPos(sensor));
                    return true;
                }
                if (i >= rawContent.Length || rawContent[i] is < '1' or > '8')
                    return false;
                var idx = rawContent[i] - '0';
                i++;
                sensor = SensorOf(area, idx);
                btn0 = idx - 1;
                button = idx;
                pos = ToComplex(MajPos.GetAreaPos(sensor));
                return true;
            }
            return false;
        }

        private static Complex ToComplex(float2 v) => new(v.x, v.y);

        /// <summary>环向（>、&lt;、^）slide 的顺/逆时针判定，复刻 SimaiSharp Deserializer.DetermineRingType。</summary>
        private static bool RingIsCw(char shape, int startBtn0, int endBtn0)
        {
            switch (shape)
            {
                case '>':
                    return !((startBtn0 + 2) % 8 >= 4);
                case '<':
                    return (startBtn0 + 2) % 8 >= 4;
                default: // '^' 最短路径
                {
                    var diff = endBtn0 - startBtn0;
                    var rotation = diff >= 0 ? (diff > 4 ? -1 : 1) : (diff < -4 ? 1 : -1);
                    return rotation > 0;
                }
            }
        }

        /// <summary>
        /// 生成传感区 slide 的 metadata（路径 + 判定区 + 箭头）。
        /// </summary>
        public static SlideMetadata Build(
            List<SensorSlideSeg> segs,
            SensorType headSensor,
            Complex startPoint)
        {
            var constructor = SlidePathConstructor.BeginAt(startPoint);
            var nodeLens = new List<double> { 0.0 };
            var nodeSensors = new List<SensorType> { headSensor };
            var totalLen = 0.0;
            var current = startPoint;
            var currentBtn0 = headSensor switch
            {
                SensorType.C => 0,
                // 0-based 键位：D1=17 → 0，即 (int)sensor - 17；其余同理
                >= SensorType.D1 and <= SensorType.D8 => (int)headSensor - 17,
                >= SensorType.E1 and <= SensorType.E8 => (int)headSensor - 25,
                >= SensorType.B1 and <= SensorType.B8 => (int)headSensor - 8,
                >= SensorType.A1 and <= SensorType.A8 => (int)headSensor,
                _ => 0
            };

            foreach (var seg in segs)
            {
                var end = seg.End;
                switch (seg.Shape)
                {
                    case '-':
                        {
                            var d = (end - current).Magnitude;
                            constructor.LineToPoint(end);
                            totalLen += d;
                            break;
                        }
                    case 'v':
                        {
                            totalLen += current.Magnitude;
                            constructor.LineToPoint(Complex.Zero);
                            var d = end.Magnitude;
                            constructor.LineToPoint(end);
                            totalLen += d;
                            break;
                        }
                    case '^':
                    case '>':
                    case '<':
                        {
                            if (current != Complex.Zero && end != Complex.Zero)
                            {
                                var isCw = RingIsCw(seg.Shape, currentBtn0, seg.EndBtn0);
                                // AstroDX Ring 滑条：角度与半径同时线性插值（平滑键入/退出触区）；
                                // 跨度含 Tau/32 wrap 加成（与 Trigonometry.GetAngleSpan 一致）
                                constructor.SpiralToPoint(end, !isCw);
                                var delta = RingSegment.CalcSpan(current, end, !isCw);
                                totalLen += delta * (current.Magnitude + end.Magnitude) / 2.0;
                            }
                            else
                            {
                                var d = (end - current).Magnitude;
                                constructor.LineToPoint(end);
                                totalLen += d;
                            }
                            break;
                        }
                    case 'p':
                    case 'q':
                        {
                            // AstroDX CurveCw/Ccw（p/q）与 EdgeCurveCw/Ccw（pp/qq）：
                            // 外圈顶点角度 → 切线 → 内圆/偏移圆圆弧 → 切线 → 外圈终点
                            AddCurveSegment(constructor, ref totalLen, current, seg, isCw: seg.Shape == 'q');
                            current = Complex.FromPolarCoordinates(MajGeo.MainRadius, VertexAngle(seg.End));
                            break;
                        }
                    default:
                        {
                            // s/z/w 与传感区组合罕见：按直线近似
                            var d = (end - current).Magnitude;
                            constructor.LineToPoint(end);
                            totalLen += d;
                            break;
                        }
                }
                nodeLens.Add(totalLen);
                nodeSensors.Add(seg.EndSensor);
                current = end;
                currentBtn0 = seg.EndBtn0;
            }

            var path = constructor.GeneratePath();
            var arrowRaw = SlideDataBuilder.BuildArrowData(path);
            var pathTotal = path.GetPathLength();
            var arrowCount = arrowRaw.Length;
            var fracs = new double[nodeLens.Count];
            for (var k = 0; k < nodeLens.Count; k++)
                fracs[k] = pathTotal > 0 ? nodeLens[k] / pathTotal : 0.0;

            var areaList = new List<SlideArea>(nodeLens.Count);
            for (var k = 0; k < nodeLens.Count - 1; k++)
            {
                var push = ArrowIdxAt(arrowRaw, fracs[k]);
                var finish = ArrowIdxAt(arrowRaw, fracs[k + 1]);
                areaList.Add(new SlideArea(push, finish, nodeSensors[k], SensorType.Invalid));
            }
            areaList.Add(new SlideArea(arrowCount, arrowCount, nodeSensors[^1], SensorType.Invalid));

            var poses = new SlidePose[arrowCount];
            for (var k = 0; k < arrowCount; k++)
                poses[k] = SlideTableNeo.CalcArrowPose(arrowRaw[k]);

            var conditionalLastArrow =
                arrowRaw[^1].PathLength - arrowRaw[^2].PathLength <= MajGeo.DefaultDistance / 2.0;

            // 尾判窗口：镜像标准 slide（末段入点），单段时取固定比例
            var slideConst = nodeLens.Count <= 2
                ? 0.15f
                : (float)Math.Clamp(1.0 - fracs[^2], 0.05, 1.0);

            // 传感区 slide 不显示 SlideOK：okPose/okType 仅占位（SlideUpdateJob 跳过渲染）
            return new SlideMetadata(
                areaList.ToArray(),
                slideConst,
                (float)pathTotal,
                poses,
                conditionalLastArrow,
                default,
                SlideOkType.StraightL,
                SlideFlag.None);
        }

        private static int ArrowIdxAt(SlideArrowRawData[] arrowRaw, double frac)
        {
            if (arrowRaw.Length <= 2) return 1;
            var target = arrowRaw[^1].PathLength * frac;
            var idx = 1;
            while (idx < arrowRaw.Length - 1 && arrowRaw[idx].PathLength <= target) idx++;
            return Math.Max(1, Math.Min(idx - 1, arrowRaw.Length - 2));
        }

        /// <summary>顶点角度（我的坐标系）：按钮/传感区位置相位；C 区按按钮 1 的角度（对应 AstroDX Location.index=0）。</summary>
        private static double VertexAngle(Complex pos)
        {
            if (pos == Complex.Zero) return MajGeo.PointGroupA(1).Phase;
            return pos.Phase;
        }

        /// <summary>AstroDX GetAngleSpan：按方向归一化跨度，≤ wrapThreshold 时加成整圈。</summary>
        private static double GetAngleSpanWrap(double from, double to, bool isCw, double wrapThreshold)
        {
            var raw = isCw ? from - to : to - from;
            raw = Math.IEEERemainder(raw, Math.PI * 2.0);
            if (raw < 0) raw += Math.PI * 2.0;
            if (raw <= wrapThreshold) raw += Math.PI * 2.0;
            return raw;
        }

        /// <summary>
        /// 复刻 AstroDX CurveCw/CcwGenerator 与 EdgeCurveCw/CcwGenerator：
        /// 起点 = 外圈（PlayFieldRadius）上的起始顶点角度（链上当前位置先直线衔接过去，与 AstroDX 行为一致），
        /// 切线进入内圆（p/q，半径 CenterRadius）或偏移圆（pp/qq，半径 CenterRadius×1.2，圆心偏移
        /// PlayFieldRadius×0.4662、角度偏移 ±(Tau/4−Tau/16)），圆弧按方向行进，再切线引出到外圈终点。
        /// </summary>
        private static void AddCurveSegment(
            SlidePathConstructor constructor,
            ref double totalLen,
            Complex current,
            in SensorSlideSeg seg,
            bool isCw)
        {
            var ringRadius = MajGeo.MainRadius;
            var curveRadius = seg.IsDoubleChar ? MajGeo.CenterRadius * 1.2 : MajGeo.CenterRadius;
            var startAngle = VertexAngle(current);
            var endAngle = VertexAngle(seg.End);

            var startPoint = Complex.FromPolarCoordinates(ringRadius, startAngle);
            var endPoint = Complex.FromPolarCoordinates(ringRadius, endAngle);

            Complex center;
            if (seg.IsDoubleChar)
            {
                // AstroDX EdgeCurve: CenterAngularOffset = Tau/4 - Tau/16 = 3π/8 = 67.5°
                // （注意 Tau=2π，故 Tau/4-Tau/16 = π/2-π/8 = 3π/8，不是 3π/16）
                var offset = Math.PI / 2.0 - Math.PI / 8.0;
                var centerAngle = isCw ? startAngle + offset : startAngle - offset;
                center = Complex.FromPolarCoordinates(ringRadius * 0.4662, centerAngle);
            }
            else
            {
                center = Complex.Zero;
            }

            // AstroDX 的曲线段起点固定在外圈顶点角度：先直线衔接当前位置
            constructor.LineToPoint(startPoint);
            totalLen += (startPoint - current).Magnitude;

            var relStart = startPoint - center;
            var startDelta = Math.Acos(Math.Min(1.0, curveRadius / relStart.Magnitude));
            var tangentInRel = isCw ? relStart.Phase - startDelta : relStart.Phase + startDelta;
            var tangentInPoint = center + Complex.FromPolarCoordinates(curveRadius, tangentInRel);
            constructor.LineToPoint(tangentInPoint);
            totalLen += (tangentInPoint - startPoint).Magnitude;

            var relEnd = endPoint - center;
            var endDelta = Math.Acos(Math.Min(1.0, curveRadius / relEnd.Magnitude));
            // AstroDX：CW 出口 delta = +acos（clockwise=false），CCW 出口 delta = −acos（clockwise=true）
            var tangentOutRel = isCw ? relEnd.Phase + endDelta : relEnd.Phase - endDelta;
            var tangentOutPoint = center + Complex.FromPolarCoordinates(curveRadius, tangentOutRel);

            var wrapThreshold = seg.IsDoubleChar ? Math.PI / 4.0 : RingSegment.WrapThreshold; // Tau/4 或 Tau/32
            var curveSpan = GetAngleSpanWrap(tangentInRel, tangentOutRel, isCw, wrapThreshold);
            // 关键：让实际绘制的圆弧也使用带 wrap 的跨度（AstroDX 在跨度 ≤ 阈值时会绕满一整圈）。
            // ArcToAngle 只做 [-π,π] 归一化的"最短方向"，必须把 wrap 后的跨度折算进目标辐角。
            var drawnEndRel = isCw ? tangentInRel - curveSpan : tangentInRel + curveSpan;
            constructor.ArcToAngle(center, drawnEndRel, !isCw, false);
            totalLen += curveSpan * curveRadius;

            constructor.LineToPoint(endPoint);
            totalLen += (endPoint - tangentOutPoint).Magnitude;
        }
    }
}
