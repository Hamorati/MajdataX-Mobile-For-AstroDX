using System.IO;
using UnityEngine;

namespace MajdataViewX.Base
{
    public static class MajEnv
    {
#if UNITY_EDITOR
        // 编辑器下，指向项目根目录（Assets 的上一级）
        public static string MajBase = new DirectoryInfo(Application.dataPath).Parent!.FullName;
#elif UNITY_ANDROID
        // Android：Skin/SFX 等运行资源在首次启动时从 StreamingAssets 解压到持久目录
        public static string MajBase => Application.persistentDataPath;
#else
        // 打包后，Application.dataPath 的上一级在 Windows 下是 exe 目录
        // 但为了兼顾 Mac 等平台，用 AppContext 或者是 dataPath 的物理同级更安全
        public static string MajBase => System.AppDomain.CurrentDomain.BaseDirectory;
#endif

        public static string GetPath(string relativePath) =>
            Path.Combine(MajBase, relativePath);

        public static string MmfAudioTimePath =>
            Path.Combine(Application.persistentDataPath, "majdata_time.dat");
        public const long MmfChartDataCapacity = 64 * 1024 * 1024; //64mb
        public static string MmfChartDataPath =>
            Path.Combine(Application.persistentDataPath, "majdata_chart.dat");
    }
}