using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// レシピパネル（主。設計 §9）。上に phase の点列と進捗%、中央に今の工程、下に次の工程、
    /// 右下「次へ」左下「戻る」。CookSession は持たない——Bootstrap から渡された状態を映すだけ。
    /// 「次へ／戻る」は 600ms のうちに同じボタンを連打しても1回しか外へ伝えない（設計 §7）。
    /// Domain 側は連打を扱わないので、この抑止は Presentation の責務。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RecipePanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();

        public event Action NextRequested;
        public event Action PrevRequested;

        private VisualElement _phaseDots;
        private Label _progressLabel;
        private VisualElement _currentSection;
        private Label _currentTitle;
        private VisualElement _currentImage;
        private Label _currentIngredientChip;
        private Label _currentInstruction;
        private VisualElement _nextSection;
        private Label _nextLabel;
        private VisualElement _completeSection;
        private Button _backButton;
        private Button _nextButton;

        private string _loadedImageUrl;
        private CancellationTokenSource _imageLoadCts;

        private void Awake()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _phaseDots = root.Q<VisualElement>("phaseDots");
            _progressLabel = root.Q<Label>("progressLabel");
            _currentSection = root.Q<VisualElement>("currentSection");
            _currentTitle = root.Q<Label>("currentTitle");
            _currentImage = root.Q<VisualElement>("currentImage");
            _currentIngredientChip = root.Q<Label>("currentIngredientChip");
            _currentInstruction = root.Q<Label>("currentInstruction");
            _nextSection = root.Q<VisualElement>("nextSection");
            _nextLabel = root.Q<Label>("nextLabel");
            _completeSection = root.Q<VisualElement>("completeSection");
            _backButton = root.Q<Button>("backButton");
            _nextButton = root.Q<Button>("nextButton");

            _backButton.clicked += () => { if (_debounce.TryAccept("back")) PrevRequested?.Invoke(); };
            _nextButton.clicked += () => { if (_debounce.TryAccept("next")) NextRequested?.Invoke(); };

            RegisterPressedVisual(_backButton);
            RegisterPressedVisual(_nextButton);
        }

        private void OnDestroy()
        {
            _imageLoadCts?.Cancel();
            _imageLoadCts?.Dispose();
        }

        /// <summary>poke の押し込み（hysteresis）に合わせた見た目だけの反応。実際の深さ判定は XRPokeFilter 側。</summary>
        private static void RegisterPressedVisual(Button button)
        {
            button.RegisterCallback<PointerDownEvent>(_ => button.AddToClassList("kitchen-button--pressed"));
            button.RegisterCallback<PointerUpEvent>(_ => button.RemoveFromClassList("kitchen-button--pressed"));
            button.RegisterCallback<PointerLeaveEvent>(_ => button.RemoveFromClassList("kitchen-button--pressed"));
        }

        public void Refresh(CookSession session)
        {
            RefreshPhaseDots(session);
            _progressLabel.text = $"{Mathf.RoundToInt((float)session.Progress * 100f)}%";

            var isComplete = session.IsComplete;
            SetHidden(_currentSection, isComplete);
            SetHidden(_nextSection, isComplete);
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
            RefreshImageOrChip(current);

            var next = session.NextStep;
            _nextLabel.text = next != null ? $"次: {next.Title}" : "次: —";
        }

        private void RefreshPhaseDots(CookSession session)
        {
            _phaseDots.Clear();
            foreach (var phase in session.PhaseProgressList())
            {
                var dot = new VisualElement();
                dot.AddToClassList("phase-dot");
                if (phase.IsCurrent)
                {
                    dot.AddToClassList("phase-dot--current");
                }
                else if (phase.IsComplete)
                {
                    dot.AddToClassList("phase-dot--done");
                }

                _phaseDots.Add(dot);
            }
        }

        private void RefreshImageOrChip(Step step)
        {
            if (string.IsNullOrEmpty(step.Image))
            {
                _loadedImageUrl = null;
                _imageLoadCts?.Cancel();
                _currentImage.style.backgroundImage = StyleKeyword.Null;
                _currentIngredientChip.style.display = DisplayStyle.Flex;
                _currentIngredientChip.text = BuildIngredientChipText(step);
                return;
            }

            _currentIngredientChip.style.display = DisplayStyle.None;

            if (_loadedImageUrl == step.Image)
            {
                return; // 同じ工程を再描画しただけなら読み直さない。
            }

            _imageLoadCts?.Cancel();
            _imageLoadCts = new CancellationTokenSource();
            _loadedImageUrl = step.Image;
            LoadImageAsync(step.Image, _imageLoadCts.Token).Forget();
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

        private async UniTaskVoid LoadImageAsync(string url, CancellationToken token)
        {
            using var request = UnityWebRequestTexture.GetTexture(url);
            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested || this == null)
            {
                return;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[KitchenXR] 工程画像の取得に失敗しました: {url} ({request.error})");
                _currentIngredientChip.style.display = DisplayStyle.Flex;
                return;
            }

            var texture = DownloadHandlerTexture.GetContent(request);
            _currentImage.style.backgroundImage = new StyleBackground(texture);
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
