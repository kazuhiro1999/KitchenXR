using System.Collections.Generic;
using System.Linq;
using KitchenXR.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 材料パネル（設計 §9・§11 追補）。チェックリスト。今の工程で使う材料を強調する。
    /// チェックの有無はこのセッション内だけの見た目（サーバに送らない。v0 の範囲外）。
    ///
    /// 行は <see cref="Toggle"/> ではなく自前の行にしてある。UI Toolkit の Toggle は
    /// 押し**上げ**で値が変わるので、押し下げで反応させる（§11 追補）と浅く突いて戻したときに
    /// 2回反転して元に戻ってしまう。行そのものを押せる的にすると的も大きくなる（設計 §7）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class IngredientsPanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();
        private readonly Dictionary<string, VisualElement> _rowsByName = new Dictionary<string, VisualElement>();

        private ScrollView _scroll;

        private void Awake()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _scroll = root.Q<ScrollView>("ingredientScroll");
        }

        /// <summary>レシピが決まったときに1度だけ呼ぶ。行を作り直すとチェック状態が消えるので Refresh とは分ける。</summary>
        public void BindRecipe(Recipe recipe)
        {
            _scroll.Clear();
            _rowsByName.Clear();

            var index = 0;
            foreach (var ingredient in recipe.Ingredients)
            {
                _scroll.Add(BuildRow(ingredient, index));
                index++;
            }
        }

        private VisualElement BuildRow(Ingredient ingredient, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("ingredient-row");

            var check = new VisualElement();
            check.AddToClassList("ingredient-row__check");
            row.Add(check);

            var name = new Label(ingredient.Name);
            name.AddToClassList("ingredient-row__name");
            row.Add(name);

            var detail = BuildDetail(ingredient);
            if (!string.IsNullOrEmpty(detail))
            {
                var detailLabel = new Label(detail);
                detailLabel.AddToClassList("ingredient-row__qty");
                row.Add(detailLabel);
            }

            // 押し下げで反応する（設計 §11 追補）。連打の抑止は行ごと。
            PokePress.Bind(row, _debounce, $"ingredient-{index}", () => ToggleRow(row, check));

            // 同じ名前が複数 group に出ることは無い前提（見本レシピどおり）。
            _rowsByName[ingredient.Name] = row;
            return row;
        }

        private static string BuildDetail(Ingredient ingredient)
        {
            var amount = (ingredient.Qty ?? string.Empty) + (ingredient.Unit ?? string.Empty);
            var parts = new[] { amount, ingredient.Prep }
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
            return string.Join("・", parts);
        }

        private static void ToggleRow(VisualElement row, VisualElement check)
        {
            var checkedNow = !row.ClassListContains("ingredient-row--checked");
            SetClass(row, "ingredient-row--checked", checkedNow);
            SetClass(check, "ingredient-row__check--checked", checkedNow);
        }

        /// <summary>今の工程で使う材料の行を強調する。</summary>
        public void Refresh(CookSession session)
        {
            var used = session.CurrentStep?.IngredientsUsed;

            foreach (var pair in _rowsByName)
            {
                var isUsed = used != null && used.Contains(pair.Key);
                SetClass(pair.Value, "ingredient-row--highlight", isUsed);
            }
        }

        private static void SetClass(VisualElement element, string className, bool on)
        {
            if (on)
            {
                element.AddToClassList(className);
            }
            else
            {
                element.RemoveFromClassList(className);
            }
        }
    }
}
