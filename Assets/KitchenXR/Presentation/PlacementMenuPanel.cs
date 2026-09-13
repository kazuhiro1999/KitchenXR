using System;
using System.Collections.Generic;
using KitchenXR.Presentation.Hazard;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 手のひらメニューの中身（板を手に追わせ、出し入れするのは <see cref="WristMenu"/>）。
    /// 配置の操作を全部ここに載せるのは、空間に出した操作板がレシピ／一覧の板と重なって
    /// 当たり判定を奪い合うため。ここの釦は2度押しにしない（手を返して目で見て押すので、
    /// 調理中の手が偶然押すことはまず無い）。例外は領域の「消す」——取り消せない。
    ///
    /// 頁は4つ（<see cref="Page"/>）。**頁ごとに板の高さを変える**——プリセットの一覧は縦に
    /// 5行あって配置の操作と同じ高さには入らない。寸法の変え方は動画の板（16:9 ⇄ 9:16）と
    /// 同じ <see cref="WorldSpacePanelFactory.Resize"/> で、板の矩形と当たり判定を一緒に動かす。
    ///
    /// 配線を <c>Awake</c> ではなく有効になるたびやり直すのが肝心——板は <c>SetActive</c> で
    /// 出し入れされ、<see cref="UIDocument"/> は無効化のたびに <c>rootVisualElement</c> を捨てて
    /// 作り直すので、1度掴んだ要素の参照は次に出たときには死んでいる。同じ理由で、出ている
    /// 頁とヒントとプリセットの一覧はこの MonoBehaviour 側に覚えておいて、配線し直すたびに
    /// 板へ塗り直す。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlacementMenuPanel : MonoBehaviour
    {
        /// <summary>メニューのどの頁が出ているか。</summary>
        public enum Page
        {
            /// <summary>調理中（「配置」1つ）。</summary>
            Idle,

            /// <summary>配置モードの操作（3行×2）。</summary>
            Placing,

            /// <summary>注意の板のプリセットの一覧。</summary>
            Hazard,

            /// <summary>コンロの領域（囲む・高さ・消す）。</summary>
            Zone,
        }

        /// <summary>板の幅（130 px ≒ 26cm）。頁で変えない。</summary>
        public const float WidthUnits = 130f;

        /// <summary>調理中の高さ（92 px ≒ 18.4cm）。</summary>
        public const float IdleHeightUnits = 92f;

        /// <summary>配置の操作の高さ。ヒント1行 ＋ 30px の行が3つ ＋ 余白。</summary>
        public const float PlacingHeightUnits = 112f;

        /// <summary>プリセットの一覧の高さ。24px の行が5つ ＋「戻る」の行。</summary>
        public const float HazardHeightUnits = 176f;

        /// <summary>領域の頁の高さ。ヒントが2行になるので配置の操作より少し高い。</summary>
        public const float ZoneHeightUnits = 124f;

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

        /// <summary>プリセットが選ばれた（注意の板を1枚作る）。</summary>
        public event Action<string> HazardPresetSelected;

        /// <summary>「囲む」——次のピンチから領域を描く。</summary>
        public event Action ZoneDrawRequested;

        /// <summary>「やり直す」——最後の領域を捨てて描き直す。</summary>
        public event Action ZoneRedoRequested;

        /// <summary>「消す」（2度押し）——最後の領域を消す。</summary>
        public event Action ZoneRemoveRequested;

        /// <summary>上面の高さを ±5cm（引数は段数。−1／+1）。</summary>
        public event Action<int> ZoneHeightRequested;

        /// <summary>頁が変わった（呼び出し側が描くのをやめる契機）。</summary>
        public event Action<Page> PageChanged;

        /// <summary>配置モードに入ったときの札。</summary>
        public const string DefaultHint = "板を掴んで動かし、終わったら「保存」";

        /// <summary>注意の板の頁の札。</summary>
        public const string HazardHint = "選ぶと頭の前に板が出ます";

        /// <summary>領域の頁の札。</summary>
        public const string ZoneHint = "「囲む」→ まず手前の辺を引き、次に奥行きを引く（つまんで引いて離す×2）";

        private const string ZoneRemoveLabel = "消す";
        private const string ZoneRemoveArmedLabel = "もう一度";
        private const string ZoneRemoveArmedClass = "kitchen-button--armed";

        /// <summary>領域の「消す」の2度押しの猶予（秒）。他の2度押しと同じ長さ。</summary>
        public const float ZoneRemoveConfirmSeconds = 4f;

        private Page _page = Page.Idle;
        private string _hint = DefaultHint;
        private bool _bound;

        private readonly List<HazardPreset> _presets = new List<HazardPreset>();

        private TwoPressButton _zoneRemovePress;

        /// <summary>今出ている頁（試験が見る）。</summary>
        public Page CurrentPage => _page;

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
                return;
            }

            _zoneRemovePress?.Tick();
        }

        /// <summary>配置モードに入った／出た。出す頁を入れ替える。</summary>
        public void SetPlacing(bool placing) => SetPage(placing ? Page.Placing : Page.Idle);

        /// <summary>頁を切り替える（ヒントも既定へ戻し、板の高さも合わせる）。</summary>
        public void SetPage(Page page)
        {
            _page = page;
            _hint = DefaultHintFor(page);
            _zoneRemovePress?.Disarm();

            ApplyState();
            PageChanged?.Invoke(page);
        }

        /// <summary>ヒントの文言を差し替える（「覚えました」など）。</summary>
        public void SetHint(string text)
        {
            _hint = string.IsNullOrEmpty(text) ? DefaultHintFor(_page) : text;
            ApplyState();
        }

        /// <summary>プリセットの一覧を挿す（<c>Bootstrap</c> が起動時に1度）。</summary>
        public void SetPresets(IReadOnlyList<HazardPreset> presets)
        {
            _presets.Clear();
            if (presets != null)
            {
                _presets.AddRange(presets);
            }

            ApplyState();
        }

        private static string DefaultHintFor(Page page)
        {
            switch (page)
            {
                case Page.Hazard:
                    return HazardHint;
                case Page.Zone:
                    return ZoneHint;
                default:
                    return DefaultHint;
            }
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

            PokePress.BindButton(root.Q<Button>("hazardMenuButton"), _debounce, "hazardMenu",
                () => SetPage(Page.Hazard));
            PokePress.BindButton(root.Q<Button>("zoneMenuButton"), _debounce, "zoneMenu",
                () => SetPage(Page.Zone));
            PokePress.BindButton(root.Q<Button>("hazardBackButton"), _debounce, "hazardBack",
                () => SetPage(Page.Placing));
            PokePress.BindButton(root.Q<Button>("zoneBackButton"), _debounce, "zoneBack",
                () => SetPage(Page.Placing));

            PokePress.BindButton(root.Q<Button>("zoneDrawButton"), _debounce, "zoneDraw",
                () => ZoneDrawRequested?.Invoke());
            PokePress.BindButton(root.Q<Button>("zoneRedoButton"), _debounce, "zoneRedo",
                () => ZoneRedoRequested?.Invoke());
            PokePress.BindButton(root.Q<Button>("zoneLowerButton"), _debounce, "zoneLower",
                () => ZoneHeightRequested?.Invoke(-1));
            PokePress.BindButton(root.Q<Button>("zoneRaiseButton"), _debounce, "zoneRaise",
                () => ZoneHeightRequested?.Invoke(1));

            // 領域を消すのは取り消せないので2度押し（板の「消す」と同じ流儀）。
            var removeButton = root.Q<Button>("zoneRemoveButton");
            _zoneRemovePress = new TwoPressButton(
                removeButton, ZoneRemoveLabel, ZoneRemoveArmedLabel,
                ZoneRemoveConfirmSeconds, ZoneRemoveArmedClass);
            _zoneRemovePress.Confirmed += () => ZoneRemoveRequested?.Invoke();
            PokePress.BindButton(removeButton, _debounce, "zoneRemove", () => _zoneRemovePress.Press());

            _bound = true;
            ApplyState();
        }

        /// <summary>覚えている状態（頁・ヒント・プリセット）を板へ塗る。</summary>
        private void ApplyState()
        {
            var root = Root;
            if (root == null)
            {
                return;
            }

            SetShown(root.Q<VisualElement>(IdleGroupName), _page == Page.Idle);
            SetShown(root.Q<VisualElement>(PlacingGroupName), _page == Page.Placing);
            SetShown(root.Q<VisualElement>(HazardGroupName), _page == Page.Hazard);
            SetShown(root.Q<VisualElement>(ZoneGroupName), _page == Page.Zone);

            var hintLabel = root.Q<Label>("hintLabel");
            if (hintLabel != null)
            {
                hintLabel.text = _hint;
                SetShown(hintLabel, _page != Page.Idle);
            }

            FillPresets(root);
            Resize();
        }

        /// <summary>プリセットの釦を作り直す（板が出るたび。要素の参照は持ち越せない）。</summary>
        private void FillPresets(VisualElement root)
        {
            var list = root.Q<VisualElement>(HazardListName);
            if (list == null)
            {
                return;
            }

            list.Clear();

            foreach (var preset in _presets)
            {
                var id = preset.Id;
                var button = new Button { text = preset.Title, name = "hazardPreset_" + id };
                button.AddToClassList("kitchen-button");
                button.AddToClassList(preset.Accent == HazardAccent.Red
                    ? "kitchen-button--primary"
                    : "kitchen-button--secondary");
                button.AddToClassList("placement-menu-preset");

                PokePress.BindButton(button, _debounce, "hazardPreset_" + id,
                    () => HazardPresetSelected?.Invoke(id));

                list.Add(button);
            }
        }

        /// <summary>頁に合わせて板の高さを変える（矩形と当たり判定を一緒に）。</summary>
        private void Resize()
        {
            var document = GetComponent<UIDocument>();
            if (document == null)
            {
                return;
            }

            var height = _page switch
            {
                Page.Placing => PlacingHeightUnits,
                Page.Hazard => HazardHeightUnits,
                Page.Zone => ZoneHeightUnits,
                _ => IdleHeightUnits,
            };

            if (Mathf.Approximately(document.worldSpaceSize.y, height) &&
                Mathf.Approximately(document.worldSpaceSize.x, WidthUnits))
            {
                return;
            }

            WorldSpacePanelFactory.Resize(gameObject, document, WidthUnits, height);
        }

        public const string IdleGroupName = "idleGroup";
        public const string PlacingGroupName = "placingGroup";
        public const string HazardGroupName = "hazardGroup";
        public const string ZoneGroupName = "zoneGroup";
        public const string HazardListName = "hazardList";

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
