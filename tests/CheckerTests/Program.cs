using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MajdataEdit_Neo.Models.SimaiAnalyzer;
using MajdataEdit_Neo.Types.SimaiAnalyzer;

var root = args.Length > 0 ? args[0] : @"D:\Workspace";
var failures = 0;

failures += CheckFile(root, "1.zip", ExtractInote(ReadMaidata(root, "1.zip"), 5));
failures += CheckFile(root, "2.zip", ExtractInote(ReadMaidata(root, "2.zip"), 6));
failures += CheckFile(root, "3.zip", ExtractInote(ReadMaidata(root, "3.zip"), 8));
failures += CheckFile(root, "4.zip", ExtractInote(ReadMaidata(root, "4.zip"), 8));
failures += CheckFile(root, "5.zip", ExtractInote(ReadMaidata(root, "5.zip"), 8));

// 关键 token 单测（含 4.zip 中 hold+slide、伪 Each 等组合）
var tokens = new[]
{
    "6-B6-B8-B2-2[8:1]",
    "E3-E5-D7-D1>4b[4:11]",
    "D2-C-D4^B8[4:5]*-C-D8^B4[4:5]*-D6m[8:5]",
    "8x>E7-C[16:27]",
    "4x<E3-Cb[128:215]",
    "Cmf-B8^E5-A5^2[4:13]",
    "1b-B2-B6-5<8[2:1]*-B8-B4-5>2[2:1]",
    "1-B5<B1-5[8:3]*-B5>B1-5[8:3]",
    "8h[1:1]<4[62#2:1]",
    "7b`3?>7[4:1]m*<7[4:1]m",
    "5bx<5b[4:3]*>5b[4:3]",
    "D4<A3b[2:1]",
    "B6>D1v3[8:5]b`B2<D6v7[8:5]",
    "4/7/5mw1b[8:1]",
    // 5.zip 用例：相邻直线链、触区锚定同起点链、空 EACH 侧
    "2bx-1-8-7-6[1:1]b*<1<8<7<6[1:1]b",
    "7bx-6-5-4-3[1:1]b*<6>5>4>3[1:1]b",
    "D6bpp3[4:3]*pp3[4:3]",
    "/4",
};
foreach (var token in tokens)
{
    var errors = SimaiChecker.Check(token).Where(d => d.Severity == Severity.Error).ToList();
    if (errors.Count != 0)
    {
        failures++;
        Console.WriteLine($"FAIL token '{token}':");
        foreach (var e in errors) Console.WriteLine($"    {e.Message}");
    }
    else
    {
        Console.WriteLine($"ok token '{token}'");
    }
}

Console.WriteLine(failures == 0 ? "CHECKER-OK" : $"CHECKER-FAILURES={failures}");
return failures == 0 ? 0 : 1;

static string ReadMaidata(string root, string name) =>
    File.ReadAllText(Path.Combine(root, "analysis", name.Replace(".zip", ""), "maidata.txt"));

static string ExtractInote(string maidata, int index)
{
    var prefix = $"&inote_{index}=";
    var lines = maidata.Split('\n');
    var start = -1;
    for (var i = 0; i < lines.Length; i++)
    {
        if (!lines[i].TrimStart().StartsWith(prefix)) continue;
        start = i;
        break;
    }
    if (start < 0) return string.Empty;
    var result = new List<string>();
    var first = lines[start].Substring(lines[start].IndexOf('=') + 1);
    if (!string.IsNullOrWhiteSpace(first)) result.Add(first);
    for (var i = start + 1; i < lines.Length; i++)
    {
        var l = lines[i].Trim();
        if (l.StartsWith("&")) break;
        result.Add(l);
    }
    return string.Join('\n', result);
}

static int CheckFile(string root, string name, string fumen)
{
    if (string.IsNullOrEmpty(fumen))
    {
        Console.WriteLine($"FAIL {name}: fumen empty");
        return 1;
    }
    var errors = SimaiChecker.Check(fumen).Where(d => d.Severity == Severity.Error).ToList();
    var warnings = SimaiChecker.Check(fumen).Count(d => d.Severity == Severity.Warning);
    if (errors.Count != 0)
    {
        Console.WriteLine($"FAIL {name}: {errors.Count} errors (warnings={warnings})");
        foreach (var e in errors.Take(12))
            Console.WriteLine($"    L{e.PositionStart.Line} '{e.Message}' | {e.Detail}");
        return 1;
    }
    Console.WriteLine($"ok {name}: 0 errors, warnings={warnings}");
    return 0;
}
