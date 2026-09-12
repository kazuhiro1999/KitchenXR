using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 手のひらメニューの中身（設計 §4.4「入り方＝手のひらメニュー」・§11 追補 2026-09-13）。
    ///
    /// 板そのものを手に追わせ、出し入れするのは <see cref="WristMenu"/>
    /// （シーンでの組み立ては <c>KitchenSceneBuilder.AttachWristMenu</c>）で、ここは中身だけを持つ。
    /// v1.0.8 までは XRI の <c>HandMenu</c> が手のひらの向きで出し入れしていたが、
    /// 手を洗っている最中にも出るのでやめた（理由は <see cref="WristMenu"/> に書いた）。
    ///
    /// **配置の操作は全部ここに載せる**（2026-09-13 に空間の操作板をやめた）。主人の言葉:
    ///   「配置の起動は手元でできるが、確定や終了は手元じゃなく 3D 空間に配置されていて、
    ///     ほかのパネルと重なるとレイでボタンが押せなくなるので配置の確定等も手元に表示してほしい」
    /// 頭の前に出していた操作板は、レシピ／一覧の板と重なって当たり判定を奪い合っていた
    /// （「メインパネルがつかむことすらできない」の一因）。手のひらなら重なる相手が居ない。
    ///
    ///   調理中     「配置」1つ
    ///   配置モード 「保存」「元に戻す」「板を手元に」「やめる」＋1行のヒント
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
    /// 同じ理由で、出ている釦の組とヒントは**この MonoBehaviour 側に覚えておいて**、
    /// 配線し直すたびに板へ塗り直す。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlacementMenuPanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();

        /// <summary>「配置」——配置モードへ入る。</summary>
        public event Action PlacementRequested;

        /// <summary>「保存」——覚えて調理モードへ戻る。</summary>
        public event Action SaveRequested;

        /// <summary>「元に戻す」——入る前の位置へ（配置モードは続く）。</summary>
        public event Action UndoRequested;

        /// <summary>「やめる」——元に戻して調理モードへ。</summary>
        public event Action CancelRequested;

        /// <summary>「板を手元に」——迷子の板を今の頭の前へ並べ直す（配置モードは続く）。</summary>
        public event Action RecallRequested;

        /// <summary>配置モードに入ったときの札。</summary>
        public const string DefaultHint = "板を掴んで動かし、終わったら「保存」";

        private bool _placing;
        private string _hint = DefaultHint;
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

        /// <summary>配置モードに入った／出た。出す釦の組を入れ替える。</summary>
        public void SetPlacing(bool placing)
        {
            _placing = placing;
            if (!placing)
            {
                _hint = DefaultHint;
            }

            ApplyState();
        }

        /// <summary>ヒントの文言を差し替える（「覚えました」など）。</summary>
        public void SetHint(string text)
        {
            _hint = string.IsNullOrEmpty(text) ? DefaultHint : text;
            ApplyState();
        }

        private VisualElement Root
        {
            get
            {
                var document = GetComponent<UIDocument>();
                return document != null ? document.rootVisualElement : null;
            }
        }

        private void TryBind()
        {
            var root = Root;
            var placementButton = root?.Q<Button>("placementButton");
            if (placementButton == null)
            {
                return;
            }

            PokePress.BindButton(placementButton, _debounce, "place", () => PlacementRequested?.Invoke());
            PokePress.BindButton(root.Q<Button>("saveButton"), _debounce, "save", () => SaveRequested?.Invoke());
            PokePress.BindButton(root.Q<Button>("undoButton"), _debounce, "undo", () => UndoRequested?.Invoke());
            PokePress.BindButton(root.Q<Button>("recallButton"), _debounce, "recall", () => RecallRequested?.Invoke());
            PokePress.BindButton(root.Q<Button>("cancelButton"), _debounce, "cancel", () => CancelRequested?.Invoke());

            _bound = true;
            ApplyState();
        }

        /// <summary>覚えている状態（配置中か・ヒント）を板へ塗る。</summary>
        private void ApplyState()
        {
            var root = Root;
            if (root == null)
            {
                return;
            }

            SetShown(root.Q<VisualElement>(IdleGroupName), !_placing);
            SetShown(root.Q<VisualElement>(PlacingGroupName), _placing);

            var hintLabel = root.Q<Label>("hintLabel");
            if (hintLabel != null)
            {
                hintLabel.text = _hint;
                SetShown(hintLabel, _placing);
            }
        }

        public const string IdleGroupName = "idleGroup";
        public const string PlacingGroupName = "placingGroup";

        private static void SetShown(VisualElement element, bool shown)
        {
            if (element == null)
            {
                return;
            }

            element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
