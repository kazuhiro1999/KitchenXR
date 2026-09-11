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
    /// 中央に今の工程（見出し・画像・1〜2文・次の工程）、下にボタンの行。
    /// CookSession は持たない——Bootstrap から渡された状態を映すだけ。
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

        public event Action NextRequested;
        public event Action PrevRequested;

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

        private RecipeStore _store;
        private string _recipeId;

        private string _shownImageKey;
        private Texture2D _shownTexture;
        private CancellationTokenSource _imageLoadCts;

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

            PokePress.BindButton(_backButton, _debounce, "back", () => PrevRequested?.Invoke());
            PokePress.BindButton(_nextButton, _debounce, "next", () => NextRequested?.Invoke());
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
            _store = store;
            _recipeId = recipeId;
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

        private void ShowChip(Step step)
        {
            ReleaseShownTexture();
            _currentImage.style.backgroundImage = StyleKeyword.Null;
            _currentIngredientChip.style.display = DisplayStyle.Flex;
            _currentIngredientChip.text = BuildIngredientChipText(step);
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
                return step.Title;
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
