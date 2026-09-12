using System.Collections.Generic;

namespace KitchenXR.Domain
{
    /// <summary>
    /// レシピ全体（契約 JSON と1対1）。クライアントは表示に徹し、
    /// 内容の短さの保証はサーバ側の仕事。
    /// </summary>
    public sealed class Recipe
    {
        public string Id { get; }
        public string Title { get; }
        public string SourceUrl { get; }
        public string HeroImage { get; }
        public int Servings { get; }
        public int TotalMinutes { get; }
        public IReadOnlyList<Ingredient> Ingredients { get; }
        public IReadOnlyList<string> Tools { get; }
        public IReadOnlyList<Phase> Phases { get; }
        public IReadOnlyList<Step> Steps { get; }

        public Recipe(
            string id,
            string title,
            string sourceUrl,
            string heroImage,
            int servings,
            int totalMinutes,
            IReadOnlyList<Ingredient> ingredients,
            IReadOnlyList<string> tools,
            IReadOnlyList<Phase> phases,
            IReadOnlyList<Step> steps)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            SourceUrl = sourceUrl;
            HeroImage = heroImage;
            Servings = servings;
            TotalMinutes = totalMinutes;
            Ingredients = ingredients ?? System.Array.Empty<Ingredient>();
            Tools = tools ?? System.Array.Empty<string>();
            Phases = phases ?? System.Array.Empty<Phase>();
            Steps = steps ?? System.Array.Empty<Step>();
        }

        /// <summary>phase の id からタイトルを引く。無ければ id をそのまま返す。</summary>
        public string PhaseTitle(string phaseId)
        {
            foreach (var phase in Phases)
            {
                if (phase.Id == phaseId)
                {
                    return phase.Title;
                }
            }

            return phaseId ?? string.Empty;
        }
    }
}
