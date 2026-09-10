using UnityEngine;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 移动端分屏布局常量：上半屏 = 渲染器，下半屏 = 编辑器。
    /// </summary>
    public static class MobileLayout
    {
        /// <summary>渲染器占屏幕高度的比例（上半部分）。</summary>
        public const float GameAreaFraction = 0.5f;

        /// <summary>编辑器区域是否激活（激活时下半屏触摸不进入游戏判定）。</summary>
        public static bool InputGated;

        /// <summary>游戏区域底部在屏幕坐标中的 Y（小于该值的触摸属于编辑器区域）。</summary>
        public static float GameAreaBottomPixels => Screen.height * GameAreaFraction;
    }
}
