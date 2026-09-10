#nullable enable

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 移动端编辑器视觉规范（深色极简扁平）：统一颜色令牌 + 程序生成的圆角/光晕贴图。
    /// 仅视觉层：不改动任何布局坐标与交互逻辑。
    /// </summary>
    public static class UiSkin
    {
        // ---- 颜色令牌 ----
        public static readonly Color PanelBg = new Color(0.098f, 0.106f, 0.125f, 0.98f);      // 面板底色 #191B20
        public static readonly Color InputBg = new Color(0.129f, 0.137f, 0.161f, 1f);         // 输入框底 #212329
        public static readonly Color ButtonBg = new Color(0.161f, 0.169f, 0.200f, 1f);        // 按钮底 #292B33
        public static readonly Color ButtonHover = new Color(0.216f, 0.224f, 0.259f, 1f);     // 悬停 #373942
        public static readonly Color ButtonPressed = new Color(0.129f, 0.137f, 0.161f, 1f);   // 按压 #212329
        public static readonly Color ButtonDisabled = new Color(0.30f, 0.30f, 0.30f, 0.5f);
        public static readonly Color Accent = new Color(0.302f, 0.624f, 0.624f, 1f);          // 低饱和青 #4D9F9F
        public static readonly Color AccentDim = new Color(0.302f, 0.624f, 0.624f, 0.42f);    // 选中底色
        public static readonly Color AccentText = new Color(0.627f, 0.847f, 0.847f, 1f);      // 亮青文字 #A0D8D8
        public static readonly Color Danger = new Color(0.435f, 0.169f, 0.169f, 1f);          // 危险按钮 #6F2B2B
        public static readonly Color DangerStrong = new Color(0.545f, 0.196f, 0.196f, 1f);    // 确认删除 #8B3232
        public static readonly Color TextPrimary = new Color(0.902f, 0.914f, 0.945f, 1f);     // #E6E9F1
        public static readonly Color TextSecondary = new Color(0.612f, 0.643f, 0.698f, 1f);   // #9CA4B2
        public static readonly Color TrackBg = new Color(0.145f, 0.153f, 0.180f, 1f);         // 滑条轨道 #25272E
        public static readonly Color Glow = new Color(0.353f, 0.780f, 0.780f, 1f);            // 按压发光 #5AC7C7

        /// <summary>圆角矩形（9-slice，白色，颜色由 Image.color 决定）。</summary>
        public static Sprite? Rounded { get; private set; }

        /// <summary>软边光晕（边缘渐变透明，用于按钮按压发光）。</summary>
        public static Sprite? GlowSprite { get; private set; }

        /// <summary>首次使用时生成贴图（幂等）。</summary>
        public static void EnsureCreated()
        {
            if (Rounded is not null) return;
            Rounded = BuildRounded(64, 18, false);
            GlowSprite = BuildRounded(64, 18, true);
        }

        /// <summary>生成白色圆角矩形贴图；soft=true 时边缘线性渐隐（光晕）。</summary>
        private static Sprite BuildRounded(int size, int radius, bool soft)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = soft ? "UiGlow" : "UiRounded";
            tex.wrapMode = TextureWrapMode.Clamp;
            var last = size - 1;
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Max(Mathf.Max(radius - x, x - (last - radius)), 0);
                    var dy = Mathf.Max(Mathf.Max(radius - y, y - (last - radius)), 0);
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = soft
                        ? Mathf.Clamp01(1f - d / radius)
                        : Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var border = new Vector4(radius + 1f, radius + 1f, radius + 1f, radius + 1f);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
        }

        /// <summary>给按钮挂按压发光子层（按下快亮、松开渐隐；不改变交互）。</summary>
        public static void AttachPressGlow(Button button)
        {
            if (GlowSprite is null) return;
            var glowGo = new GameObject("PressGlow", typeof(RectTransform), typeof(Image));
            var t = (RectTransform)glowGo.transform;
            t.SetParent(button.transform, false);
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.offsetMin = new Vector2(-10f, -10f);
            t.offsetMax = new Vector2(10f, 10f);
            var img = glowGo.GetComponent<Image>();
            img.sprite = GlowSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(Glow.r, Glow.g, Glow.b, 0f);
            img.raycastTarget = false;
            glowGo.transform.SetAsFirstSibling(); // 辉光渲染在按钮底之上、标签文字之下
            // 组件必须挂在按钮本体：uGUI 指针事件沿父链上行，子物体收不到 OnPointerDown/Up
            var glow = button.gameObject.AddComponent<ButtonPressGlow>();
            glow._button = button;
            glow._glow = img;
        }

        /// <summary>选中态：低饱和青实底（难度/播放速度等被选中的档位按钮）。</summary>
        public static void ApplyAccent(Button button)
        {
            var c = button.colors;
            c.normalColor = Accent;
            c.highlightedColor = Accent;
            c.pressedColor = new Color(Accent.r * 0.75f, Accent.g * 0.75f, Accent.b * 0.75f, 1f);
            c.selectedColor = Accent;
            c.disabledColor = ButtonDisabled;
            button.colors = c;
        }

        /// <summary>危险按钮：低饱和红实底（strong=确认删除，更醒目）。</summary>
        public static void ApplyDanger(Button button, bool strong = false)
        {
            var baseCol = strong ? DangerStrong : Danger;
            var c = button.colors;
            c.normalColor = baseCol;
            c.highlightedColor = baseCol;
            c.pressedColor = new Color(baseCol.r * 0.75f, baseCol.g * 0.75f, baseCol.b * 0.75f, 1f);
            c.selectedColor = baseCol;
            c.disabledColor = ButtonDisabled;
            button.colors = c;
        }
    }

    /// <summary>按压发光：IPointerDown 快亮、松开慢熄。</summary>
    public sealed class ButtonPressGlow : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        internal Button? _button;
        internal Image? _glow;
        private float _alpha;
        private float _target;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_button is not null && !_button.interactable) return;
            _target = 0.85f;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _target = 0f;
        }

        private void Update()
        {
            if (_glow is null) return;
            var speed = _target > _alpha ? 14f : 18f; // 按下快亮；松手极速淡出（≈0.05 秒）
            _alpha = Mathf.MoveTowards(_alpha, _target, Time.unscaledDeltaTime * speed);
            var c = _glow.color;
            c.a = _alpha;
            _glow.color = c;
        }
    }
}
