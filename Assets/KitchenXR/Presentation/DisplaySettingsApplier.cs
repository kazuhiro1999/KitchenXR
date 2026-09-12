using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// <see cref="DisplaySettings"/> を板へ当てる唯一の場所
    /// （2026-09-13 主人「設定とかで変更できたらもっといい」）。
    ///
    /// 当て方は2つ:
    ///   - **文字**: 板の <c>.root-panel</c> に <c>font-scale--small</c>／<c>font-scale--large</c> を
    ///     付け外しする。<c>theme.uss</c> のそのクラスが <c>--font-size-*</c> を上書きし、
    ///     カスタムプロパティは継承するので中の全部（ボタンの文字も材料の行も）が一緒に動く
    ///   - **板**: <c>transform.localScale</c> を
    ///     <see cref="WorldSpacePanelFactory.PanelLocalScale"/> × 係数にする。
    ///     コライダーはローカル単位なので一緒に拡縮される（＝押せる場所も一緒に動く）。
    ///     板の原点は左上なので、大きくすると**右下へ**伸びる
    ///
    /// 位置と向きは触らない。<see cref="PanelPlacement"/> が覚える控え（`panels.json`）も
    /// アンカーも位置と向きしか持たないので、縮尺は**起動のたびに設定から当て直す**。
    ///
    /// 当てる先は調理と一覧の4枚（レシピ一覧・レシピ・材料・タイマー）だけ。
    /// 動画の板は別の寸法の決め方（16:9 ⇄ 9:16）を持っているので混ぜない。
    /// 配置モードの操作板と手のひらメニューも対象外——出ている間だけの板で、
    /// 大きさを変える意味がない。
    /// </summary>
    public static class DisplaySettingsApplier
    {
        public const string FontScaleSmallClass = "font-scale--small";
        public const string FontScaleLargeClass = "font-scale--large";

        /// <summary>板の縮尺の係数（小／中／大）。中は 1.0＝v1.0.6 までと同じ大きさ。</summary>
        public static float PanelScaleFactor(DisplayScale scale)
        {
            switch (scale)
            {
                case DisplayScale.Small:
                    return 0.85f;
                case DisplayScale.Large:
                    return 1.2f;
                default:
                    return 1f;
            }
        }

        /// <summary>設定に対応する文字の大きさのクラス名。中は付けない（null）。</summary>
        public static string FontScaleClass(DisplayScale scale)
        {
            switch (scale)
            {
                case DisplayScale.Small:
                    return FontScaleSmallClass;
                case DisplayScale.Large:
                    return FontScaleLargeClass;
                default:
                    return null;
            }
        }

        /// <summary>まとめて当てる（Bootstrap が起動時と設定を変えたときに呼ぶ）。</summary>
        public static void Apply(DisplaySettings settings, params Component[] panels)
        {
            if (settings == null || panels == null)
            {
                return;
            }

            foreach (var panel in panels)
            {
                ApplyTo(settings, panel);
            }
        }

        /// <summary>板1枚へ当てる。</summary>
        public static void ApplyTo(DisplaySettings settings, Component panel)
        {
            if (settings == null || panel == null)
            {
                return;
            }

            ApplyFontScale(settings.FontScale, panel);
            ApplyPanelScale(settings.PanelScale, panel);
        }

        public static void ApplyFontScale(DisplayScale scale, Component panel)
        {
            var root = FindPanelRoot(panel);
            if (root == null)
            {
                return;
            }

            root.RemoveFromClassList(FontScaleSmallClass);
            root.RemoveFromClassList(FontScaleLargeClass);

            var className = FontScaleClass(scale);
            if (!string.IsNullOrEmpty(className))
            {
                root.AddToClassList(className);
            }
        }

        public static void ApplyPanelScale(DisplayScale scale, Component panel)
        {
            if (panel == null)
            {
                return;
            }

            var side = WorldSpacePanelFactory.PanelLocalScale * PanelScaleFactor(scale);
            panel.transform.localScale = new Vector3(side, side, side);
        }

        /// <summary>
        /// 板の中の <c>.root-panel</c>（UXML の <c>name="root"</c>）を引く。
        /// <see cref="UIDocument.rootVisualElement"/> そのものではないのが肝心——
        /// そちらは <see cref="PanelVisibility"/> が display を切るために使っている外側の器で、
        /// <c>theme.uss</c> の地の色も文字も当たっていない。
        /// </summary>
        private static VisualElement FindPanelRoot(Component panel)
        {
            var document = panel != null ? panel.GetComponent<UIDocument>() : null;
            var documentRoot = document != null ? document.rootVisualElement : null;
            if (documentRoot == null)
            {
                return null;
            }

            return documentRoot.Q<VisualElement>(className: "root-panel") ?? documentRoot;
        }
    }
}
