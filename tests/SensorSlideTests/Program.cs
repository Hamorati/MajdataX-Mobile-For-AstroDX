using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MajSimai;
using MajdataViewX.Notes.SlideUtils;
using MajdataViewX.Types.Input;

// 用真实谱面验证 ViewX 传感区 slide 几何/判定区构建代码（链接同一份源码）
var root = args.Length > 0 ? args[0] : @"D:\Workspace";
var failures = 0;
var total = 0;

foreach (var (name, chartIdx) in new[] { ("1.zip", 4), ("2.zip", 5), ("3.zip", 7), ("4.zip", 7) })
{
    var file = ParseFile(root, name);
    var chart = file.Charts[chartIdx];
    if (chart.IsEmpty)
    {
        Console.WriteLine($"FAIL {name}: chart {chartIdx} empty");
        failures++;
        continue;
    }
    var sensorSlides = chart.NoteTimings.ToArray().SelectMany(t => t.Notes)
        .Where(n => n.IsSensorSlide && n.Type == SimaiNoteType.Slide).ToList();
    Console.WriteLine($"===== {name}: {sensorSlides.Count} sensor slides =====");
    foreach (var n in sensorSlides)
    {
        total++;
        if (!SensorSlideBuilder.TryParse(n.RawContent, out var head, out var sPos, out var sPoint, out var segs, out var ePos))
        {
            Console.WriteLine($"FAIL {name} parse: '{n.RawContent}'");
            failures++;
            continue;
        }
        var meta = SensorSlideBuilder.Build(segs, head, sPoint);
        var f = Validate(name, n.RawContent, meta);
        if (f != 0)
        {
            failures += f;
            Console.WriteLine($"FAIL {name}: '{n.RawContent}' head={(int)head} segs={string.Join("", segs.Select(s => s.Shape))}");
        }
        else
        {
            Console.WriteLine($"  ok '{n.RawContent}' -> len={meta.SlideLength:F2} arrows={meta.ArrowPoses.Length} areas={meta.JudgeAreaQueue.Length} head={head}");
        }
    }
}

Console.WriteLine(failures == 0 ? $"SENSOR-GEO-OK (checked {total} slides)" : $"SENSOR-GEO-FAILURES={failures}/{total}");

// ---- wrap 阈值单测（AstroDX GetAngleSpan 行为） ----
var a1 = MajdataViewX.Base.MajGeo.PointGroupA(1);
var a2 = MajdataViewX.Base.MajGeo.PointGroupA(2);
var spanSame = MajdataViewX.Notes.SlideUtils.RingSegment.CalcSpan(a1, a1, true);
var span45 = MajdataViewX.Notes.SlideUtils.RingSegment.CalcSpan(a1, a2, true);
// a1=67.5°, a2=22.5°：CCW（角度递增）跨度 = 315° = 7π/4（与 AstroDX GetAngleSpan 同公式）
Console.WriteLine($"wrap test: samePoint span={spanSame:F4} (expect {2 * Math.PI:F4}), ccw67->22 span={span45:F4} (expect {7 * Math.PI / 4:F4})");
if (Math.Abs(spanSame - 2 * Math.PI) > 1e-6) { Console.WriteLine("FAIL wrap same-point"); failures++; }
if (Math.Abs(span45 - 7 * Math.PI / 4) > 1e-6) { Console.WriteLine("FAIL wrap ccw span"); failures++; }

// 合成 wrap slide：1>1[8:1] → 整圈，长度 = 2π × 4.8
if (SensorSlideBuilder.TryParse("1>1[8:1]", out var wh, out var ws, out var wp, out var wsegs, out var we))
{
    var wmeta = SensorSlideBuilder.Build(wsegs, wh, wp);
    var expect = 2 * Math.PI * 4.8;
    Console.WriteLine($"wrap slide len={wmeta.SlideLength:F3} (expect {expect:F3})");
    if (Math.Abs(wmeta.SlideLength - expect) > 0.05) { Console.WriteLine("FAIL wrap slide length"); failures++; }
}
else { Console.WriteLine("FAIL wrap slide parse"); failures++; }

// ---- pp/qq 双字符形状（传感区锚定，用户反馈 D1bqq4 案例） ----
foreach (var (raw, expHead, expEndBtn) in new[]
{
    ("D1qq4[4:3]", SensorType.D1, 4),
    ("D1pp4[4:3]", SensorType.D1, 4),
    ("E3qqD5[4:3]", SensorType.E3, 5),
    ("Cqq4[4:3]", SensorType.C, 4),
    ("D2ppE7[4:3]", SensorType.D2, 7),
})
{
    if (!SensorSlideBuilder.TryParse(raw, out var h, out var sp, out var pt, out var segs, out var ep))
    {
        Console.WriteLine($"FAIL pp/qq parse '{raw}'");
        failures++;
        continue;
    }
    if (h != expHead || ep != expEndBtn || segs.Count != 1 || !segs[0].IsDoubleChar)
    {
        Console.WriteLine($"FAIL pp/qq fields '{raw}': head={(int)h} exp={(int)expHead} end={ep} exp={expEndBtn} segs={segs.Count} double={segs[0].IsDoubleChar}");
        failures++;
        continue;
    }
    var m = SensorSlideBuilder.Build(segs, h, pt);
    if (m.SlideLength <= 0 || double.IsNaN(m.SlideLength)) { Console.WriteLine($"FAIL pp/qq len '{raw}'"); failures++; }
    Console.WriteLine($"ok pp/qq '{raw}' -> head={h} len={m.SlideLength:F2}");
}

Console.WriteLine(failures == 0 ? "WRAP-OK" : $"WRAP-FAILURES={failures}");

// ---- D1qq4 EdgeCurve 完整环路（AstroDX wrap 行为，用户 D1bqq4 反馈）----
// 理想几何（AstroDX EdgeCurveCwGenerator，offset=3π/8）：
//   圆心 (-2.0674, 0.8564)、曲线半径 2.2043、CW 345° 大环；路径应经过环的远侧 (-4.27, 0.86) 附近。
{
    if (!SensorSlideBuilder.TryParse("D1qq4[4:3]", out var h, out var sp, out var pt, out var segs, out var ep))
    {
        Console.WriteLine("FAIL D1qq4 parse");
        failures++;
    }
    else
    {
        var m = SensorSlideBuilder.Build(segs, h, pt);
        var farSideHit = m.ArrowPoses.Any(a => a.X < -3.5 && Math.Abs(a.X + 4.271) < 0.6 && Math.Abs(a.Y - 0.856) < 0.6);
        Console.WriteLine($"D1qq4: len={m.SlideLength:F2} arrows={m.ArrowPoses.Length} farSideHit={farSideHit}");
        Console.WriteLine($"  (expect len≈23.3，含绕行远侧 (-4.27, 0.86) 的箭头)");
        if (!farSideHit) { Console.WriteLine("FAIL D1qq4: loop far side not traversed"); failures++; }
        if (m.SlideLength < 20) { Console.WriteLine("FAIL D1qq4: length too short (loop missing)"); failures++; }
    }
}

// ---- D1pp4（镜像方向）同样应形成大环 ----
{
    if (!SensorSlideBuilder.TryParse("D1pp4[4:3]", out var h, out var sp, out var pt, out var segs, out var ep))
    {
        Console.WriteLine("FAIL D1pp4 parse");
        failures++;
    }
    else
    {
        var m = SensorSlideBuilder.Build(segs, h, pt);
        // pp（CCW）：偏移圆心在 (2.067, 0.856)，远侧为 (+4.27, 0.86)
        var farSideHit = m.ArrowPoses.Any(a => a.X > 3.5 && Math.Abs(a.X - 4.271) < 0.6 && Math.Abs(a.Y - 0.856) < 0.6);
        Console.WriteLine($"D1pp4: len={m.SlideLength:F2} arrows={m.ArrowPoses.Length} farSideHit={farSideHit}");
        if (!farSideHit) { Console.WriteLine("FAIL D1pp4: loop far side not traversed"); failures++; }
        if (m.SlideLength < 20) { Console.WriteLine("FAIL D1pp4: length too short (loop missing)"); failures++; }
    }
}

// ---- D2>5-B6-2[8:3] 环向（用户反馈：D2 起始的 '>' 环走错方向）----
// SimaiSharp DetermineRingType：D2 index=1，(1+2)%8=3 < 4 → '>' = RingCw（顺时针，短弧 157.5°）
// 修正前 head button0 偏一（D2→2），方向翻转走 202.5° 长弧
{
    if (!SensorSlideBuilder.TryParse("D2>5-B6-2[8:3]", out var h, out var sp, out var pt, out var segs, out var ep))
    {
        Console.WriteLine("FAIL D2>5 parse");
        failures++;
    }
    else
    {
        var m = SensorSlideBuilder.Build(segs, h, pt);
        // 正确（CW 短弧）：环经过右侧 (4.3, ~0)；错误（CCW 长弧）：环经过左侧 (−4.3, ~0)
        var hasRight = m.ArrowPoses.Any(a => a.X > 4.0f && Math.Abs(a.Y) < 1.0f);
        var hasFarLeft = m.ArrowPoses.Any(a => a.X < -4.0f && Math.Abs(a.Y) < 1.0f);
        Console.WriteLine($"D2>5: len={m.SlideLength:F2} arrows={m.ArrowPoses.Length} rightSideHit={hasRight} farLeftHit={hasFarLeft}");
        if (!hasRight || hasFarLeft) { Console.WriteLine("FAIL D2>5: ring direction wrong (should be CW short arc via right side)"); failures++; }
    }
}

// ---- D1<5-B4-8[8:3]（配对的 '<' 环，起于 D1 index=0 → RingCcw，短弧 157.5°）----
{
    if (!SensorSlideBuilder.TryParse("D1<5-B4-8[8:3]", out var h, out var sp, out var pt, out var segs, out var ep))
    {
        Console.WriteLine("FAIL D1<5 parse");
        failures++;
    }
    else
    {
        var m = SensorSlideBuilder.Build(segs, h, pt);
        // 正确（CCW 短弧）：环经过左侧 (−4.3, ~0)；错误：经过右侧
        var hasLeft = m.ArrowPoses.Any(a => a.X < -4.0f && Math.Abs(a.Y) < 1.0f);
        var hasFarRight = m.ArrowPoses.Any(a => a.X > 4.0f && Math.Abs(a.Y) < 1.0f);
        Console.WriteLine($"D1<5: len={m.SlideLength:F2} arrows={m.ArrowPoses.Length} leftSideHit={hasLeft} farRightHit={hasFarRight}");
        if (!hasLeft || hasFarRight) { Console.WriteLine("FAIL D1<5: ring direction wrong (should be CCW short arc via left side)"); failures++; }
    }
}

Console.WriteLine(failures == 0 ? "RING-OK" : $"RING-FAILURES={failures}");
return failures == 0 ? 0 : 1;

static SimaiFile ParseFile(string root, string name)
{
    var path = Path.Combine(root, "analysis", name.Replace(".zip", ""), "maidata.txt");
    var content = File.ReadAllText(path);
    return SimaiParser.ParseAsync(content, "").GetAwaiter().GetResult();
}

static int Validate(string name, string raw, SlideMetadata meta)
{
    var f = 0;
    if (meta.SlideLength <= 0 || double.IsNaN(meta.SlideLength) || double.IsInfinity(meta.SlideLength))
    {
        Console.WriteLine($"  BAD length {meta.SlideLength}");
        f++;
    }
    var arrows = meta.ArrowPoses;
    if (arrows.Length < 2)
    {
        Console.WriteLine($"  BAD arrow count {arrows.Length}");
        f++;
    }
    foreach (var a in arrows)
    {
        if (float.IsNaN(a.X) || float.IsNaN(a.Y) || float.IsNaN(a.RotZ) || float.IsNaN(a.L) ||
            float.IsInfinity(a.X) || float.IsInfinity(a.Y) || float.IsInfinity(a.RotZ) || float.IsInfinity(a.L))
        {
            Console.WriteLine($"  BAD arrow NaN/Inf: x={a.X} y={a.Y} r={a.RotZ} l={a.L}");
            f++;
        }
    }
    var areas = meta.JudgeAreaQueue;
    if (areas.Length == 0)
    {
        Console.WriteLine("  BAD no judge areas");
        return f + 1;
    }
    var prev = 0;
    for (var i = 0; i < areas.Length; i++)
    {
        var a = areas[i];
        var isLast = i == areas.Length - 1;
        if ((int)a.SensorA < 0 || (int)a.SensorA > 32)
        {
            Console.WriteLine($"  BAD sensorA {(int)a.SensorA} at area {i}");
            f++;
        }
        if (a.SensorB != SensorType.Invalid)
        {
            Console.WriteLine($"  BAD sensorB {(int)a.SensorB} at area {i}");
            f++;
        }
        if (isLast)
        {
            if (a.ArrowProgressPush != arrows.Length || a.ArrowProgressFinish != arrows.Length)
            {
                Console.WriteLine($"  BAD last area {a.ArrowProgressPush}/{a.ArrowProgressFinish} arrows={arrows.Length}");
                f++;
            }
        }
        else
        {
            if (a.ArrowProgressPush < 1 || a.ArrowProgressFinish < 1 ||
                a.ArrowProgressPush > a.ArrowProgressFinish ||
                a.ArrowProgressFinish > arrows.Length - 2)
            {
                Console.WriteLine($"  BAD area {i} push={a.ArrowProgressPush} finish={a.ArrowProgressFinish} arrows={arrows.Length}");
                f++;
            }
            if (a.ArrowProgressPush < prev)
            {
                Console.WriteLine($"  BAD non-monotonic at area {i}");
                f++;
            }
            prev = a.ArrowProgressFinish;
        }
    }
    if (areas[0].ArrowProgressPush != 1 && areas.Length > 1)
    {
        Console.WriteLine($"  NOTE: first area push={areas[0].ArrowProgressPush} (expected 1)");
    }
    return f;
}
