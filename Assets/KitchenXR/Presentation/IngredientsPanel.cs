using System.Collections.Generic;
using System.Linq;
using KitchenXR.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 材料パネル（設計 §9）。チェックリスト。今の工程で使う材料を強調する。
    /// チェックの有無はこのセッション内だけの見た目（サーバに送らない。v0 の範囲外）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class IngredientsPanel : MonoBehaviour
    {
        private ScrollView _scroll;
        private readonly Dictionary<string, VisualElement> _rowsByName = new Dictionary<string, VisualElement>();

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

            foreach (var ingredient in recipe.Ingredients)
            {
                var row = new VisualElement();
                row.AddToClassList("ingredient-row");

                var line = ingredient.Qty + ingredient.Unit;
                var label = string.IsNullOrEmpty(line) ? ingredient.Name : $"{ingredient.Name}（{line}）";

                var toggle = new Toggle(label);
                toggle.AddToClassList("ingredient-toggle");
                row.Add(toggle);

                if (!string.IsNullOrEmpty(ingredient.Prep))
                {
                    var prepLabel = new Label(ingredient.Prep);
                    prepLabel.AddToClassList("ingredient-row__qty");
                    row.Add(prepLabel);
                }

                _scroll.Add(row);

                // 同じ名前が複数 group に出ることは無い前提（見本レシピどおり）。
                _rowsByName[ingredient.Name] = row;
            }
        }

        /// <summary>今の工程で使う材料の行を強調する。</summary>
        public void Refresh(CookSession session)
        {
            var used = session.CurrentStep?.IngredientsUsed;

            foreach (var pair in _rowsByName)
            {
                var isUsed = used != null && used.Contains(pair.Key);
                if (isUsed)
                {
                    pair.Value.AddToClassList("ingredient-row--highlight");
                }
                else
                {
                    pair.Value.RemoveFromClassList("ingredient-row--highlight");
                }
            }
        }
    }
}
