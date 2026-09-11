namespace KitchenXR.Domain
{
    /// <summary>
    /// レシピ一覧の1行（manor の <c>GET /api/v1/kitchen/recipes</c> が返す行。ADR-015 D3・D9追補）。
    ///
    /// 契約の行は `id・title・hero_image・total_minutes・servings・tags・favorite・times_cooked・
    /// last_cooked_at・kcal・category・main_ingredient・cuisine・updated_at` だが、
    /// **一覧の板に出すのは題名・分・分類・kcal の4つだけ**（設計 §9「文字は極力少なく」）。
    /// 残りは持たない——使わないものを運ぶと、契約が動いたときに壊れる面が増える。
    ///
    /// <see cref="Id"/> は文字列。manor 側は int だが、見本（Resources の炒飯）の id は
    /// <c>"chahan"</c> のような文字列で、<see cref="Recipe.Id"/> も文字列なので揃えてある。
    /// </summary>
    public sealed class RecipeSummary
    {
        public string Id { get; }
        public string Title { get; }

        /// <summary>合計の分。0 なら出さない。</summary>
        public int TotalMinutes { get; }

        /// <summary>分類（manor の meta.category。無ければ cuisine → main_ingredient の順で代える）。</summary>
        public string Category { get; }

        /// <summary>1食あたりの kcal。未推定なら null。</summary>
        public double? Kcal { get; }

        /// <summary>一覧の絵（今は使っていないが、選んだ直後の先読みの当てにする）。</summary>
        public string HeroImage { get; }

        /// <summary>アプリに同梱した見本（Resources）か。true なら manor へは問い合わせない。</summary>
        public bool IsBundledSample { get; }

        public RecipeSummary(
            string id, string title, int totalMinutes, string category, double? kcal,
            string heroImage = null, bool isBundledSample = false)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            TotalMinutes = totalMinutes;
            Category = category ?? string.Empty;
            Kcal = kcal;
            HeroImage = heroImage;
            IsBundledSample = isBundledSample;
        }

        /// <summary>板の右側に出す1行（「25分 ・ 中華 ・ 620kcal」）。空の欄は詰める。</summary>
        public string DetailLine()
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (TotalMinutes > 0)
            {
                parts.Add($"{TotalMinutes}分");
            }

            if (!string.IsNullOrEmpty(Category))
            {
                parts.Add(Category);
            }

            if (Kcal.HasValue && Kcal.Value > 0)
            {
                parts.Add($"{System.Math.Round(Kcal.Value)}kcal");
            }

            return string.Join(" ・ ", parts);
        }
    }
}
