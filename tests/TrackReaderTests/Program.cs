using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.Models.TrackUtils;
using System;
using System.IO;

// 多格式音频读取测试：
// 1) TrackFile.Find 的优先级/大小写/退化扫描
// 2) BASS 原生格式（ogg/wav/mp3/flac）直接解码
// 3) 非原生格式（opus）→ ffmpeg 转码回退（本环境无 ffmpeg，验证友好报错路径）
var failures = 0;
var tmp = Path.Combine(Path.GetTempPath(), "majdata_track_test_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmp);

try
{
    // ---- TrackFile.Find ----
    var d1 = Path.Combine(tmp, "find1");
    Directory.CreateDirectory(d1);
    File.WriteAllText(Path.Combine(d1, "track.mp3"), "fake");
    File.WriteAllText(Path.Combine(d1, "track.opus"), "fake");
    var f1 = TrackFile.Find(d1);
    Console.WriteLine($"find priority: {Path.GetFileName(f1 ?? "NULL")} (expect track.mp3)");
    if (f1 is null || Path.GetFileName(f1) != "track.mp3") { Console.WriteLine("FAIL priority"); failures++; }

    var d2 = Path.Combine(tmp, "find2");
    Directory.CreateDirectory(d2);
    File.WriteAllText(Path.Combine(d2, "track.OGG"), "fake");   // 大写兜底
    var f2 = TrackFile.Find(d2);
    Console.WriteLine($"find case: {Path.GetFileName(f2 ?? "NULL")} (expect track.OGG)");
    if (f2 is null || !f2.EndsWith("track.OGG", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("FAIL case"); failures++; }

    var d3 = Path.Combine(tmp, "find3");
    Directory.CreateDirectory(d3);
    File.WriteAllText(Path.Combine(d3, "song.opus"), "fake");   // 任意音频文件兜底
    File.WriteAllText(Path.Combine(d3, "bg.jpg"), "fake");
    var f3 = TrackFile.Find(d3);
    Console.WriteLine($"find fallback: {Path.GetFileName(f3 ?? "NULL")} (expect song.opus)");
    if (f3 is null || Path.GetFileName(f3) != "song.opus") { Console.WriteLine("FAIL fallback"); failures++; }

    // ---- 真实解码：mp3（analysis\2\track.mp3）----
    var mp3 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "analysis", "2", "track.mp3"));
    if (!File.Exists(mp3)) { Console.WriteLine($"SKIP mp3: missing {mp3}"); }
    else
    {
        var dir = Path.Combine(tmp, "mp3"); Directory.CreateDirectory(dir);
        File.Copy(mp3, Path.Combine(dir, "track.mp3"), true);
        using var r = new TrackReader();
        var info = r.ReadTrack(dir);
        Console.WriteLine($"mp3: len={info.Length:F2}s samples={info.RawWave.Length} resolved={Path.GetFileName(r.ResolvedTrackPath ?? "NULL")}");
        if (info.Length <= 0 || info.RawWave.Length == 0) { Console.WriteLine("FAIL mp3 decode"); failures++; }
    }

    // ---- 真实解码：wav（MajdataX\SFX 下的 wav）----
    var wav = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "MajdataX", "SFX"));
    string? wavFile = null;
    if (Directory.Exists(wav))
    {
        foreach (var f in Directory.EnumerateFiles(wav, "*.wav"))
        {
            if (new FileInfo(f).Length > 1024) { wavFile = f; break; }
        }
    }
    if (wavFile is null)
    {
        Console.WriteLine("SKIP wav: no sample wav found, generating PCM wav");
        wavFile = GenerateWav(Path.Combine(tmp, "gen.wav"), 44100, 2.0);
    }
    {
        var dir = Path.Combine(tmp, "wav"); Directory.CreateDirectory(dir);
        File.Copy(wavFile, Path.Combine(dir, "track.wav"), true);
        using var r = new TrackReader();
        var info = r.ReadTrack(dir);
        Console.WriteLine($"wav: len={info.Length:F2}s samples={info.RawWave.Length} resolved={Path.GetFileName(r.ResolvedTrackPath ?? "NULL")}");
        if (info.Length <= 0 || info.RawWave.Length == 0) { Console.WriteLine("FAIL wav decode"); failures++; }
    }

    // ---- 非原生格式 opus：BASS 无法解码 → ffmpeg 转码回退（本环境无 ffmpeg，验证友好报错路径）----
    {
        var dir = Path.Combine(tmp, "opus"); Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "track.opus"), new byte[4096]); // 无法识别的音频内容
        using var r = new TrackReader();
        try
        {
            var info = r.ReadTrack(dir);
            Console.WriteLine($"opus: len={info.Length:F2}s (ffmpeg 转码成功) resolved={Path.GetFileName(r.ResolvedTrackPath ?? "NULL")}");
            if (info.Length <= 0 || info.RawWave.Length == 0) { Console.WriteLine("FAIL opus"); failures++; }
        }
        catch (Exception e)
        {
            Console.WriteLine($"opus: 回退报错（无 ffmpeg 环境，预期行为）: {e.Message.Split('\n')[0]}");
            if (!e.Message.Contains("ffmpeg")) { Console.WriteLine("FAIL opus error message"); failures++; }
        }
    }
}
finally
{
    try { Directory.Delete(tmp, true); } catch { }
}

Console.WriteLine(failures == 0 ? "TRACK-OK" : $"TRACK-FAILURES={failures}");
return failures == 0 ? 0 : 1;

static string GenerateWav(string path, int sampleRate, double seconds)
{
    var count = (int)(sampleRate * seconds);
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);
    void Str(string s) { foreach (var c in s) bw.Write((byte)c); }
    bw.Write(0); Str("RIFF"); bw.Write(36 + count * 2); Str("WAVEfmt ");
    bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(sampleRate);
    bw.Write(sampleRate * 2); bw.Write((short)2); bw.Write((short)16);
    Str("data"); bw.Write(count * 2);
    for (var i = 0; i < count; i++)
        bw.Write((short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 12000));
    return path;
}
