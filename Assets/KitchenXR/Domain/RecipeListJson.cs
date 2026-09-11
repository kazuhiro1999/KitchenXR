using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KitchenXR.Domain
{
    /// <summary>
    /// レシピ一覧の JSON（manor の <c>GET /api/v1/kitchen/recipes</c> → <c>{"items": [...]}</c>）を
    /// <see cref="RecipeSummary"/> の並びに直す。
    ///
    /// <see cref="RecipeJson"/> と同じ流儀で **壊れていても落ちない**:
    /// 読めなかった行は黙って飛ばし、読めた行だけを返す。全部読めなければ空の並び。
    /// 一覧が出ないことで調理そのものが止まってはいけない（オフライン前提。設計 §11 追補）。
    /// </summary>
    public static class RecipeListJson
    {
        /// <summary>板に並べる上限（主人の指示。20 件・縦スクロール）。</summary>
        public const int MaxItems = 20;

        public static IReadOnlyList<RecipeSummary> Parse(string json)
        {
            var result = new List<RecipeSummary>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            JToken root;
            try
            {
                root = JToken.Parse(json);
            }
            catch (JsonException)
            {
                return result;
            }

            // `{"items": [...]}` が正だが、素の配列で返ってきても読めるようにしておく。
            var items = root as JArray ?? root["items"] as JArray;
            if (items == null)
            {
                return result;
            }

            foreach (var item in items)
            {
                if (!(item is JObject obj))
                {
                    continue;
                }

                var summary = ParseOne(obj);
                if (summary != null)
                {
                    result.Add(summary);
                }

                if (result.Count >= MaxItems)
                {
                    break;
                }
            }

            return result;
        }

        private static RecipeSummary ParseOne(JObject obj)
        {
            var id = Text(obj["id"]);
            var title = Text(obj["title"]);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title))
            {
                return null; // 題名か id の無い行は出しようがない。
            }

            // 分類は category を第一とし、無ければ cuisine → main_ingredient で代える
            // （manor は3つとも任意。どれも空なら分類の欄を出さない）。
            var category = Text(obj["category"]);
            if (string.IsNullOrEmpty(category))
            {
                category = Text(obj["cuisine"]);
            }

            if (string.IsNullOrEmpty(category))
            {
                category = Text(obj["main_ingredient"]);
            }

            return new RecipeSummary(
                id, title, Int(obj["total_minutes"]), category, Number(obj["kcal"]), Text(obj["hero_image"]));
        }

        private static string Text(JToken token) =>
            token == null || token.Type == JTokenType.Null ? string.Empty : token.ToString();

        private static int Int(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0;
            }

            return int.TryParse(token.ToString(), out var value) ? value : 0;
        }

        private static double? Number(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            return double.TryParse(
                token.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : (double?)null;
        }
    }
}
