using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 手首に付けっぱなしの小さな板の中身。釦は1つだけで、押すと <see cref="Toggled"/> が上がる。
    /// 板を手首に追わせるのは <see cref="WristMenu"/>。
    ///
    /// 配線を <c>Awake</c> ではなく有効になるたびやり直すのは
    /// <see cref="PlacementMenuPanel"/> と同じ理由——<see cref="UIDocument"/> は
    /// 無効化のたびに <c>rootVisualElement</c> を捨てて作り直すので、
    /// 1度掴んだ要素の参照は次に出たときには死んでいる。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class WristMenuButton : MonoBehaviour
    {
        /// <summary>uxml の中の釦の名前（シーンの検算が見る）。</summary>
        public const string ButtonName = "wristToggleButton";

        private readonly ClickDebounce _debounce = new ClickDebounce();

        /// <summary>釦が押された（メニューの出し入れ）。</summary>
        public event Action Toggled;

        private bool _bound;

        private void OnEnable()
        {
            _bound = false;
            TryBind();
        }

        private void Update()
        {
            // rootVisualElement が出来るのは有効化と同じフレームとは限らないので、出来るまで待つ。
            if (!_bound)
            {
                TryBind();
            }
        }

        private void TryBind()
        {
            var document = GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            var button = root?.Q<Button>(ButtonName);
            if (button == null)
            {
                return;
            }

            // 2度押しにしない——手首の内側は調理中の手が偶然触る場所ではないし、
            // 押して出るのはメニューだけで、取り消せない操作は何も起きない。
            PokePress.BindButton(button, _debounce, "wrist-toggle", () => Toggled?.Invoke());
            _bound = true;
        }
    }
}
