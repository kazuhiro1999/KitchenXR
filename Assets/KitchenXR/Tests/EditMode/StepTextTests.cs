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

        // ------------------------------------------------------------ ingredients_used の推定

        /// <summary>
        /// manor から取り込んだレシピは <c>ingredients_used</c> が空なので、
        /// 材料の板が1行も光らない。空のときは説明から拾う。
        /// </summary>
        private static IReadOnlyList<Ingredient> Chahan() => new List<Ingredient>
        {
            new Ingredient("ごはん", "300", "g", string.Empty, string.Empty),
            new Ingredient("卵", "2", "個", string.Empty, string.Empty),
            new Ingredient("長ねぎ（青い部分）", "1/2", "本", "みじん切り", string.Empty),
            new Ingredient("ごま油", "大さじ1", string.Empty, string.Empty, string.Empty),
            new Ingredient("しょうゆ", "小さじ1", string.Empty, string.Empty, "A"),
            new Ingredient("酒", "小さじ1", string.Empty, string.Empty, "A"),
        };

        [Test]
        public void 説明に出てくる材料名を拾う()
        {
            var used = StepText.InferIngredientsUsed("ごはんと卵を混ぜる。", Chahan());

            CollectionAssert.AreEqual(new[] { "ごはん", "卵" }, used,
                "材料の並び順で返らないと、札の並びが工程ごとに変わります。");
        }

        [Test]
        public void 材料名の括弧の添え書きは落として照合する()
        {
            var used = StepText.InferIngredientsUsed("長ねぎを散らす。", Chahan());

            CollectionAssert.AreEqual(new[] { "長ねぎ（青い部分）" }, used,
                "名前は材料に書かれたまま返す（板の行の鍵に使うため）。");
        }

        [Test]
        public void グループ参照はその組の材料を全部拾う()
        {
            var used = StepText.InferIngredientsUsed("(A)を回し入れる。", Chahan());

            CollectionAssert.AreEqual(new[] { "しょうゆ", "酒" }, used);
        }

        [Test]
        public void グループ参照と名前は一緒に拾う()
        {
            var used = StepText.InferIngredientsUsed("ごはんを炒め、(A)を回し入れる。", Chahan());

            CollectionAssert.AreEqual(new[] { "ごはん", "しょうゆ", "酒" }, used);
        }

        [Test]
        public void 長い名前が先に当たるので短い名前は誤爆しない()
        {
            var ingredients = new List<Ingredient>
            {
                new Ingredient("ごま油", "大さじ1", string.Empty, string.Empty, string.Empty),
                new Ingredient("油", "適量", string.Empty, string.Empty, string.Empty),
            };

            var used = StepText.InferIngredientsUsed("ごま油を熱する。", ingredients);

            CollectionAssert.AreEqual(new[] { "ごま油" }, used,
                "「ごま油」の中の「油」まで拾っています。");
        }

        [Test]
        public void 短い名前は複合語の中では拾わない()
        {
            var ingredients = new List<Ingredient>
            {
                new Ingredient("油", "適量", string.Empty, string.Empty, string.Empty),
                new Ingredient("水", "100", "ml", string.Empty, string.Empty),
            };

            Assert.IsEmpty(StepText.InferIngredientsUsed("ごま油を熱する。", ingredients),
                "「ごま油」に「油」が当たっています。");
            Assert.IsEmpty(StepText.InferIngredientsUsed("油揚げを刻む。", ingredients),
                "「油揚げ」に「油」が当たっています。");
            Assert.IsEmpty(StepText.InferIngredientsUsed("水菜を添える。", ingredients),
                "「水菜」に「水」が当たっています。");
        }

        [Test]
        public void 短い名前も助詞の前後なら拾う()
        {
            var ingredients = new List<Ingredient>
            {
                new Ingredient("油", "適量", string.Empty, string.Empty, string.Empty),
            };

            CollectionAssert.AreEqual(new[] { "油" },
                StepText.InferIngredientsUsed("フライパンに油をひく。", ingredients));
        }

        [Test]
        public void 長い名前は部分一致でも拾う()
        {
            var ingredients = new List<Ingredient>
            {
                new Ingredient("合びき肉", "300", "g", string.Empty, string.Empty),
            };

            CollectionAssert.AreEqual(new[] { "合びき肉" },
                StepText.InferIngredientsUsed("合びき肉をほぐしながら炒める。", ingredients));
        }

        [Test]
        public void 全角と空白の揺れを吸収する()
        {
            var ingredients = new List<Ingredient>
            {
                new Ingredient("バター", "10", "g", string.Empty, string.Empty),
            };

            CollectionAssert.AreEqual(new[] { "バター" },
                StepText.InferIngredientsUsed("バ タ ー を落とす。", ingredients));
        }

        [Test]
        public void 材料が出てこなければ空()
        {
            Assert.IsEmpty(StepText.InferIngredientsUsed("弱火で5分そのまま置く。", Chahan()));
            Assert.IsEmpty(StepText.InferIngredientsUsed(string.Empty, Chahan()));
            Assert.IsEmpty(StepText.InferIngredientsUsed(null, Chahan()));
            Assert.IsEmpty(StepText.InferIngredientsUsed("ごはんを炒める。", null));
        }
    }
}
