using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace KitchenXR.Domain
{
    /// <summary>
    /// 契約 JSON（設計 §3）を <see cref="Recipe"/> に直す。
    /// 未知のキーは無視し、欠けているキーは既定値で埋める
    /// （サーバ側の構造化がまだ荒くても Unity 側は落ちない。設計 §3 の方針）。
    /// </summary>
    public static class RecipeJson
    {
        public static Recipe Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("json が空です。", nameof(json));
            }

            // MissingMemberHandling.Ignore が既定なので、DTO に無いキーは自動で無視される。
            var dto = JsonConvert.DeserializeObject<RecipeDto>(json)
                      ?? throw new InvalidOperationException("レシピ JSON の解析結果が null でした。");

            var ingredients = (dto.Ingredients ?? new List<IngredientDto>())
                .Select(i => new Ingredient(i.Name, i.Qty, i.Unit, i.Prep, i.Group))
                .ToList();

            var phases = (dto.Phases ?? new List<PhaseDto>())
                .Select(p => new Phase(p.Id, p.Title))
                .ToList();

            var steps = (dto.Steps ?? new List<StepDto>())
                .OrderBy(s => s.Index)
                .Select(s => new Step(
                    s.Index,
                    s.Phase,
                    s.Title,
                    s.Instruction,
                    s.Image,
                    s.IngredientsUsed ?? new List<string>(),
                    s.TimerSec,
                    ParseCompletion(s.Completion),
                    s.Tips ?? new List<string>()))
                .ToList();

            return new Recipe(
                dto.Id,
                dto.Title,
                dto.SourceUrl,
                dto.HeroImage,
                dto.Servings,
                dto.TotalMinutes,
                ingredients,
                dto.Tools ?? new List<string>(),
                phases,
                steps);
        }

        private static CompletionType ParseCompletion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return CompletionType.Manual;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "auto":
                    return CompletionType.Auto;
                case "confirm":
                    return CompletionType.Confirm;
                case "manual":
                    return CompletionType.Manual;
                default:
                    // 未知の値は manual 扱い（v0 は全部 manual なので安全側）。
                    return CompletionType.Manual;
            }
        }

        // --- 契約 JSON をそのまま映す DTO（snake_case）。ドメイン型とは分ける ---

        private sealed class RecipeDto
        {
            public string Id;
            public string Title;

            [JsonProperty("source_url")]
            public string SourceUrl;

            [JsonProperty("hero_image")]
            public string HeroImage;

            public int Servings;

            [JsonProperty("total_minutes")]
            public int TotalMinutes;

            public List<IngredientDto> Ingredients;
            public List<string> Tools;
            public List<PhaseDto> Phases;
            public List<StepDto> Steps;
        }

        private sealed class IngredientDto
        {
            public string Name;
            public string Qty;
            public string Unit;
            public string Prep;
            public string Group;
        }

        private sealed class PhaseDto
        {
            public string Id;
            public string Title;
        }

        private sealed class StepDto
        {
            public int Index;
            public string Phase;
            public string Title;
            public string Instruction;
            public string Image;

            [JsonProperty("ingredients_used")]
            public List<string> IngredientsUsed;

            [JsonProperty("timer_sec")]
            public int? TimerSec;

            public string Completion;
            public List<string> Tips;
        }
    }
}
