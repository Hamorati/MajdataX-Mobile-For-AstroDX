using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MajSimai;
#if FORK
using MemoryPack;
#endif

// 用法: MajSimaiParseTests [root]
// Ref 模式: 输出 1.zip/2.zip 的标准 dump（与旧解析器行为对照）
// Fork 模式: 输出 dump + 运行断言（传感区 slide / 第8难度 / MemoryPack 往返）
var root = args.Length > 0 ? args[0] : @"D:\Workspace";
var failures = 0;

#if FORK
failures += Check1(root);
failures += Check2(root);
failures += Check8(root, "3.zip");
failures += Check8(root, "4.zip");
failures += CheckMemoryPack(root);
failures += Check5(root);
#endif

Dump(root, "1.zip", 4);
Dump(root, "2.zip", 5);
Dump(root, "5.zip", 7);
#if FORK
Dump(root, "3.zip", 7);
Dump(root, "4.zip", 7);
Console.WriteLine(failures == 0 ? "FORK-ASSERTS-OK" : $"FORK-FAILURES={failures}");
#else
Console.WriteLine("REF-DUMP-DONE");
#endif
return failures == 0 ? 0 : 1;

static SimaiFile ParseFile(string root, string name)
{
    var path = Path.Combine(root, "analysis", name.Replace(".zip", ""), "maidata.txt");
    var content = File.ReadAllText(path);
    return SimaiParser.ParseAsync(content, "").GetAwaiter().GetResult();
}

static void Dump(string root, string name, int chartIndex)
{
    var file = ParseFile(root, name);
    Console.WriteLine($"===== DUMP {name} =====");
    var chart = file.Charts[chartIndex];
    if (chart.IsEmpty)
    {
        Console.WriteLine($"[chart {chartIndex} EMPTY]");
        return;
    }
    var byType = chart.NoteTimings.ToArray().SelectMany(t => t.Notes).GroupBy(n => n.Type)
        .ToDictionary(g => g.Key, g => g.Count());
    Console.WriteLine($"chart {chartIndex} level={chart.Level} designer={chart.Designer} notes={chart.NoteTimings.Length} timings types=" +
        string.Join(",", byType.Select(kv => $"{kv.Key}:{kv.Value}")));
    foreach (var t in chart.NoteTimings.ToArray())
    {
        foreach (var n in t.Notes)
        {
            Console.WriteLine($"{n.Type,-9} pos={n.StartPosition} area={n.TouchArea} " +
                $"noHead={n.IsSlideNoHead} tapHead={n.IsTapHeadSlide} brk={n.IsBreak} sBrk={n.IsSlideBreak} " +
                $"mine={n.IsMine} sMine={n.IsMineSlide} st={n.SlideStartTime:F3} len={n.SlideTime:F3} hold={n.HoldTime:F3} " +
#if FORK
                $"sensor={n.IsSensorSlide} " +
#endif
                $"raw=\"{n.RawContent}\"");
        }
    }
}

#if FORK
static int Check5(string root)
{
    var f = 0;
    var file = ParseFile(root, "5.zip");
    var chart = file.Charts[7];
    if (chart.IsEmpty) { Console.WriteLine("FAIL: 5.zip lv8 chart empty"); return 1; }
    var notes = chart.NoteTimings.ToArray().SelectMany(t => t.Notes).ToArray();
    var sensorSlides = notes.Where(n => n.IsSensorSlide).ToArray();
    Console.WriteLine($"check5: 5.zip lv8 notes={notes.Length} sensorSlides={sensorSlides.Length}");

    // D1bqq4[4:3]：触区锚定 EdgeCurve
    var d1 = notes.FirstOrDefault(n => n.RawContent.StartsWith("D1"));
    if (d1 is null || !d1.IsSensorSlide || d1.TouchArea != 'D' || d1.StartPosition != 1 || !d1.IsSlideNoHead)
    {
        Console.WriteLine($"FAIL: 5.zip D1bqq4 wrong: {d1?.RawContent} area={d1?.TouchArea} pos={d1?.StartPosition}");
        f++;
    }
    else Console.WriteLine($"  ok D1bqq4 -> area=D pos=1 raw='{d1.RawContent}'");

    // D6bpp3[4:3]*pp3[4:3]：触区锚定同起点链（第二支应为 D6 头 + pp3）
    var d6pp = notes.Where(n => n.RawContent.StartsWith("D6pp3")).ToArray();
    Console.WriteLine($"  D6pp3 notes: {string.Join(" | ", d6pp.Select(n => n.RawContent))}");
    if (d6pp.Length != 2) { Console.WriteLine($"FAIL: 5.zip D6pp3 chain expected 2 notes, got {d6pp.Length}"); f++; }
    foreach (var n in d6pp)
    {
        if (n.TouchArea != 'D' || n.StartPosition != 6 || !n.IsSlideNoHead || !n.IsSensorSlide)
        {
            Console.WriteLine($"FAIL: 5.zip D6 chain wrong: '{n.RawContent}' area={n.TouchArea} pos={n.StartPosition}");
            f++;
        }
    }

    // 相邻直线链 2bx-1-8-7-6[1:1]b（AstroDX 允许相邻直线段）
    var chain = notes.FirstOrDefault(n => n.RawContent.Contains("-1-8-7-6"));
    if (chain is null) { Console.WriteLine("FAIL: 5.zip adjacent straight chain missing"); f++; }
    else Console.WriteLine($"  ok adjacent chain raw='{chain.RawContent}'");
    return f;
}
#endif

static int Check1(string root)
{
    var f = 0;
    var file = ParseFile(root, "1.zip");
    var chart = file.Charts[4];
    if (chart.IsEmpty) { Console.WriteLine("FAIL: 1.zip chart empty"); return 1; }
    var sensorSlides = chart.NoteTimings.ToArray().SelectMany(t => t.Notes)
        .Where(n => n.IsSensorSlide).Count();
    if (sensorSlides != 0) { Console.WriteLine($"FAIL: 1.zip has {sensorSlides} sensor slides (expect 0)"); f++; }
    var taps = chart.NoteTimings.ToArray().SelectMany(t => t.Notes).Count(n => n.Type == SimaiNoteType.Tap);
    var slides = chart.NoteTimings.ToArray().SelectMany(t => t.Notes).Count(n => n.Type == SimaiNoteType.Slide);
    if (taps == 0 || slides == 0) { Console.WriteLine("FAIL: 1.zip tap/slide count zero"); f++; }
    Console.WriteLine($"check1: 1.zip taps={taps} slides={slides} sensorSlides={sensorSlides}");
    return f;
}

static int Check2(string root)
{
    var f = 0;
    var file = ParseFile(root, "2.zip");
    var chart = file.Charts[5];
    if (chart.IsEmpty) { Console.WriteLine("FAIL: 2.zip chart empty"); return 1; }
    var notes = chart.NoteTimings.ToArray().SelectMany(t => t.Notes).ToArray();
    var sensorSlides = notes.Where(n => n.IsSensorSlide).ToArray();
    Console.WriteLine($"check2: sensor slides total={sensorSlides.Length}");

    // 6-B6-B8-B2-2 : 按钮锚定
    var b6 = notes.FirstOrDefault(n => n.RawContent.StartsWith("6-B6"));
    if (b6 is null || !b6.IsSensorSlide || b6.StartPosition != 6 || b6.TouchArea != ' ' || b6.IsSlideNoHead)
    { Console.WriteLine($"FAIL: 6-B6-... note wrong: {b6?.RawContent}"); f++; }
    else Console.WriteLine($"  ok 6-B6-... start=6 area=' ' sensor={b6.IsSensorSlide}");

    // E3-E5-D7-D1>4b : 触区锚定
    var e3 = notes.FirstOrDefault(n => n.RawContent.StartsWith("E3-"));
    if (e3 is null || !e3.IsSensorSlide || e3.StartPosition != 3 || e3.TouchArea != 'E' || !e3.IsSlideNoHead || e3.Type != SimaiNoteType.Slide)
    { Console.WriteLine($"FAIL: E3-... note wrong: {e3?.RawContent}"); f++; }
    else Console.WriteLine($"  ok E3-... start=3 area=E noHead={e3.IsSlideNoHead}");

    // D2>5-B6-2 : 触区锚定
    var d2 = notes.FirstOrDefault(n => n.RawContent.StartsWith("D2>"));
    if (d2 is null || !d2.IsSensorSlide || d2.TouchArea != 'D' || !d2.IsSlideNoHead)
    { Console.WriteLine($"FAIL: D2>... note wrong: {d2?.RawContent}"); f++; }
    else Console.WriteLine($"  ok D2>... area=D");

    // 8x>E7-C[16:27] : 按钮锚定 + 传感顶点
    var e7c = notes.FirstOrDefault(n => n.RawContent.Contains("E7-C"));
    if (e7c is null || !e7c.IsSensorSlide || e7c.TouchArea != ' ')
    { Console.WriteLine($"FAIL: 8x>E7-C note wrong: {e7c?.RawContent}"); f++; }
    else Console.WriteLine($"  ok 8x>E7-C");

    // 触区锚定的 slide 都应为 Slide 类型 + noHead + 有 TouchArea
    var anchored = sensorSlides.Where(n => n.TouchArea != ' ').ToArray();
    var badAnchored = anchored.Count(n => n.Type != SimaiNoteType.Slide || !n.IsSlideNoHead);
    if (badAnchored != 0) { Console.WriteLine($"FAIL: {badAnchored} anchored slides have wrong type/noHead"); f++; }
    Console.WriteLine($"  anchored slides={anchored.Length}");
    return f;
}

static int Check8(string root, string name)
{
    var f = 0;
    var file = ParseFile(root, name);
    var chart = file.Charts[7];
    if (chart.IsEmpty)
    {
        Console.WriteLine($"FAIL: {name} inote_8 chart empty (G16)"); return 1;
    }
    var notes = chart.NoteTimings.ToArray().SelectMany(t => t.Notes).ToArray();
    var sensorSlides = notes.Count(n => n.IsSensorSlide);
    var mines = notes.Count(n => n.IsMine || n.IsMineSlide);
    var anchored = notes.Count(n => n.Type == SimaiNoteType.Slide && n.TouchArea != ' ');
    Console.WriteLine($"check8: {name} lv={chart.Level} des={chart.Designer} sensorSlides={sensorSlides} mines={mines} anchoredSlides={anchored}");
    if (sensorSlides == 0) { Console.WriteLine($"FAIL: {name} no sensor slides"); f++; }
    if (mines == 0) { Console.WriteLine($"FAIL: {name} no mine notes"); f++; }
    if (anchored == 0) { Console.WriteLine($"FAIL: {name} no anchored slides"); f++; }
    return f;
}

static int CheckMemoryPack(string root)
{
    var f = 0;
    var file = ParseFile(root, "2.zip");
    var bytes = MemoryPackSerializer.Serialize(file);
    var rt = MemoryPackSerializer.Deserialize<SimaiFile>(bytes);
    if (rt is null) { Console.WriteLine("FAIL: MemoryPack round-trip null"); return 1; }
    var srcCount = file.Charts[5].NoteTimings.Length;
    var rtCount = rt.Charts[5].NoteTimings.Length;
    if (srcCount != rtCount) { Console.WriteLine($"FAIL: round-trip timing count {srcCount} -> {rtCount}"); f++; }
    var rtSensor = rt.Charts[5].NoteTimings.ToArray().SelectMany(t => t.Notes).Any(n => n.IsSensorSlide);
    if (!rtSensor) { Console.WriteLine("FAIL: round-trip lost IsSensorSlide"); f++; }
    var rtE3 = rt.Charts[5].NoteTimings.ToArray().SelectMany(t => t.Notes)
        .FirstOrDefault(n => n.RawContent.StartsWith("E3-"));
    if (rtE3 is null || rtE3.TouchArea != 'E' || !rtE3.IsSlideNoHead)
    { Console.WriteLine("FAIL: round-trip E3 slide fields wrong"); f++; }
    // Deparse 往返（fumen 原文直写）
    var text = SimaiParser.Deparse(file);
    var reparsed = SimaiParser.ParseAsync(text, "").GetAwaiter().GetResult();
    if (reparsed.Charts[5].NoteTimings.Length != srcCount)
    { Console.WriteLine($"FAIL: Deparse round-trip count {srcCount} -> {reparsed.Charts[5].NoteTimings.Length}"); f++; }
    Console.WriteLine($"checkMemoryPack: timings={srcCount} roundTrip={rtCount} rtSensor={rtSensor}");
    return f;
}
