using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using KitchenXR.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// レシピを選ぶ板（P3。主人の指示）。起動したら**レシピの板の場所に**これが出る。
    ///
    /// v1.0.6（2026-09-13 主人の実機確認）から**写真付きのグリッド**
    /// 「最初の一覧表示の際も、Web のレシピサイトと同じようにグリッドで写真も表示してほしい」。
    /// 3列・上に写真・下に題名と「25分 ・ 中華 ・ 620kcal」。出す文字は今までどおり
    /// 題名・分・分類・kcal の4つだけ（設計 §9「文字は極力少なく」）。
    ///
    /// 写真は <see cref="RecipeStore"/> 越しに**ローカルから**読む（オフライン前提。§11 追補）。
    /// 手元に無ければその場で取りに行き、取れなければ黙って下地のまま——
    /// 一覧が出ないことのほうが困るので、写真の失敗で何も止めない。
    ///
    /// 先頭は必ず「見本: 炒飯」——manor が寝ていても、合言葉が未設定でも、
    /// ここから1本は最後まで進められる。
    ///
    /// 押すのは**カードそのもの**（材料の板と同じ流儀。§11 追補 (a)）。
    /// <see cref="Toggle"/> や <see cref="Button"/> より的が大きく、
    /// <see cref="PokePress"/> の押し下げ発火と相性が良い。
    ///
    /// 選んだあとは「準備中 n/m」の覆いを出す——
    /// レシピの JSON と画像を**先に全部**手元へ落としてから調理を始めるので、その間に
    /// 別のカードを押されないようにする（設計 §7「押し間違いを防ぐ」）。
    ///
    /// 頭の「設定」は、一覧と**入れ替わりで**表示の設定（文字と板の大きさ）を出す。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RecipeListPanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();

        /// <summary>カードが選ばれた。受けるのは <see cref="KitchenXR.App.Bootstrap"/>。</summary>
        public event Action<RecipeSummary> RecipeSelected;

        /// <summary>
        /// 「配置」が**2度**押された（P2。設計 §4.4）。
        /// 起動直後はこの板が出ているので、調理を始める前に板を置き直せる入り口がここに要る。
        /// </summary>
        public event Action PlacementRequested;

        /// <summary>文字の大きさが選ばれた（押した瞬間。受け側が保存して当てる）。</summary>
        public event Action<DisplayScale> FontScaleSelected;

        /// <summary>板の大きさが選ばれた。</summary>
        public event Action<DisplayScale> PanelScaleSelected;

        private const string PlacementLabel = "配置";
        private const string PlacementArmedLabel = "もう一度";
        private const string PlacementArmedClass = "recipe-place-button--armed";

        private const string PrimaryClass = "kitchen-button--primary";
        private const string SecondaryClass = "kitchen-button--secondary";

        /// <summary>「配置」の2度押しの猶予（秒）。レシピの板の「一覧へ」と同じ長さ。</summary>
        public const float PlacementConfirmSeconds = 4f;

        private Label _statusLabel;
        private ScrollView _scroll;
        private VisualElement _busySection;
        private Label _busyLabel;
        private VisualElement _settingsSection;
        private TwoPressButton _placementPress;

        private readonly List<RecipeSummary> _items = new List<RecipeSummary>();

        /// <summary>段ごとの釦（今の値を琥珀にするために持っておく）。</summary>
        private readonly Dictionary<DisplayScale, Button> _fontButtons = new Dictionary<DisplayScale, Button>();
        private readonly Dictionary<DisplayScale, Button> _panelButtons = new Dictionary<DisplayScale, Button>();

        /// <summary>写真の出どころ（Bootstrap から挿す）。無ければ写真は出ない（一覧は出る）。</summary>
        private RecipeStore _store;

        /// <summary>今の一覧の写真読み込み。並べ直す・板が消えるときに断つ。</summary>
        private CancellationTokenSource _imageLoadCts;

        /// <summary>この一覧で作ったテクスチャ。並べ直すときにまとめて捨てる（漏らさない）。</summary>
        private readonly List<Texture2D> _shownTextures = new List<Texture2D>();

        private void Awake()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _statusLabel = root.Q<Label>("statusLabel");
            _scroll = root.Q<ScrollView>("recipeScroll");
            _busySection = root.Q<VisualElement>("busySection");
            _busyLabel = root.Q<Label>("busyLabel");
            _settingsSection = root.Q<VisualElement>("settingsSection");

            var placementButton = root.Q<Button>("placementButton");
            _placementPress = new TwoPressButton(
                placementButton, PlacementLabel, PlacementArmedLabel,
                PlacementConfirmSeconds, PlacementArmedClass);
            _placementPress.Confirmed += () => PlacementRequested?.Invoke();
            PokePress.BindButton(placementButton, _debounce, "place", () => _placementPress.Press());

            BindSettingsButtons(root);
        }

        private void BindSettingsButtons(VisualElement root)
        {
            PokePress.BindButton(root.Q<Button>("settingsButton"), _debounce, "settings",
                () => ShowSettings(true));
            PokePress.BindButton(root.Q<Button>("settingsCloseButton"), _debounce, "settingsClose",
                () => ShowSettings(false));

            BindScaleButton(root, "fontSmallButton", _fontButtons, DisplayScale.Small,
                scale => FontScaleSelected?.Invoke(scale));
            BindScaleButton(root, "fontMediumButton", _fontButtons, DisplayScale.Medium,
                scale => FontScaleSelected?.Invoke(scale));
            BindScaleButton(root, "fontLargeButton", _fontButtons, DisplayScale.Large,
                scale => FontScaleSelected?.Invoke(scale));

            BindScaleButton(root, "panelSmallButton", _panelButtons, DisplayScale.Small,
                scale => PanelScaleSelected?.Invoke(scale));
            BindScaleButton(root, "panelMediumButton", _panelButtons, DisplayScale.Medium,
                scale => PanelScaleSelected?.Invoke(scale));
            BindScaleButton(root, "panelLargeButton", _panelButtons, DisplayScale.Large,
                scale => PanelScaleSelected?.Invoke(scale));
        }

        private void BindScaleButton(
            VisualElement root, string name, Dictionary<DisplayScale, Button> registry,
            DisplayScale scale, Action<DisplayScale> raise)
        {
            var button = root.Q<Button>(name);
            if (button == null)
            {
                return;
            }

            registry[scale] = button;
            PokePress.BindButton(button, _debounce, name, () => raise(scale));
        }

        /// <summary>「配置」が2度目を待っているか（試験用）。</summary>
        public bool IsPlacementArmed => _placementPress != null && _placementPress.IsArmed;

        private void Update() => _placementPress?.Tick();

        private void OnDestroy()
        {
            CancelImageLoads();
            ReleaseTextures();
        }

        /// <summary>今並んでいるレシピ（試験と Bootstrap の確認用）。</summary>
        public IReadOnlyList<RecipeSummary> Items => _items;

        /// <summary>準備中の覆いが出ているか。</summary>
        public bool IsBusy => _busySection != null && !_busySection.ClassListContains("is-hidden");

        /// <summary>表示の設定が出ているか（試験用）。</summary>
        public bool IsShowingSettings =>
            _settingsSection != null && !_settingsSection.ClassListContains("is-hidden");

        /// <summary>写真の出どころを挿す（Bootstrap から。P3 でサーバに変わってもここは変わらない）。</summary>
        public void Bind(RecipeStore store) => _store = store;

        /// <summary>
        /// 一覧を並べ直す。<paramref name="status"/> は板の上の小さな札
        /// （「manor 未設定（見本だけ）」「控えた一覧（manor に繋がりません）」など）。
        /// </summary>
        public void Show(IReadOnlyList<RecipeSummary> items, string status)
        {
            // 前の一覧の写真の取得を断ってから捨てる（作りかけが後から差し込まれないように）。
            CancelImageLoads();
            ReleaseTextures();

            _items.Clear();
            _scroll.Clear();

            if (_statusLabel != null)
            {
                _statusLabel.text = status ?? string.Empty;
            }

            _imageLoadCts = new CancellationTokenSource();

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null || _items.Count >= RecipeListJson.MaxItems)
                    {
                        continue;
                    }

                    _items.Add(item);
                    _scroll.Add(BuildCard(item, _imageLoadCts.Token));
                }
            }

            if (_items.Count == 0)
            {
                var empty = new Label("レシピがありません");
                empty.AddToClassList("recipe-list-empty");
                _scroll.Add(empty);
            }

            HideBusy();
            ShowSettings(false);
        }

        private VisualElement BuildCard(RecipeSummary summary, CancellationToken token)
        {
            var card = new VisualElement();
            card.AddToClassList("recipe-card");
            if (summary.IsBundledSample)
            {
                card.AddToClassList("recipe-card--sample");
            }

            var body = new VisualElement();
            body.AddToClassList("recipe-card__body");
            card.Add(body);

            var photo = new VisualElement();
            photo.AddToClassList("recipe-card__photo");
            body.Add(photo);

            var noPhoto = new Label("写真なし");
            noPhoto.AddToClassList("recipe-card__no-photo");
            photo.Add(noPhoto);

            var title = new Label(summary.Title);
            title.AddToClassList("recipe-card__title");
            body.Add(title);

            var detail = summary.DetailLine();
            if (!string.IsNullOrEmpty(detail))
            {
                var detailLabel = new Label(detail);
                detailLabel.AddToClassList("recipe-card__detail");
                body.Add(detailLabel);
            }

            // 鍵はレシピ id（別のカードを続けて押すのは誤操作ではないので、カードごとに 600ms を数える）。
            PokePress.BindButton(card, _debounce, $"recipe:{summary.Id}", () =>
            {
                if (IsBusy)
                {
                    return; // 準備中は受けない（覆いが出ているので普通は届かないが、念のため）。
                }

                RecipeSelected?.Invoke(summary);
            });

            if (_store != null && !string.IsNullOrEmpty(summary.HeroImage))
            {
                LoadHeroAsync(summary, photo, noPhoto, token).Forget();
            }

            return card;
        }

        /// <summary>
        /// 写真を1枚。手元にあれば通信しない（<see cref="RecipeStore"/> が面倒を見る）ので、
        /// 一度開いたレシピはオフラインでも絵つきで並ぶ。取れなかったら黙って下地のまま。
        /// </summary>
        private async UniTaskVoid LoadHeroAsync(
            RecipeSummary summary, VisualElement photo, VisualElement noPhoto, CancellationToken token)
        {
            Texture2D texture;
            try
            {
                texture = await _store.LoadImageAsync(
                    summary.Id, RecipeStore.HeroImageKey, summary.HeroImage, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (texture == null)
            {
                return;
            }

            // 待っている間に並べ直された／板が消えた。作ったものは自分で捨てる。
            if (token.IsCancellationRequested || this == null)
            {
                DestroyTexture(texture);
                return;
            }

            _shownTextures.Add(texture);
            photo.style.backgroundImage = new StyleBackground(texture);
            noPhoto.style.display = DisplayStyle.None;
        }

        private void CancelImageLoads()
        {
            _imageLoadCts?.Cancel();
            _imageLoadCts?.Dispose();
            _imageLoadCts = null;
        }

        private void ReleaseTextures()
        {
            foreach (var texture in _shownTextures)
            {
                DestroyTexture(texture);
            }

            _shownTextures.Clear();
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }

        // ---------------------------------------------------------------- 表示の設定

        /// <summary>
        /// 今の設定を映す（Bootstrap が起動時と、値が変わったあとに呼ぶ）。
        /// 今の段の釦だけ琥珀（primary）にして、どれが効いているかを見た目で分かるようにする。
        /// </summary>
        public void SetDisplaySettings(DisplayScale fontScale, DisplayScale panelScale)
        {
            MarkSelected(_fontButtons, fontScale);
            MarkSelected(_panelButtons, panelScale);
        }

        private static void MarkSelected(Dictionary<DisplayScale, Button> buttons, DisplayScale selected)
        {
            foreach (var pair in buttons)
            {
                var isSelected = pair.Key == selected;
                pair.Value.EnableInClassList(PrimaryClass, isSelected);
                pair.Value.EnableInClassList(SecondaryClass, !isSelected);
            }
        }

        /// <summary>設定と一覧を入れ替える（板を増やさない）。</summary>
        public void ShowSettings(bool visible)
        {
            if (_settingsSection == null)
            {
                return;
            }

            _settingsSection.EnableInClassList("is-hidden", !visible);
            _scroll?.EnableInClassList("is-hidden", visible);
        }

        // ---------------------------------------------------------------- 覆いと札

        /// <summary>「準備中 n/m」の覆いを出す（<paramref name="total"/> が 0 なら枚数を出さない）。</summary>
        public void ShowPreparing(int done, int total)
        {
            if (_busySection == null)
            {
                return;
            }

            _busySection.RemoveFromClassList("is-hidden");
            if (_busyLabel != null)
            {
                _busyLabel.text = total > 0 ? $"準備中 {done}/{total}" : "準備中";
            }
        }

        /// <summary>覆いに好きな文字を出す（「manor に繋いでいます」など）。</summary>
        public void ShowBusy(string text)
        {
            if (_busySection == null)
            {
                return;
            }

            _busySection.RemoveFromClassList("is-hidden");
            if (_busyLabel != null)
            {
                _busyLabel.text = text ?? "準備中";
            }
        }

        public void HideBusy()
        {
            _busySection?.AddToClassList("is-hidden");
        }

        /// <summary>札だけを書き換える（一覧はそのまま）。</summary>
        public void SetStatus(string status)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = status ?? string.Empty;
            }
        }
    }
}
