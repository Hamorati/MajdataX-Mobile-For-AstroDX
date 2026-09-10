#nullable enable

using System;
using UnityEngine;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 系统文件夹选择器桥（SAF ACTION_OPEN_DOCUMENT_TREE）：选择含 maidata.txt 的谱面文件夹根目录，
    /// 把其下每个谱面子目录整体复制进 ChartsRoot，解决 Android 11+ 分区存储下无法直接访问应用目录的问题。
    /// </summary>
    public static class MobileChartPicker
    {
        private static AndroidJavaClass? _bridge;

        /// <summary>是否处于导入中（发起后应每帧轮询 <see cref="TryGetResult"/>）。</summary>
        public static bool Picking { get; private set; }

        /// <summary>是否处于导出中（发起后应每帧轮询 <see cref="TryGetExportResult"/>）。</summary>
        public static bool Exporting { get; private set; }

        /// <summary>发起系统文件夹选择用于导出 zip（ACTION_OPEN_DOCUMENT_TREE；zipPath 为应用内部临时 zip）。</summary>
        public static bool StartExportPick(string zipPath)
        {
#if UNITY_ANDROID
            try
            {
                if (_bridge is null)
                    _bridge = new AndroidJavaClass("com.majdata.mobilepicker.MajdataActivity");
                _bridge.CallStatic("PickExportFolder", zipPath);
                Exporting = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MobileChartPicker.StartExportPick failed: {ex}");
                Exporting = false;
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 轮询导出结果。返回 (已完成, 错误)。未完成时返回 (false, "")。
        /// </summary>
        public static (bool done, string error) TryGetExportResult()
        {
#if UNITY_ANDROID
            if (!Exporting || _bridge is null) return (false, string.Empty);
            try
            {
                var raw = _bridge.CallStatic<string>("QueryExportResult");
                if (string.IsNullOrEmpty(raw)) return (false, string.Empty);
                Exporting = false;
                if (raw == "OK") return (true, string.Empty);
                return (true, raw.StartsWith("ERR|", StringComparison.Ordinal) ? raw.Substring(4) : raw);
            }
            catch (Exception ex)
            {
                Exporting = false;
                return (true, "picker error: " + ex.Message);
            }
#else
            return (true, "not supported on this platform");
#endif
        }

        /// <summary>发起系统文件夹选择。destRoot 为 Charts 目录。仅 Android 有效。</summary>
        public static bool StartPick(string destRoot)
        {
#if UNITY_ANDROID
            try
            {
                if (_bridge is null)
                    _bridge = new AndroidJavaClass("com.majdata.mobilepicker.MajdataActivity");
                _bridge.CallStatic("PickChartFolder", destRoot);
                Picking = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MobileChartPicker.StartPick failed: {ex}");
                Picking = false;
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 轮询导入结果。返回 (已完成, 导入数量, 错误)。未完成时返回 (false, 0, "")。
        /// </summary>
        public static (bool done, int count, string error) TryGetResult()
        {
#if UNITY_ANDROID
            if (!Picking || _bridge is null) return (false, 0, string.Empty);
            try
            {
                var raw = _bridge.CallStatic<string>("QueryChartResult");
                if (string.IsNullOrEmpty(raw)) return (false, 0, string.Empty);
                Picking = false;
                var sep = raw.IndexOf('|');
                var countStr = sep >= 0 ? raw.Substring(0, sep) : string.Empty;
                var error = sep >= 0 ? raw.Substring(sep + 1) : string.Empty;
                var count = 0;
                if (error.Length == 0) int.TryParse(countStr, out count);
                return (true, count, error);
            }
            catch (Exception ex)
            {
                Picking = false;
                return (true, 0, "picker error: " + ex.Message);
            }
#else
            return (true, 0, "not supported on this platform");
#endif
        }
    }
}
