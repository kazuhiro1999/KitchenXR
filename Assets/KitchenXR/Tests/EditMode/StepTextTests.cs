using System.Collections.Generic;
using KitchenXR.Domain;
using NUnit.Framework;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 工程の説明のグループ参照を材料名に開く（<see cref="StepText.ExpandGroups"/>）。
    /// 本文は書き換えず、下に添える行だけを作ること。
    /// </summary>
    public class StepTextTests
    {
        private static IReadOnlyList<Ingredient> Seasonings(string group = "B")
        {
            return new List<Ingredient>
            {
                new Ingredient("合びき肉", "300", "g", string.Empty, "主材料"),
                new Ingredient("しょうゆ", "大さじ1", string.Empty, string.Empty, group),
                new Ingredient("みりん", "大さじ1", string.Empty, string.Empty, group),
                new Ingredient("砂糖", "小さじ1", string.Empty, string.Empty, group),
            };
        }

        // ------------------------------------------------------------ 本文は触らない

        [Test]
        public void 本文は書き換えない()
        {
            const string instruction = "合びき肉と(B)を加えて炒めます。";

            var result = StepText.ExpandGroups(instruction, Seasonings());

            Assert.AreEqual(instruction, result.Text, "出典の文が書き換わっています。");
        }

        // ------------------------------------------------------------ 表記の揺れ

        [Test]
        public void 半角の括弧つきの参照を開く()
        {
            var result = StepText.ExpandGroups("合びき肉と(B)を加えて炒めます。", Seasonings());

            Assert.AreEqual(1, result.Notes.Count);
            Assert.AreEqual("(B)＝しょうゆ 大さじ1・みりん 大さじ1・砂糖 小さじ1", result.Notes[0]);
        }

        [Test]
        public void 全角の括弧つきの参照を開く()
        {
            var result = StepText.ExpandGroups("合びき肉と（Ｂ）を加えて炒めます。", Seasonings());

            Assert.AreEqual(1, result.Notes.Count);
            StringAssert.StartsWith("(B)＝しょうゆ", result.Notes[0]);
        }

        [Test]
        public void 角括弧の参照を開く()
        {
            var result = StepText.ExpandGroups("[B]を回しかける。", Seasonings());

            Assert.AreEqual(1, result.Notes.Count);
            StringAssert.StartsWith("(B)＝", result.Notes[0]);
        }

        [Test]
        public void 括弧の無い全角の英字を開く()
        {
            var result = StepText.ExpandGroups("弱火にしてＢを加える。", Seasonings());

            Assert.AreEqual(1, result.Notes.Count);
            StringAssert.StartsWith("(B)＝", result.Notes[0]);
        }

        [Test]
        public void 調味料Bの形を開く()
        {
            var result = StepText.ExpandGroups("調味料Bを加えて煮からめる。", Seasonings());

            Assert.AreEqual(1, result.Notes.Count);
            StringAssert.StartsWith("(B)＝", result.Notes[0]);
        }

        [Test]
        public void Bの材料の形を開く()
        {
            var result = StepText.ExpandGroups("B の材料を合わせておく。", Seasonings());

            Assert.AreEqual(1, result.Notes.Count);
            StringAssert.StartsWith("(B)＝", result.Notes[0]);
        }

        [Test]
        public void 語そのものがグループ名なら開く()
        {
            var result = StepText.ExpandGroups(
                "合わせ調味料を回しかける。", Seasonings("合わせ調味料"));

            Assert.AreEqual(1, result.Notes.Count);
            StringAssert.StartsWith("合わせ調味料＝しょうゆ", result.Notes[0]);
        }

        // ------------------------------------------------------------ 材料側の表記の揺れ

        [Test]
        public void 材料のgroupが括弧つきでも同じグループとして照合する()
        {
            var result = StepText.ExpandGroups("(B)を加える。", Seasonings("(B)"));

            Assert.AreEqual(1, result.Notes.Count);
            StringAssert.StartsWith("(B)＝", result.Notes[0]);
        }

        [Test]
        public void 材料のgroupが全角でも同じグループとして照合する()
        {
            var result = StepText.ExpandGroups("(B)を加える。", Seasonings("Ｂ"));

            Assert.AreEqual(1, result.Notes.Count);
        }

        [Test]
        public void 材料のgroupが調味料Bでも同じグループとして照合する()
        {
            var result = StepText.ExpandGroups("(B)を加える。", Seasonings("調味料B"));

            Assert.AreEqual(1, result.Notes.Count);
        }

        // ------------------------------------------------------------ 複数・重複

        [Test]
        public void 同じ工程に複数のグループがあれば行を分ける()
        {
            var ingredients = new List<Ingredient>
            {
                new Ingredient("しょうゆ", "大さじ1", string.Empty, string.Empty, "A"),
                new Ingredient("酒", "大さじ1", string.Empty, string.Empty, "A"),
                new Ingredient("片栗粉", "小さじ2", string.Empty, string.Empty, "B"),
            };

            var result = StepText.ExpandGroups("(A)をもみ込み、(B)をまぶす。", ingredients);

            Assert.AreEqual(2, result.Notes.Count);
            StringAssert.StartsWith("(A)＝しょうゆ 大さじ1・酒 大さじ1", result.Notes[0]);
            StringAssert.StartsWith("(B)＝片栗粉 小さじ2", result.Notes[1]);
        }

        [Test]
        public void 同じグループが2度出ても行は1本()
        {
            var result = StepText.ExpandGroups("(B)を混ぜ、後から(B)を足す。", Seasonings());

            Assert.AreEqual(1, result.Notes.Count);
        }

        [Test]
        public void 行は本文に出てきた順に並ぶ()
        {
            var ingredients = new List<Ingredient>
            {
                new Ingredient("片栗粉", "小さじ2", string.Empty, string.Empty, "B"),
                new Ingredient("しょうゆ", "大さじ1", string.Empty, string.Empty, "A"),
            };

            var result = StepText.ExpandGroups("(B)をまぶしてから(A)を加える。", ingredients);

            Assert.AreEqual(2, result.Notes.Count);
            StringAssert.StartsWith("(B)＝", result.Notes[0]);
            StringAssert.StartsWith("(A)＝", result.Notes[1]);
        }

        // ------------------------------------------------------------ 該当なし

        [Test]
        public void 参照が無ければ何も添えない()
        {
            var result = StepText.ExpandGroups("豚バラを粗みじん切りにする。", Seasonings());

            Assert.AreEqual(0, result.Notes.Count);
        }

        [Test]
        public void 参照があっても材料側に無ければ何も添えない()
        {
            // 材料は (B) だけ。本文の (C) は空振り。
            var result = StepText.ExpandGroups("(C)を加えて炒めます。", Seasonings());

            Assert.AreEqual(0, result.Notes.Count, "材料に無いグループの行が出ています。");
        }

        [Test]
        public void 材料のgroupが空なら何も添えない()
        {
            var ingredients = new List<Ingredient>
            {
                new Ingredient("しょうゆ", "大さじ1", string.Empty, string.Empty, string.Empty),
                new Ingredient("みりん", "大さじ1", string.Empty, string.Empty, null),
            };

            var result = StepText.ExpandGroups("(B)を加えて炒めます。", ingredients);

            Assert.AreEqual(0, result.Notes.Count);
        }

        [Test]
        public void 材料が空でも落ちない()
        {
            var result = StepText.ExpandGroups("(B)を加えます。", new List<Ingredient>());

            Assert.AreEqual("(B)を加えます。", result.Text);
            Assert.AreEqual(0, result.Notes.Count);
        }

        [Test]
        public void 説明が空でも落ちない()
        {
            var result = StepText.ExpandGroups(null, Seasonings());

            Assert.AreEqual(string.Empty, result.Text);
            Assert.AreEqual(0, result.Notes.Count);
        }

        [Test]
        public void 普通の名詞をグループ参照と取り違えない()
        {
            // group「仕上げ」は在るが、「仕上げに」は参照の表記ではない。
            var ingredients = new List<Ingredient>
            {
                new Ingredient("ごま油", "2", "滴", string.Empty, "仕上げ"),
            };

            var result = StepText.ExpandGroups("仕上げに鍋肌から回しかける。", ingredients);

            Assert.AreEqual(0, result.Notes.Count);
        }

        // ------------------------------------------------------------ 分量の出し方

        [Test]
        public void 分量は数と単位をつなげて名前の後ろに出す()
        {
            var ingredients = new List<Ingredient>
            {
                new Ingredient("水", "100", "ml", string.Empty, "A"),
                new Ingredient("塩", string.Empty, string.Empty, "少々", "A"),
            };

            var result = StepText.ExpandGroups("(A)を注ぐ。", ingredients);

            Assert.AreEqual("(A)＝水 100ml・塩", result.Notes[0],
                "分量の無い材料は名前だけ（prep は添え行に出さない）。");
        }

        // ------------------------------------------------------------ 正規化そのもの

        [Test]
        public void グループ名の正規化はAと括弧つきと全角と語つきを同じ鍵にする()
        {
            Assert.AreEqual("A", StepText.NormalizeGroupKey("A"));
            Assert.AreEqual("A", StepText.NormalizeGroupKey("(A)"));
            Assert.AreEqual("A", StepText.NormalizeGroupKey("（Ａ）"));
            Assert.AreEqual("A", StepText.NormalizeGroupKey("Ａ"));
            Assert.AreEqual("A", StepText.NormalizeGroupKey("調味料A"));
            Assert.AreEqual("A", StepText.NormalizeGroupKey("合わせ調味料Ａ"));
            Assert.AreEqual(string.Empty, StepText.NormalizeGroupKey(null));
            Assert.AreEqual(string.Empty, StepText.NormalizeGroupKey("  "));
        }
    }
}
