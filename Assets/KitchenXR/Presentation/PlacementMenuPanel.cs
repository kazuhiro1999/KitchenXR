using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 手のひらメニューの中身（設計 §4.4「入り方＝手のひらメニュー」）。
    ///
    /// 板そのものを手のひらに追わせるのは XRI の <c>HandMenu</c>（シーンで結ぶ。
    /// <c>KitchenSceneBuilder.AttachHandMenu</c>）で、ここは「配置」の釦1つを持つだけ。
    ///
    /// **2度押しにしない。** 手のひらメニューは「手を返して、目で見て、押す」ので、
    /// 調理中の手が偶然この釦を押すことはまず無い（レシピの板の頭に付けた同じ釦のほうは、
    /// 調理の面に並んでいるので2度押しにしてある）。
    ///
    /// 配線を <c>Awake</c> ではなく**有効になるたび**やり直すのが肝心——
    /// <c>HandMenu</c> は板を <c>SetActive</c> で出し入れし、
    /// <see cref="UIDocument"/> は無効化のたびに <c>rootVisualElement</c> を捨てて作り直すので、
    /// 1度掴んだ要素の参照は次に出たときには死んでいる
    /// （<see cref="PanelVisibility"/> が SetActive を使わない理由と同じ罠）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlacementMenuPanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();

        public event Action PlacementRequested;

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
            var button = root?.Q<Button>("placementButton");
            if (button == null)
            {
                return;
            }

            PokePress.BindButton(button, _debounce, "place", () => PlacementRequested?.Invoke());
            _bound = true;
        }
    }
}
