#nullable enable

using System;
using UnityEngine;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 系统音频选择器桥（SAF ACTION_OPEN_DOCUMENT，audio/*）。
    /// 插件 Activity：com.majdata.mobilepicker.MajdataActivity（继承 UnityPlayerActivity）。
    /// 选中文件被复制到目标目录下的 .picked&lt;ext&gt;，由调用方改名为 track.&lt;ext&gt;。
    /// </summary>
    public static class MobileAudioPicker
    {
        private static AndroidJavaClass? _bridge;

        /// <summary>是否处于选择中（发起后应每帧轮询 <see cref="TryGetResult"/>）。</summary>
        public static bool Picking { get; private set; }

        /// <summary>发起系统音频选择。targetDir 必须已存在。仅 Android 有效。</summary>
        public static bool StartPick(string targetDir)
        {
#if UNITY_ANDROID
            try
            {
                if (_bridge is null)
                    _bridge = new AndroidJavaClass("com.majdata.mobilepicker.MajdataActivity");
                _bridge.CallStatic("PickAudio", targetDir);
                Picking = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MobileAudioPicker.StartPick failed: {ex}");
                Picking = false;
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>发起系统图片选择（编辑器背景图）。targetDir 必须已存在。仅 Android 有效。</summary>
        public static bool StartPickImage(string targetDir)
        {
#if UNITY_ANDROID
            try
            {
                if (_bridge is null)
                    _bridge = new AndroidJavaClass("com.majdata.mobilepicker.MajdataActivity");
                _bridge.CallStatic("PickImage", targetDir);
                Picking = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"MobileAudioPicker.StartPickImage failed: {ex}");
                Picking = false;
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 轮询选择结果。返回 (已完成, 文件路径, 错误)。未完成时返回 (false, "", "")。
        /// </summary>
        public static (bool done, string path, string error) TryGetResult()
        {
#if UNITY_ANDROID
            if (!Picking || _bridge is null) return (false, string.Empty, string.Empty);
            try
            {
                var raw = _bridge.CallStatic<string>("QueryResult");
                if (string.IsNullOrEmpty(raw)) return (false, string.Empty, string.Empty);
                Picking = false;
                var sep = raw.IndexOf('|');
                var path = sep >= 0 ? raw.Substring(0, sep) : string.Empty;
                var error = sep >= 0 ? raw.Substring(sep + 1) : string.Empty;
                return (true, path, error);
            }
            catch (Exception ex)
            {
                Picking = false;
                return (true, string.Empty, "picker error: " + ex.Message);
            }
#else
            return (true, string.Empty, "not supported on this platform");
#endif
        }
    }
}
