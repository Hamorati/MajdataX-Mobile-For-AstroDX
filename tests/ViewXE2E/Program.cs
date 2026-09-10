using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading;
using MajSimai;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.MajSetting;
using MajdataViewX.Types.MajWs;
using MajdataViewX.Types.Rendering;
using MemoryPack;
using WebSocketSharp;

// 端到端运行验证：启动部署版 MajdataViewX，走 WS 协议加载并播放一份传感区 slide 谱面，
// 截图留证 + 检查 ViewX 运行日志是否有加载异常。
var root = args.Length > 0 ? args[0] : @"D:\Workspace";
var chartName = args.Length > 1 ? args[1] : "2.zip";
var chartIdx = args.Length > 2 ? int.Parse(args[2]) : 5;
var exportMode = args.Length > 3 && args[3] == "export";
var exportStartAt = args.Length > 4 ? double.Parse(args[4]) : 150;
var recordSeconds = args.Length > 5 && exportMode ? int.Parse(args[5]) : 26;
var playStartAt = args.Length > 4 && args[3] != "export" ? double.Parse(args[4]) : 0;
var shotTimes = args.Length > 5 && args[3] != "export"
    ? args[5].Split(',').Select(double.Parse).ToArray()
    : new double[] { 3, 12, 22 };
var failures = 0;

var majdataDir = Path.Combine(root, "MajdataX");
var viewxExe = Path.Combine(majdataDir, "MajdataViewX.exe");
var chartDir = Path.Combine(root, "analysis", chartName.Replace(".zip", ""));
var maidataPath = Path.Combine(chartDir, "maidata.txt");
var trackPath = Path.Combine(chartDir, "track.mp3");
if (!File.Exists(trackPath)) trackPath = Path.Combine(chartDir, "track.ogg");
var imagePath = Path.Combine(chartDir, "bg.jpg");
var localLowDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow");
var persistentDir = Path.Combine(localLowDir, "bbben", "MajdataViewX");
var logPath = Path.Combine(persistentDir, "Player.log");
var shotPath = Path.Combine(root, "analysis", $"e2e-{chartName.Replace(".zip", "")}.png");

Console.WriteLine($"E2E: {chartName} chart={chartIdx} track={trackPath}");

// 1) 解析谱面并写入共享内存
var content = File.ReadAllText(maidataPath);
var file = SimaiParser.ParseAsync(content, "").GetAwaiter().GetResult();
var chart = file.Charts[chartIdx];
if (chart.IsEmpty) { Console.WriteLine("FAIL: chart empty"); return 1; }
var fileBytes = MemoryPackSerializer.Serialize(file);
var chartBytes = MemoryPackSerializer.Serialize(chart);
Console.WriteLine($"serialized: file={fileBytes.Length} chart={chartBytes.Length}");

Directory.CreateDirectory(persistentDir);
var mmfPath = Path.Combine(persistentDir, "majdata_chart.dat");
using (var mmfStream = new FileStream(mmfPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
{
    if (mmfStream.Length < 64 * 1024 * 1024) mmfStream.SetLength(64 * 1024 * 1024);
    using var mmf = MemoryMappedFile.CreateFromFile(mmfStream, null, 64 * 1024 * 1024, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
    using var accessor = mmf.CreateViewAccessor();
    accessor.WriteArray(0, fileBytes, 0, fileBytes.Length);
    accessor.WriteArray(fileBytes.Length, chartBytes, 0, chartBytes.Length);
}
Console.WriteLine("MMF written");

// 2) 启动 ViewX
if (File.Exists(logPath)) File.Delete(logPath);
var psi = new ProcessStartInfo(viewxExe) { WorkingDirectory = majdataDir, UseShellExecute = false };
using var proc = Process.Start(psi)!;
Console.WriteLine($"ViewX started pid={proc.Id}");

WebSocket? ws = null;
var responses = new List<MajWsResponse>();
try
{
    // 3) 连接 WS
    ws = new WebSocket("ws://127.0.0.1:8083/majdata") { WaitTime = TimeSpan.FromSeconds(3) };
    var loadOk = new ManualResetEventSlim(false);
    ws.OnMessage += (s, e) =>
    {
        try
        {
            var resp = MemoryPackSerializer.Deserialize<MajWsResponse>(e.RawData);
            if (resp != null) { responses.Add(resp); if (resp.ResponseType == MajWsResponseType.LoadOk) loadOk.Set(); }
        }
        catch { }
    };
    var connected = false;
    for (var i = 0; i < 30 && !connected; i++)
    {
        try { ws.Connect(); connected = ws.IsAlive; } catch { }
        if (!connected) Thread.Sleep(1000);
    }
    if (!connected) { Console.WriteLine("FAIL: WS connect"); failures++; }
    else
    {
        Console.WriteLine("WS connected");
        ws.Send(MemoryPackSerializer.Serialize<MajWsRequest>(new MajWsLoadRequest
        {
            TrackPath = trackPath,
            ImagePath = imagePath,
            VideoPath = string.Empty
        }));
        if (!loadOk.Wait(TimeSpan.FromSeconds(30))) { Console.WriteLine("FAIL: LoadOk timeout"); failures++; }
        else
        {
            Console.WriteLine("LoadOk");
            if (exportMode)
            {
                // 导出验证：Setting(30fps/Medium) → Update → Record(从尾部 StartAt 录制)
                ws.Send(MemoryPackSerializer.Serialize<MajWsRequest>(new MajWsSettingRequest
                {
                    ViewSetting = new MajViewSetting { OutputFps = 30, ExportQuality = ExportQuality.Medium },
                    VolumeSetting = new MajVolumeSetting()
                }));
                Thread.Sleep(800);
                ws.Send(MemoryPackSerializer.Serialize<MajWsRequest>(new MajWsUpdateRequest
                {
                    FileLength = fileBytes.Length,
                    ChartLength = chartBytes.Length,
                    SelectedDifficulty = chartIdx
                }));
                Thread.Sleep(1500);
                var exportDir = Path.Combine(Path.GetTempPath(), "majdata-export");
                Directory.CreateDirectory(exportDir);
                ws.Send(MemoryPackSerializer.Serialize<MajWsRequest>(new MajWsPlayRequest
                {
                    Mode = PlaybackMode.Record,
                    StartAt = exportStartAt,
                    Speed = 1f,
                    MaidataPath = exportDir
                }));
                Console.WriteLine($"Record sent (StartAt={exportStartAt}s), recording {recordSeconds}s...");
                Thread.Sleep(recordSeconds * 1000);
                ws.Send(MemoryPackSerializer.Serialize<MajWsRequest>(new MajWsStopRequest()));
                Thread.Sleep(4000);
                var outMp4 = Path.Combine(exportDir, "out.mp4");
                if (File.Exists(outMp4))
                {
                    var info = new FileInfo(outMp4);
                    var head = new byte[12];
                    using (var fs = File.OpenRead(outMp4)) fs.Read(head, 0, 12);
                    var isMp4 = System.Text.Encoding.ASCII.GetString(head, 4, 4) == "ftyp";
                    Console.WriteLine($"out.mp4: size={info.Length} ftyp={isMp4}");
                    if (info.Length < 100_000 || !isMp4) { Console.WriteLine("FAIL: export output invalid"); failures++; }
                    var exportShot = Path.Combine(root, "analysis", $"e2e-export-{chartName.Replace(".zip", "")}.png");
                    try
                    {
                        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                        using var bmp = new Bitmap(bounds.Width, bounds.Height);
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                        bmp.Save(exportShot, ImageFormat.Png);
                        Console.WriteLine($"export screenshot saved: {exportShot}");
                    }
                    catch { }
                }
                else
                {
                    Console.WriteLine("FAIL: out.mp4 not produced");
                    failures++;
                }
            }
            else
            {
                ws.Send(MemoryPackSerializer.Serialize<MajWsRequest>(new MajWsUpdateRequest
                {
                    FileLength = fileBytes.Length,
                    ChartLength = chartBytes.Length,
                    SelectedDifficulty = chartIdx
                }));
                Thread.Sleep(1500);
                ws.Send(MemoryPackSerializer.Serialize<MajWsRequest>(new MajWsPlayRequest
                {
                    Mode = PlaybackMode.Normal,
                    StartAt = playStartAt,
                    Speed = 1f
                }));
                Console.WriteLine($"Play sent (StartAt={playStartAt}s)...");
                var elapsed = 0;
                foreach (var t in shotTimes)
                {
                    var waitMs = (int)Math.Max(0, t * 1000 - elapsed);
                    Thread.Sleep(waitMs);
                    elapsed += waitMs;
                    try
                    {
                        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                        using var bmp = new Bitmap(bounds.Width, bounds.Height);
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                        var p = shotPath.Replace(".png", $"-{t.ToString("0.#")}s.png");
                        bmp.Save(p, ImageFormat.Png);
                        Console.WriteLine($"screenshot saved: {p}");
                    }
                    catch (Exception ex) { Console.WriteLine($"screenshot failed: {ex.Message}"); }
                }
                ws.Send(MemoryPackSerializer.Serialize<MajWsRequest>(new MajWsStopRequest()));
                Thread.Sleep(1500);
            }
        }
    }
}
finally
{
    try { ws?.Close(); } catch { }
    if (!proc.HasExited) { try { proc.Kill(); } catch { } }
    Thread.Sleep(1000);
}

// 4) 检查 ViewX 日志
foreach (var r in responses.Take(20))
    Console.WriteLine($"  RESP: {r.ResponseType} state={r.Summary.State} err={r.Error}");
if (File.Exists(logPath))
{
    var log = File.ReadAllText(logPath);
    var bad = new List<string>();
    foreach (var line in log.Split('\n'))
    {
        if (line.Contains("[Note Load Failed]") || line.Contains("NullReferenceException") ||
            line.Contains("IndexOutOfRangeException") || line.Contains("KeyNotFoundException"))
            bad.Add(line.Trim());
    }
    Console.WriteLine($"log lines={log.Split('\n').Length} suspicious={bad.Count}");
    foreach (var b in bad.Take(10)) Console.WriteLine("  LOG: " + b);
    if (bad.Count > 0) failures++;
}
else
{
    Console.WriteLine("FAIL: no Player.log");
    failures++;
}

Console.WriteLine(failures == 0 ? "E2E-OK" : $"E2E-FAILURES={failures}");
return failures == 0 ? 0 : 1;
