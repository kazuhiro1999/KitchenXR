using System.IO;
using KitchenXR.Domain;
using NUnit.Framework;
using UnityEngine;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 見本 <c>Docs/samples/chahan.recipe.json</c>（Resources 配下にコピー済み）の往復を確かめる。
    /// 設計 §3 の見本どおり、9工程・3 phase・材料13 であることが契約 JSON を守れているかの検算になる。
    /// </summary>
    public class RecipeJsonTests
    {
        private static string ChahanJsonPath =>
            Path.Combine(Application.dataPath, "KitchenXR", "Resources", "Recipes", "chahan.json");

        private static Recipe ParseChahan() => RecipeJson.Parse(File.ReadAllText(ChahanJsonPath));

        [Test]
        public void Parse_見本レシピ_タイトルと出典を読める()
        {
            var recipe = ParseChahan();

            Assert.AreEqual("R1", recipe.Id);
            Assert.AreEqual("パラパラ炒飯（基本）", recipe.Title);
            Assert.AreEqual("https://oceans-nadia.com/user/253470/recipe/440737", recipe.SourceUrl);
        }

        [Test]
        public void Parse_見本レシピ_材料13点()
        {
            var recipe = ParseChahan();

            Assert.AreEqual(13, recipe.Ingredients.Count);
        }

        [Test]
        public void Parse_見本レシピ_phaseは3つ()
        {
            var recipe = ParseChahan();

            Assert.AreEqual(3, recipe.Phases.Count);
            CollectionAssert.AreEqual(new[] { "prep", "cook", "finish" },
                new[] { recipe.Phases[0].Id, recipe.Phases[1].Id, recipe.Phases[2].Id });
        }

        [Test]
        public void Parse_見本レシピ_工程は9つで順番どおり()
        {
            var recipe = ParseChahan();

            Assert.AreEqual(9, recipe.Steps.Count);
            for (var i = 0; i < recipe.Steps.Count; i++)
            {
                Assert.AreEqual(i + 1, recipe.Steps[i].Index);
            }
        }

        [Test]
        public void Parse_見本レシピ_completionは既定でmanual()
        {
            var recipe = ParseChahan();

            Assert.IsTrue(recipe.Steps.TrueForAllStepsAreManual());
        }

        [Test]
        public void Parse_未知のキーは無視し欠けたキーは既定値になる()
        {
            const string json = @"{
                ""id"": ""RX"",
                ""title"": ""試験用"",
                ""未知のキー"": ""無視されるはず"",
                ""steps"": [
                    { ""index"": 1, ""phase"": ""prep"", ""title"": ""切る"" }
                ]
            }";

            var recipe = RecipeJson.Parse(json);

            Assert.AreEqual("RX", recipe.Id);
            Assert.AreEqual(0, recipe.Ingredients.Count);
            Assert.AreEqual(0, recipe.Servings);
            Assert.AreEqual(1, recipe.Steps.Count);
            Assert.AreEqual(CompletionType.Manual, recipe.Steps[0].Completion);
            Assert.IsNull(recipe.Steps[0].TimerSec);
            Assert.AreEqual(0, recipe.Steps[0].Tips.Count);
        }
    }

    internal static class StepListTestExtensions
    {
        public static bool TrueForAllStepsAreManual(this System.Collections.Generic.IReadOnlyList<Step> steps)
        {
            foreach (var step in steps)
            {
                if (step.Completion != CompletionType.Manual)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
