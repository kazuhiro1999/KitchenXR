using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 板の出し入れ（P3。一覧の板 ⇄ 調理の板の切り替え）。
    ///
    /// **<c>GameObject.SetActive</c> では切り替えない。** <see cref="UIDocument"/> は
    /// 無効になるときに <c>rootVisualElement</c> を手放し、有効になるときに作り直すので、
    /// <c>Awake</c> で <c>Q&lt;&gt;</c> して掴んでおいた要素の参照が全部死ぬ
    /// （各パネルは Awake で1度だけ引いて持つ作りになっている）。
    ///
    /// 代わりに3つを一緒に切る:
    ///   1. 板の中身（<c>rootVisualElement</c> の display）——見えなくする
    ///   2. <see cref="BoxCollider"/>——指が当たらなくする
    ///   3. <see cref="XRSimpleInteractable"/>——ホバーの色と振動も出さない
    ///
    /// 2 と 3 を一緒に切るのが肝心。見えない板が当たり判定だけ残っていると、
    /// 一覧を選んだ直後に指が「まだそこにある透明な板」を突いてしまう（設計 §7 の誤操作）。
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
