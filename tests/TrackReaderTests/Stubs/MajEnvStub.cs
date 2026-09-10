namespace MajdataEdit_Neo.Base;

using System;
using System.IO;

// 测试桩：TrackReader 使用部署版 ViewX 的 bass.dll（与编辑器行为一致）
public static class MajEnv
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static string MajdataViewBassDllFile =>
        Path.Combine(RepoRoot, "MajdataX", "MajdataViewX_Data", "Plugins", "x86_64", "bass.dll");
}
