using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 手のひらメニューの中身（板を手に追わせ、出し入れするのは <see cref="WristMenu"/>）。
    /// 調理中は「配置」1つ、配置モードでは「保存」「元に戻す」「板を手元に」「やめる」＋
    /// ヒント1行。配置の操作を全部ここに載せるのは、空間に出した操作板がレシピ／一覧の板と
    /// 重なって当たり判定を奪い合うため。ここの釦は2度押しにしない（手を返して目で見て押す
    /// ので、調理中の手が偶然押すことはまず無い）。
    ///
    /// 配線を <c>Awake</c> ではなく有効になるたびやり直すのが肝心——板は <c>SetActive</c> で
    /// 出し入れされ、<see cref="UIDocument"/> は無効化のたびに <c>rootVisualElement</c> を捨てて
    /// 作り直すので、1度掴んだ要素の参照は次に出たときには死んでいる。同じ理由で、出ている
    /// 釦の組とヒントはこの MonoBehaviour 側に覚えておいて、配線し直すたびに板へ塗り直す。
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
