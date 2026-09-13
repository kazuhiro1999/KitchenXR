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
    /// レシピを選ぶ板（起動したらレシピの板の場所にこれが出る）。3列のグリッドで、
    /// 上に写真・下に題名と「25分 ・ 中華 ・ 620kcal」。頭の「設定」は一覧と入れ替わりで
    /// 表示の設定を出す（板を増やさない）。先頭は必ず見本。
    ///
    /// 写真は <see cref="RecipeStore"/> 越しにローカルから読み、取れなければ黙って下地のまま
    /// ——一覧が出ないことのほうが困る。押すのは <see cref="Toggle"/> や <see cref="Button"/>
    /// ではなくカードそのもの（的が大きく、押し下げ発火と相性が良い）。
    ///
    /// 選んだあとは「準備中 n/m」の覆いを出す——JSON と画像を先に全部手元へ落としてから
    /// 調理を始めるので、その間に別のカードを押されないようにする。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RecipeListPanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();

        /// <summary>カードが選ばれた。受けるのは <see cref="KitchenXR.App.Bootstrap"/>。</summary>
        public event Action<RecipeSummary> RecipeSelected;

        /// <summary>
        /// 「配置」が2度押された。
        /// 起動直後はこの板が出ているので、調理を始める前に板を置き直せる入り口がここに要る。
        /// </summary>
        public event Action PlacementRequested;

        /// <summary>文字の大きさが選ばれた（押した瞬間。受け側が保存して当てる）。</summary>
        public event Action<DisplayScale> FontScaleSelected;

        /// <summary>板の大きさが選ばれた。</summary>
        public event Action<DisplayScale> PanelScaleSelected;

        /// <summary>カメラの入／切が選ばれた（受け側が権限を求め、保存する）。</summary>
        public event Action<bool> CameraEnabledSelected;

        private const string PlacementLabel = "配置";
        private const string PlacementArmedLabel = "もう一度";
        private const string PlacementArmedClass = "recipe-place-button--armed";

        private const string PrimaryClass = "kitchen-button--primary";
        private const string SecondaryClass = "kitchen-button--secondary";

        /// <summary>番号の下に添える手順（どこへ入れるかで迷わないように）。</summary>
        public const string DefaultPairingHint = "manor の 設定 → 端末 に入れて許可してください";

        /// <summary>「配置」の2度押しの猶予（秒）。レシピの板の「一覧へ」と同じ長さ。</summary>
        public const float PlacementConfirmSeconds = 4f;

        private Label _statusLabel;
        private ScrollView _scroll;
        private VisualElement _busySection;
        private Label _busyLabel;
        private Button _busyCloseButton;

        /// <summary>ペアリングの6桁と手順（覆いの中に出す）。</summary>
        private Label _pairCodeLabel;
        private Label _pairHintLabel;

        private VisualElement _settingsSection;
        private TwoPressButton _placementPress;

        private readonly List<RecipeSummary> _items = new List<RecipeSummary>();

        /// <summary>段ごとの釦（今の値を琥珀にするために持っておく）。</summary>
        private readonly Dictionary<DisplayScale, Button> _fontButtons = new Dictionary<DisplayScale, Button>();
        private readonly Dictionary<DisplayScale, Button> _panelButtons = new Dictionary<DisplayScale, Button>();

        /// <summary>カメラの 無効／有効 の2釦（今の値を琥珀にする）。</summary>
        private Button _cameraOffButton;
        private Button _cameraOnButton;

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
            _busyCloseButton = root.Q<Button>("busyCloseButton");
            PokePress.BindButton(_busyCloseButton, _debounce, "busyClose", HideBusy);
            _pairCodeLabel = root.Q<Label>("pairCodeLabel");
            _pairHintLabel = root.Q<Label>("pairHintLabel");
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

            _cameraOffButton = root.Q<Button>("cameraOffButton");
            _cameraOnButton = root.Q<Button>("cameraOnButton");
            PokePress.BindButton(_cameraOffButton, _debounce, "cameraOff",
                () => CameraEnabledSelected?.Invoke(false));
            PokePress.BindButton(_cameraOnButton, _debounce, "cameraOn",
                () => CameraEnabledSelected?.Invoke(true));
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

        /// <summary>写真の出どころを挿す（Bootstrap から）。</summary>
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

        /// <summary>カメラの今の値を映す（無効／有効のどちらかが琥珀になる）。</summary>
        public void SetCameraEnabled(bool enabled)
        {
            MarkButton(_cameraOnButton, enabled);
            MarkButton(_cameraOffButton, !enabled);
        }

        private static void MarkButton(Button button, bool isSelected)
        {
            if (button == null)
            {
                return;
            }

            button.EnableInClassList(PrimaryClass, isSelected);
            button.EnableInClassList(SecondaryClass, !isSelected);
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
            HidePairing();
            HideBusyClose();
            if (_busyLabel != null)
            {
                _busyLabel.text = total > 0 ? $"準備中 {done}/{total}" : "準備中";
            }
        }

        /// <summary>
        /// 覆いに案内を出す（カメラを初めて有効にしたときの「立ち上げ直してください」など）。
        /// 準備中と違って自分で閉じられる——閉じれば一覧がそのまま使える。
        /// </summary>
        public void ShowNotice(string text)
        {
            if (_busySection == null)
            {
                return;
            }

            _busySection.RemoveFromClassList("is-hidden");
            HidePairing();

            if (_busyLabel != null)
            {
                _busyLabel.text = text ?? string.Empty;
            }

            _busyCloseButton?.RemoveFromClassList("is-hidden");
        }

        /// <summary>覆いに出ている文（試験用。出ていなければ空）。</summary>
        public string BusyText => IsBusy ? _busyLabel?.text ?? string.Empty : string.Empty;

        /// <summary>案内の「閉じる」が出ているか（試験用）。</summary>
        public bool IsNoticeClosable =>
            IsBusy && _busyCloseButton != null && !_busyCloseButton.ClassListContains("is-hidden");

        /// <summary>覆いに好きな文字を出す（「manor を探しています」など）。</summary>
        public void ShowBusy(string text)
        {
            if (_busySection == null)
            {
                return;
            }

            _busySection.RemoveFromClassList("is-hidden");
            HidePairing();
            HideBusyClose();
            if (_busyLabel != null)
            {
                _busyLabel.text = text ?? "準備中";
            }
        }

        /// <summary>
        /// ペアリングの番号を覆いに出す。板に文字入力は置かないので、番号は端末に出して
        /// Web 側に入れてもらう。覆いの仕組みをそのまま使うのは、この間レシピを選ばせては
        /// いけないから（鍵が無いうちは manor のレシピを開けない）。
        /// </summary>
        public void ShowPairing(string code, string hint = DefaultPairingHint)
        {
            if (_busySection == null)
            {
                return;
            }

            _busySection.RemoveFromClassList("is-hidden");
            HideBusyClose();

            if (_busyLabel != null)
            {
                _busyLabel.text = "manor と繋ぎます";
            }

            if (_pairCodeLabel != null)
            {
                _pairCodeLabel.text = code ?? string.Empty;
                _pairCodeLabel.EnableInClassList("is-hidden", string.IsNullOrEmpty(code));
            }

            if (_pairHintLabel != null)
            {
                _pairHintLabel.text = hint ?? string.Empty;
                _pairHintLabel.EnableInClassList("is-hidden", string.IsNullOrEmpty(hint));
            }
        }

        /// <summary>覆いに出ているペアリングの番号（試験用。出ていなければ空）。</summary>
        public string PairingCode =>
            _pairCodeLabel != null && !_pairCodeLabel.ClassListContains("is-hidden")
                ? _pairCodeLabel.text ?? string.Empty
                : string.Empty;

        public void HideBusy()
        {
            _busySection?.AddToClassList("is-hidden");
            HidePairing();
            HideBusyClose();
        }

        private void HideBusyClose() => _busyCloseButton?.AddToClassList("is-hidden");

        private void HidePairing()
        {
            _pairCodeLabel?.AddToClassList("is-hidden");
            _pairHintLabel?.AddToClassList("is-hidden");
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
