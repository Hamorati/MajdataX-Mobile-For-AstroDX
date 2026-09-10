#nullable enable

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 移动端文本编辑手势（浏览滚动由右侧滚动条承担）：
    /// - 单击 = 放下光标到点击字符（不弹输入法、不聚焦）；
    /// - 单指拖动 = 字符级光标跟随手指（自绘光标；接近边缘由外部自动滚动）；
    /// - 长按 ≈0.5 秒后拖动 = 字符级文本选择（锚点=长按处字符）；
    /// - 纯长按（不拖动）= 选中整行；
    /// - 双击 = 输入法 + 光标定位到点击字符（此时原生光标/选区接管，自绘覆盖层隐藏）。
    /// 注意：除双击外不调用 ActivateInputField（避免模拟器上软键盘弹出）；
    /// 光标/选区视觉由外部自绘覆盖层渲染，内部 caretPosition/selection* 保持同步。
    /// </summary>
    public sealed class MobileInputField : InputField
    {
        /// <summary>屏幕坐标 → 字符索引（字符级光标/选区用）。</summary>
        public Func<Vector2, int>? CharIndexAt;

        /// <summary>屏幕坐标 → 行首字符索引（纯长按整行选择用）。</summary>
        public Func<Vector2, int>? LineStartCharAt;

        /// <summary>屏幕坐标 → 行尾字符索引（纯长按整行选择用）。</summary>
        public Func<Vector2, int>? LineEndCharAt;

        /// <summary>光标/选区变化（外部刷新自绘覆盖层，不滚动）。</summary>
        public Action? SelectionChanged;

        /// <summary>拖动中手指移动（外部处理边缘自动滚动）。</summary>
        public Action<Vector2>? DragMoved;

        /// <summary>输入法激活后光标落位完成（外部把光标行居中）。</summary>
        public Action? CaretPlaced;

        private bool _pointerDown;
        private float _downTime;
        private Vector2 _downPos;
        private int _downChar = -1;
        private bool _longPressed;
        private bool _dragging;
        private float _lastTapTime;
        private Vector2 _lastTapPos;
        private int _pendingCaret = -1;

        protected override void Awake()
        {
            base.Awake();
            shouldActivateOnSelect = false; // 选中 ≠ 输入法：输入法只由双击显式打开
        }

        /// <summary>双击：进入编辑态（输入法）并把光标放到 charIndex。</summary>
        public void FocusAtChar(int charIndex)
        {
            if (!isActiveAndEnabled) return;
            if (EventSystem.current is not null && EventSystem.current.currentSelectedGameObject != gameObject)
                EventSystem.current.SetSelectedGameObject(gameObject);
            if (isFocused)
            {
                caretPosition = charIndex;
                _pendingCaret = -1;
                ForceLabelUpdate();
                CaretPlaced?.Invoke();
            }
            else
            {
                _pendingCaret = charIndex;
                ActivateInputField();
            }
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (_pendingCaret < 0) return;
            if (!isFocused || m_Keyboard == null) return; // 等待激活完成（下一帧）
            caretPosition = _pendingCaret;
            _pendingCaret = -1;
            ForceLabelUpdate();
            CaretPlaced?.Invoke();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            _pointerDown = true;
            _longPressed = false;
            _dragging = false;
            _downTime = Time.unscaledTime;
            _downPos = eventData.position;
            _downChar = CharIndexAt?.Invoke(eventData.position) ?? -1;

            // 双击：输入法 + 光标到点击字符
            if (Time.unscaledTime - _lastTapTime < 0.35f &&
                (eventData.position - _lastTapPos).sqrMagnitude < 3600f)
            {
                _lastTapTime = 0f;
                _pointerDown = false; // 进入 IME 会话，不再跟踪本次按下
                FocusAtChar(_downChar >= 0 ? _downChar : 0);
                return;
            }
            _lastTapTime = Time.unscaledTime;
            _lastTapPos = eventData.position;

            // 单击/拖动/长按：不调用基类按下（不聚焦、不弹输入法）；先把光标放到按下字符
            if (_downChar >= 0)
            {
                caretPosition = _downChar;
                if (isFocused) ForceLabelUpdate();
                SelectionChanged?.Invoke();
            }
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            // 吞掉基类点击：单击不激活输入法（双击已在按下时处理）
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = true;
            // 不调用 base.OnBeginDrag：拖动手势由本类自行处理（光标移动/选区扩展）
            DragMoved?.Invoke(eventData.position);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            var ch = CharIndexAt?.Invoke(eventData.position) ?? -1;
            if (ch >= 0)
            {
                if (_longPressed)
                {
                    // 长按后拖动：选区扩展（锚点 = 长按处字符）
                    selectionAnchorPosition = _downChar >= 0 ? _downChar : ch;
                    selectionFocusPosition = ch;
                }
                else
                {
                    // 单指拖动：光标跟随手指
                    caretPosition = ch;
                }
                if (isFocused) ForceLabelUpdate();
                SelectionChanged?.Invoke();
            }
            DragMoved?.Invoke(eventData.position);
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            _pointerDown = false;
            var wasCaretDrag = _dragging && !_longPressed;
            _dragging = false;
            // 单指拖动光标松手 → 立即呼出输入法（选区拖动不呼出）
            if (wasCaretDrag)
            {
                // 已聚焦但软键盘可能已收起：先失活再激活，保证键盘确实弹出
                if (isFocused) DeactivateInputField();
                FocusAtChar(caretPosition);
            }
        }

        private void Update()
        {
            if (!_pointerDown || _longPressed || _dragging) return;
            if (Time.unscaledTime - _downTime < 0.5f) return;
            // 纯长按 → 选中整行
            _longPressed = true;
            var ls = LineStartCharAt?.Invoke(_downPos) ?? _downChar;
            var le = LineEndCharAt?.Invoke(_downPos) ?? _downChar;
            if (ls >= 0 && le >= 0)
            {
                selectionAnchorPosition = ls;
                selectionFocusPosition = le;
                if (isFocused) ForceLabelUpdate();
                SelectionChanged?.Invoke();
            }
        }

        /// <summary>结束选择/长按状态（外部调用，兼容保留）。</summary>
        public void EndSelection() => _longPressed = false;
    }
}
