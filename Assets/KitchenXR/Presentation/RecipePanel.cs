using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using KitchenXR.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// レシピパネル（主。設計 §9・§11 追補）。上に**工程の数だけ**の点列と進捗%、
    /// 中央に今の工程（左に画像・右に見出しと説明と次の工程）、下にボタンの行。
    /// CookSession は持たない——Bootstrap から渡された状態を映すだけ。
    ///
    /// v1.0.6（2026-09-13 主人の実機確認）で**1画面に収める**作りへ変えた。
    /// 中身がなぜ左右2列なのかは <c>RecipePanel.uss</c> の先頭に書いた。
    ///
    /// 「次へ／戻る」は <see cref="PokePress"/> で**押し下げ**に反応する（設計 §11 追補）。
    /// <c>Button.clicked</c>（押し上げ）は購読しない——深く突き抜けると発火しないため。
    ///
    /// 画像は <see cref="RecipeStore"/> 越しに**ローカルから**読む（オフライン前提。§11 追補）。
    /// 無ければその場で取りに行き、取れなければ材料名の淡い札で代える。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RecipePanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();

        /// <summary>
        /// 「一覧へ」の2度押しを受け付ける猶予（秒）。1度目から4秒で元に戻る。
        /// <see cref="ClickDebounce"/> が 600ms 間引くので、2度目はそれより後になる。
        /// </summary>
        public const float BackToListConfirmSeconds = 4f;

        private const string BackToListLabel = "一覧へ";
        private const string BackToListArmedLabel = "もう一度";
        private const string BackToListArmedClass = "recipe-to-list-button--armed";

        private const string PlacementLabel = "配置";
        private const string PlacementArmedLabel = "もう一度";
        private const string PlacementArmedClass = "recipe-place-button--armed";

        /// <summary>工程の画像が本文の幅に占めてよい割合。残りが説明の置き場になる。</summary>
        private const float ImageMaxWidthRatio = 0.55f;

        public event Action NextRequested;
        public event Action PrevRequested;

        /// <summary>「一覧へ」が**2度**押された（P3。1度目は身構えるだけ）。</summary>
        public event Action BackToListRequested;

        /// <summary>「作り終えた」が押された（P3。完了したときだけ出るボタン）。</summary>
        public event Action FinishRequested;

        /// <summary>
        /// 「配置」が**2度**押された（P2。板を置き直すモードへ。設計 §4.4）。
        /// 手のひらメニューが実機で出ないときの、確実な入り口を兼ねる。
        /// </summary>
        public event Action PlacementRequested;

        private VisualElement _stepDots;
        private Label _progressLabel;
        private VisualElement _currentSection;
        private Label _currentTitle;
        private VisualElement _currentImage;
        private Label _currentIngredientChip;
        private Label _currentInstruction;
        private Label _nextLabel;
        private VisualElement _completeSection;
        private Button _backButton;
        private Button _nextButton;
        private Button _backToListButton;
        private Button _finishButton;
        private TwoPressButton _placementPress;

        private RecipeStore _store;
        private string _recipeId;

        private string _shownImageKey;
        private Texture2D _shownTexture;
        private CancellationTokenSource _imageLoadCts;

        /// <summary>「一覧へ」の1度目を受けた時刻＋猶予。0 なら身構えていない。</summary>
        private float _backToListArmedUntil;

        private void Awake()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _stepDots = root.Q<VisualElement>("stepDots");
            _progressLabel = root.Q<Label>("progressLabel");
            _currentSection = root.Q<VisualElement>("currentSection");
            _currentTitle = root.Q<Label>("currentTitle");
            _currentImage = root.Q<VisualElement>("currentImage");
            _currentIngredientChip = root.Q<Label>("currentIngredientChip");
            _currentInstruction = root.Q<Label>("currentInstruction");
            _nextLabel = root.Q<Label>("nextLabel");
            _completeSection = root.Q<VisualElement>("completeSection");
            _backButton = root.Q<Button>("backButton");
            _nextButton = root.Q<Button>("nextButton");
            _backToListButton = root.Q<Button>("backToListButton");
            _finishButton = root.Q<Button>("finishButton");

            PokePress.BindButton(_backButton, _debounce, "back", () =>
            {
                DisarmBackToList(); // 工程を動かしたら身構えを解く（別の意図の操作なので）。
                _placementPress?.Disarm();
                PrevRequested?.Invoke();
            });

            PokePress.BindButton(_nextButton, _debounce, "next", () =>
            {
                DisarmBackToList();
                _placementPress?.Disarm();
                NextRequested?.Invoke();
            });

            PokePress.BindButton(_backToListButton, _debounce, "toList", HandleBackToListPressed);
            PokePress.BindButton(_finishButton, _debounce, "finish", () => FinishRequested?.Invoke());

            // 工程の画像を正方形に保つ（契約の工程画像は 1:1）。USS に aspect-ratio が無いので、
            // 本文の高さが決まった瞬間に同じ値を幅へ入れる。
            _currentSection?.RegisterCallback<GeometryChangedEvent>(_ => LayoutStepImage());

            // P2。「配置」も2度押し（調理の面に並んでいる釦なので、1度では動かさない）。
            _placementPress = new TwoPressButton(
                root.Q<Button>("placementButton"), PlacementLabel, PlacementArmedLabel,
                BackToListConfirmSeconds, PlacementArmedClass);
            _placementPress.Confirmed += () => PlacementRequested?.Invoke();
            PokePress.BindButton(root.Q<Button>("placementButton"), _debounce, "place",
                () => _placementPress.Press());

            DisarmBackToList();
        }

        /// <summary>
        /// 「一覧へ」は**2度押し**（主人の指示。長押しではない）。
        ///
        /// 調理の途中で一覧へ戻るのは、進めていた工程を画面から失う操作にあたる。
        /// 1度目は身構えるだけ（文字が「もう一度」に変わり色が付く）で、
        /// 猶予（<see cref="BackToListConfirmSeconds"/>）の間にもう1度押されたときだけ戻る。
        ///
        /// 長押しにしなかったのは、押し下げ発火（設計 §11 追補）と噛み合わないため——
        /// 手応えの無い板を押せば指は深く入って留まるので、「長押し」が普通の1回押しと区別できない。
        /// </summary>
        private void HandleBackToListPressed()
        {
            if (_backToListArmedUntil > 0f && Time.unscaledTime <= _backToListArmedUntil)
            {
                DisarmBackToList();
                BackToListRequested?.Invoke();
                return;
            }

            _backToListArmedUntil = Time.unscaledTime + BackToListConfirmSeconds;
            if (_backToListButton != null)
            {
                _backToListButton.text = BackToListArmedLabel;
                _backToListButton.AddToClassList(BackToListArmedClass);
            }
        }

        /// <summary>身構えを解く（猶予切れ・別のボタンが押された・板が入れ替わった）。</summary>
        public void DisarmBackToList()
        {
            _backToListArmedUntil = 0f;
            if (_backToListButton != null)
            {
                _backToListButton.text = BackToListLabel;
                _backToListButton.RemoveFromClassList(BackToListArmedClass);
            }
        }

        /// <summary>「一覧へ」が2度目を待っているか（試験用）。</summary>
        public bool IsBackToListArmed => _backToListArmedUntil > 0f && Time.unscaledTime <= _backToListArmedUntil;

        /// <summary>
        /// 工程の画像を正方形にする。一辺は**本文の高さ**（＝板の残り全部）。
        /// ただし本文の幅の <see cref="ImageMaxWidthRatio"/> は越えない——
        /// 文字の大きさを「大」にすると本文が縦に縮むのではなく右の列が要る幅が増えるので、
        /// 画像が右の列を押し潰さないようにここで頭を押さえる。
        /// </summary>
        private void LayoutStepImage()
        {
            if (_currentImage == null || _currentSection == null)
            {
                return;
            }

            var height = _currentSection.resolvedStyle.height;
            var width = _currentSection.resolvedStyle.width;
            if (float.IsNaN(height) || float.IsNaN(width) || height <= 0f || width <= 0f)
            {
                return;
            }

            var side = Mathf.Min(height, width * ImageMaxWidthRatio);

            // 同じ値を入れ直してレイアウトを揺らさない（GeometryChanged が自分で自分を呼ぶのを断つ）。
            if (Mathf.Abs(_currentImage.resolvedStyle.width - side) < 0.5f)
            {
                return;
            }

            _currentImage.style.width = side;
            _currentImage.style.height = side;
        }

        /// <summary>「配置」が2度目を待っているか（試験用）。</summary>
        public bool IsPlacementArmed => _placementPress != null && _placementPress.IsArmed;

        private void Update()
        {
            // 猶予が切れたら黙って元に戻す（押しっぱなしの札を残さない）。
            if (_backToListArmedUntil > 0f && Time.unscaledTime > _backToListArmedUntil)
            {
                DisarmBackToList();
            }

            _placementPress?.Tick();
        }

        private void OnDestroy()
        {
            _imageLoadCts?.Cancel();
            _imageLoadCts?.Dispose();
            ReleaseShownTexture();
        }

        /// <summary>画像の出どころを挿す（Bootstrap から。P3 でサーバに変わってもここは変わらない）。</summary>
        public void Bind(RecipeStore store, string recipeId)
        {
            var changed = _recipeId != recipeId;
            _store = store;
            _recipeId = recipeId;

            if (changed)
            {
                // P3。別のレシピに差し替わった——同じ工程番号でも絵は別物なので、
                // 「今出している絵」の覚えを捨てて必ず読み直させる。
                _imageLoadCts?.Cancel();
                _imageLoadCts?.Dispose();
                _imageLoadCts = null;
                _shownImageKey = null;
                DisarmBackToList();
            }
        }

        public void Refresh(CookSession session)
        {
            RefreshStepDots(session);
            _progressLabel.text = $"{Mathf.RoundToInt((float)session.Progress * 100f)}%";

            var isComplete = session.IsComplete;
            SetHidden(_currentSection, isComplete);
            SetHidden(_completeSection, !isComplete);
            // 戻るは常に可能（設計 §5）。次へは完了後に押しても Domain 側で無視されるだけなので、
            // 工程が1つも無いレシピ（想定外）のときだけ押せなくする。
            _nextButton.SetEnabled(session.Recipe.Steps.Count > 0);

            if (isComplete)
            {
                return;
            }

            var current = session.CurrentStep;
            if (current == null)
            {
                return;
            }

            _currentTitle.text = current.Title;
            _currentInstruction.text = current.Instruction;

            var next = session.NextStep;
            _nextLabel.text = next != null ? $"次: {next.Title}" : "次: —";

            ShowImageOrChip(current);

            // 既にレイアウトが済んでいる板（＝2工程目以降）では GeometryChangedEvent が来ないので、
            // ここでも一度当てる。初回は高さが未確定で、上の登録が受け持つ。
            LayoutStepImage();
        }

        /// <summary>
        /// 工程の数だけ点を出す（設計 §11 追補。v1.0.2 は phase の3つしか出していなかった）。
        /// 今までを塗り、今の工程は大きく。phase の変わり目は点の間隔で見せる。
        /// </summary>
        private void RefreshStepDots(CookSession session)
        {
            _stepDots.Clear();

            string previousPhase = null;
            foreach (var step in session.Recipe.Steps)
            {
                var dot = new VisualElement();
                dot.AddToClassList("step-dot");

                if (session.IsComplete || step.Index < session.Current)
                {
                    dot.AddToClassList("step-dot--done");
                }
                else if (!session.IsComplete && step.Index == session.Current)
                {
                    dot.AddToClassList("step-dot--current");
                }

                if (previousPhase != null && step.PhaseId != previousPhase)
                {
                    dot.AddToClassList("step-dot--phase-start");
                }

                previousPhase = step.PhaseId;
                _stepDots.Add(dot);
            }
        }

        private void ShowImageOrChip(Step step)
        {
            var key = RecipeStore.StepImageKey(step.Index);
            if (_shownImageKey == key)
            {
                return; // 同じ工程を描き直しただけ。読み直さない。
            }

            _shownImageKey = key;
            _imageLoadCts?.Cancel();
            _imageLoadCts?.Dispose();
            _imageLoadCts = null;

            ShowChip(step);

            if (_store == null || string.IsNullOrEmpty(step.Image))
            {
                return; // URL の無い工程は材料名の札のまま（設計 §9）。
            }

            _imageLoadCts = new CancellationTokenSource();
            LoadImageAsync(key, step.Image, _imageLoadCts.Token).Forget();
        }

        private async UniTaskVoid LoadImageAsync(string key, string url, CancellationToken token)
        {
            Texture2D texture;
            try
            {
                texture = await _store.LoadImageAsync(_recipeId, key, url, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested || this == null || texture == null)
            {
                return;
            }

            if (_shownImageKey != key)
            {
                return; // 待っている間に工程が変わった。
            }

            ReleaseShownTexture();
            _shownTexture = texture;
            _currentImage.style.backgroundImage = new StyleBackground(texture);
            _currentIngredientChip.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 画像の代わりの札（設計 §9）。v1.0.6 から画像の**上ではなく説明の下**に出る。
        /// 材料が1つも無い工程は見出しと同じ文字を繰り返すだけなので、何も出さない。
        /// </summary>
        private void ShowChip(Step step)
        {
            ReleaseShownTexture();
            _currentImage.style.backgroundImage = StyleKeyword.Null;

            var text = BuildIngredientChipText(step);
            _currentIngredientChip.text = text;
            _currentIngredientChip.style.display =
                string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void ReleaseShownTexture()
        {
            if (_shownTexture == null)
            {
                return;
            }

            _currentImage.style.backgroundImage = StyleKeyword.Null;
            if (Application.isPlaying)
            {
                Destroy(_shownTexture);
            }
            else
            {
                DestroyImmediate(_shownTexture);
            }

            _shownTexture = null;
        }

        private static string BuildIngredientChipText(Step step)
        {
            if (step.IngredientsUsed.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < step.IngredientsUsed.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(step.IngredientsUsed[i]);
            }

            return sb.ToString();
        }

        private static void SetHidden(VisualElement element, bool hidden)
        {
            if (hidden)
            {
                element.AddToClassList("is-hidden");
            }
            else
            {
                element.RemoveFromClassList("is-hidden");
            }
        }
    }
}
