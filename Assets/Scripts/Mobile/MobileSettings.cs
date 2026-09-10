#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// APK 本地设置（仅移动端生效，不写入谱面）：判定音自定义、tap/touch 流速、全局声音偏移。
    /// 持久化于 persistentDataPath/settings.json（JsonUtility，不支持字典，用条目列表）。
    /// </summary>
    [Serializable]
    public class MobileSettings
    {
        public static MobileSettings Instance { get; private set; } = Load();

        private static string SettingsPath => Path.Combine(Application.persistentDataPath, "settings.json");

        public float TapSpeed = 7.5f;
        public float TouchSpeed = 7.5f;
        public double GlobalOffsetMs = 0d;
        /// <summary>编辑器背景图文件名（CustomBG 目录内，空 = 默认深色）。</summary>
        public string BackgroundFile = string.Empty;
        /// <summary>背景暗化程度（0~0.85，保护文字可读性）。</summary>
        public float BackgroundDim = 0.4f;
        /// <summary>时间轴缩放档位（历史遗留；现固定为 3=自定义窗口，旧档位在加载时自动迁移）。</summary>
        public int TimelineZoom = 3;
        /// <summary>自定义窗口秒数（0.25~8，始终生效）。</summary>
        public float TimelineWindowSec = 8f;
        // ---- 判定音音量（与原生 MajdataEdit-Neo 的 9 类一致：0~100%，默认 90%）----
        public float TrackVol = 90f;      // 音乐
        public float AnswerVol = 90f;     // 应答
        public float TapVol = 90f;
        public float SlideVol = 90f;
        public float BreakVol = 90f;
        public float BreakSlideVol = 90f;
        public float ExVol = 90f;
        public float TouchVol = 90f;
        public float HanabiVol = 90f;     // 花火

        public static MobileSettings Load()
        {
            MobileSettings settings;
            try
            {
                if (File.Exists(SettingsPath))
                    settings = JsonUtility.FromJson<MobileSettings>(File.ReadAllText(SettingsPath)) ?? new MobileSettings();
                else
                    settings = new MobileSettings();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MobileSettings] load failed: {ex.Message}");
                settings = new MobileSettings();
            }
            // 时间轴已取消三档预设，旧档位（0/1/2）迁移为自定义 8 秒窗口
            if (settings.TimelineZoom != 3)
            {
                settings.TimelineZoom = 3;
                settings.TimelineWindowSec = 8f;
            }
            settings.TimelineWindowSec = Mathf.Clamp(settings.TimelineWindowSec, 0.25f, 8f);
            return settings;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(this, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MobileSettings] save failed: {ex.Message}");
            }
        }
    }
}
