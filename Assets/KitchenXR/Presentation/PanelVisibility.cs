using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 板の出し入れ（一覧の板 ⇄ 調理の板の切り替え）。
    ///
    /// <c>GameObject.SetActive</c> では切り替えない——<see cref="UIDocument"/> は無効になるとき
    /// <c>rootVisualElement</c> を手放して作り直すので、各パネルが <c>Awake</c> で掴んだ要素の
    /// 参照が全部死ぬ。代わりに3つを一緒に切る: 中身の display・<see cref="BoxCollider"/>・
    /// <see cref="XRSimpleInteractable"/>。後ろ2つを一緒に切るのが肝心で、見えない板が当たり
    /// 判定だけ残っていると、一覧を選んだ直後に指が「まだそこにある透明な板」を突いてしまう。
    /// </summary>
    public static class PanelVisibility
    {
        public static void SetVisible(Component panel, bool visible)
        {
            if (panel == null)
            {
                return;
            }

            SetVisible(panel.gameObject, visible);
        }

        public static void SetVisible(GameObject panel, bool visible)
        {
            if (panel == null)
            {
                return;
            }

            var document = panel.GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            if (root != null)
            {
                root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            var collider = panel.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.enabled = visible;
            }

            var interactable = panel.GetComponent<XRSimpleInteractable>();
            if (interactable != null)
            {
                interactable.enabled = visible;
            }
        }

        /// <summary>今出ているか（試験用）。</summary>
        public static bool IsVisible(Component panel)
        {
            if (panel == null)
            {
                return false;
            }

            var document = panel.GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            return root != null && root.style.display.value != DisplayStyle.None;
        }
    }
}
