using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 配置モードの操作板（ROADMAP P2・設計 §4.4「出方＝『保存』」）。
    ///
    /// 配置モードの間だけ出て、「保存」「元に戻す」「やめる」の3つを出す。
    /// 発火は調理の板と同じ <see cref="PokePress"/>（押し下げ）なので、
    /// **指でもレイでも押せる**——配置モードでは Ray が生きているが、
    /// 実機でレイが UI Toolkit の板に届かなかったときに出られなくなるのを避けたい
    /// （<see cref="CookingModeInputGate"/> が調理の板だけを止めて、この板は止めないのはそのため）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlacementPanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();

        public event Action SaveRequested;
        public event Action UndoRequested;
        public event Action CancelRequested;

        private Label _hintLabel;

        private void Awake()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _hintLabel = root.Q<Label>("hintLabel");

            PokePress.BindButton(root.Q<Button>("saveButton"), _debounce, "save",
                () => SaveRequested?.Invoke());
            PokePress.BindButton(root.Q<Button>("undoButton"), _debounce, "undo",
                () => UndoRequested?.Invoke());
            PokePress.BindButton(root.Q<Button>("cancelButton"), _debounce, "cancel",
                () => CancelRequested?.Invoke());
        }

        /// <summary>札の文言を差し替える（「覚えました」など）。</summary>
        public void SetHint(string text)
        {
            if (_hintLabel != null)
            {
                _hintLabel.text = text ?? string.Empty;
            }
        }
    }
}
