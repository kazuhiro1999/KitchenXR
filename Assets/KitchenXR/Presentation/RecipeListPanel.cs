using System;
using System.Collections.Generic;
using KitchenXR.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// レシピを選ぶ板（P3。主人の指示）。起動したら**レシピの板の場所に**これが出る。
    ///
    /// 並べるのは題名・分・分類・kcal の4つだけ（設計 §9「文字は極力少なく」）。
    /// 先頭は必ず「見本: 炒飯」——manor が寝ていても、合言葉が未設定でも、
    /// ここから1本は最後まで進められる。
    ///
    /// 押すのは**行そのもの**（材料の板と同じ流儀。§11 追補 (a)）。
    /// <see cref="Toggle"/> や <see cref="Button"/> より的が大きく、
    /// <see cref="PokePress"/> の押し下げ発火と相性が良い。
    ///
    /// 選んだあとは「準備中 n/m」の覆いを出す——
    /// レシピの JSON と画像を**先に全部**手元へ落としてから調理を始めるので、その間に
    /// 別の行を押されないようにする（設計 §7「押し間違いを防ぐ」）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RecipeListPanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();

        /// <summary>行が選ばれた。受けるのは <see cref="KitchenXR.App.Bootstrap"/>。</summary>
        public event Action<RecipeSummary> RecipeSelected;

        /// <summary>
        /// 「配置」が**2度**押された（P2。設計 §4.4）。
        /// 起動直後はこの板が出ているので、調理を始める前に板を置き直せる入り口がここに要る。
        /// </summary>
        public event Action PlacementRequested;

        private const string PlacementLabel = "配置";
        private const string PlacementArmedLabel = "もう一度";
        private const string PlacementArmedClass = "recipe-place-button--armed";

        /// <summary>「配置」の2度押しの猶予（秒）。レシピの板の「一覧へ」と同じ長さ。</summary>
        public const float PlacementConfirmSeconds = 4f;

        private Label _statusLabel;
        private ScrollView _scroll;
        private VisualElement _busySection;
        private Label _busyLabel;
        private TwoPressButton _placementPress;

        private readonly List<RecipeSummary> _items = new List<RecipeSummary>();

        private void Awake()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _statusLabel = root.Q<Label>("statusLabel");
            _scroll = root.Q<ScrollView>("recipeScroll");
            _busySection = root.Q<VisualElement>("busySection");
            _busyLabel = root.Q<Label>("busyLabel");

            var placementButton = root.Q<Button>("placementButton");
            _placementPress = new TwoPressButton(
                placementButton, PlacementLabel, PlacementArmedLabel,
                PlacementConfirmSeconds, PlacementArmedClass);
            _placementPress.Confirmed += () => PlacementRequested?.Invoke();
            PokePress.BindButton(placementButton, _debounce, "place", () => _placementPress.Press());
        }

        /// <summary>「配置」が2度目を待っているか（試験用）。</summary>
        public bool IsPlacementArmed => _placementPress != null && _placementPress.IsArmed;

        private void Update() => _placementPress?.Tick();

        /// <summary>今並んでいる行（試験と Bootstrap の確認用）。</summary>
        public IReadOnlyList<RecipeSummary> Items => _items;

        /// <summary>準備中の覆いが出ているか。</summary>
        public bool IsBusy => _busySection != null && !_busySection.ClassListContains("is-hidden");

        /// <summary>
        /// 一覧を並べ直す。<paramref name="status"/> は板の上の小さな札
        /// （「manor 未設定（見本だけ）」「控えた一覧（manor に繋がりません）」など）。
        /// </summary>
        public void Show(IReadOnlyList<RecipeSummary> items, string status)
        {
            _items.Clear();
            _scroll.Clear();

            if (_statusLabel != null)
            {
                _statusLabel.text = status ?? string.Empty;
            }

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null || _items.Count >= RecipeListJson.MaxItems)
                    {
                        continue;
                    }

                    _items.Add(item);
                    _scroll.Add(BuildRow(item));
                }
            }

            if (_items.Count == 0)
            {
                var empty = new Label("レシピがありません");
                empty.AddToClassList("recipe-list-empty");
                _scroll.Add(empty);
            }

            HideBusy();
        }

        private VisualElement BuildRow(RecipeSummary summary)
        {
            var row = new VisualElement();
            row.AddToClassList("recipe-row");
            if (summary.IsBundledSample)
            {
                row.AddToClassList("recipe-row--sample");
            }

            var title = new Label(summary.Title);
            title.AddToClassList("recipe-row__title");
            row.Add(title);

            var detail = summary.DetailLine();
            if (!string.IsNullOrEmpty(detail))
            {
                var detailLabel = new Label(detail);
                detailLabel.AddToClassList("recipe-row__detail");
                row.Add(detailLabel);
            }

            // 鍵はレシピ id（別の行を続けて押すのは誤操作ではないので、行ごとに 600ms を数える）。
            PokePress.BindButton(row, _debounce, $"recipe:{summary.Id}", () =>
            {
                if (IsBusy)
                {
                    return; // 準備中は受けない（覆いが出ているので普通は届かないが、念のため）。
                }

                RecipeSelected?.Invoke(summary);
            });

            return row;
        }

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
