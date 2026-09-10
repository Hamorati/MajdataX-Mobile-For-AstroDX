using System;
using System.IO;
using System.Linq;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 移动端谱面目录的音频查找（桌面版编辑器已有同语义实现，移动端独立维护一份轻量版）。
    /// BASS 核心（Android）原生支持 mp3/ogg(Vorbis)/wav/flac/aiff。
    /// </summary>
    public static class MobileTrackFile
    {
        public static readonly string[] SupportedExtensions =
            new[] { ".mp3", ".ogg", ".wav", ".flac", ".aiff" };

        public static bool IsBassNative(string path) =>
            Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".ogg" or ".wav" or ".flac" or ".aiff";

        /// <summary>按优先级查找目录下的 track 音频（大小写不敏感），无 track.* 时退化为目录内任意受支持音频。</summary>
        public static string? Find(string dirpath)
        {
            if (!Directory.Exists(dirpath)) return null;

            foreach (var ext in SupportedExtensions)
            {
                var p = Path.Combine(dirpath, "track" + ext);
                if (File.Exists(p)) return p;
            }

            var lower = SupportedExtensions.Select(e => e.ToLowerInvariant()).ToArray();
            string? Candidate(string f)
            {
                var idx = Array.IndexOf(lower, Path.GetExtension(f).ToLowerInvariant());
                return idx < 0 ? null : f;
            }

            var exact = Directory.EnumerateFiles(dirpath, "track.*")
                .Select(Candidate)
                .Where(f => f is not null)
                .OrderBy(f => Array.IndexOf(lower, Path.GetExtension(f!).ToLowerInvariant()))
                .FirstOrDefault();
            if (exact is not null) return exact;

            return Directory.EnumerateFiles(dirpath)
                .Select(Candidate)
                .Where(f => f is not null)
                .OrderBy(f => Array.IndexOf(lower, Path.GetExtension(f!).ToLowerInvariant()))
                .FirstOrDefault();
        }
    }
}
