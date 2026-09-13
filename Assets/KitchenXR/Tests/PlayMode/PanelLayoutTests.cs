#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KitchenXR.Domain;
using KitchenXR.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace KitchenXR.Tests.PlayMode
{
    /// <summary>
    /// 「1画面に収まっているか」の検算。レシピの板は進捗・画像・説明が、材料の板は材料の全部が、
    /// スクロールなしで見えていること。
    ///
    /// 「収まっている」は resolvedStyle でしか分からない（EditMode ではレイアウトが走らない）ので
    /// PlayMode で板を1枚立てて測る。板の寸法は Kitchen.unity と同じ値を使う——
    /// <c>KitchenSceneBuilder</c> の定数を変えたらここも一緒に直すこと。
    /// </summary>
    public class PanelLayoutTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string RecipeUxmlPath = "Assets/KitchenXR/Presentation/UI/RecipePanel.uxml";
        private const string IngredientsUxmlPath = "Assets/KitchenXR/Presentation/UI/IngredientsPanel.uxml";

        // Kitchen.unity と同じ寸法（UI px）。1 UI px ≒ 2mm。
        private const float RecipeWidthUnits = 260f;
        private const float RecipeHeightUnits = 190f;
        private const float IngredientsWidthUnits = 170f;
        private const float IngredientsHeightUnits = 240f;

        private const string PlacementMenuUxmlPath = "Assets/KitchenXR/Presentation/UI/PlacementMenu.uxml";
        private const float PlacementMenuWidthUnits = 130f;
        private const float PlacementMenuHeightUnits = 92f;

        /// <summary>46 文字。以下の「最悪の1文」を数えやすくするための部品。</summary>
        private const string Kana46 =
            "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん";

        /// <summary>契約の上限（工程の説明は 100 文字以内）に合わせた最悪の1文。</summary>
        private const string LongInstruction = Kana46 + Kana46 + "あいうえおかきく";

        /// <summary>上限いっぱいで、なおグループ参照が2つ入っている最悪の1文（92 + 8 = 100 文字）。</summary>
        private const string LongGroupInstruction = Kana46 + Kana46 + "(A)と(B)。";

        private static readonly string RecipeJsonText = @"{
  ""id"": ""layout"",
  ""title"": ""検算"",
  ""phases"": [{""id"": ""cook"", ""title"": ""作る""}],
  ""steps"": [
    {""index"": 1, ""phase"": ""cook"", ""title"": ""とても長い工程の見出しをここに入れる"",
     ""instruction"": """ + LongInstruction + @""",
     ""ingredients_used"": [""卵"", ""ねぎ""]},
    {""index"": 2, ""phase"": ""cook"", ""title"": ""次の工程"", ""instruction"": ""仕上げる。""}
  ]
}";

        /// <summary>100 文字の説明＋グループ参照2つ＋材料の札という、右の列が最も混む組み合わせ。</summary>
        private static readonly string GroupRecipeJsonText = @"{
  ""id"": ""layout-group"",
  ""title"": ""検算"",
  ""ingredients"": [
    {""name"": ""しょうゆ"", ""qty"": ""大さじ1"", ""group"": ""A""},
    {""name"": ""みりん"", ""qty"": ""大さじ1"", ""group"": ""A""},
    {""name"": ""砂糖"", ""qty"": ""小さじ1"", ""group"": ""A""},
    {""name"": ""片栗粉"", ""qty"": ""小さじ2"", ""group"": ""B""},
    {""name"": ""水"", ""qty"": ""100"", ""unit"": ""ml"", ""group"": ""B""}
  ],
  ""phases"": [{""id"": ""cook"", ""title"": ""作る""}],
  ""steps"": [
    {""index"": 1, ""phase"": ""cook"", ""title"": ""とても長い工程の見出し"",
     ""instruction"": """ + LongGroupInstruction + @""",
     ""ingredients_used"": [""しょうゆ"", ""片栗粉""]},
    {""index"": 2, ""phase"": ""cook"", ""title"": ""次の工程"", ""instruction"": ""仕上げる。""}
  ]
}";

        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null)
            {
                Object.Destroy(_go);
                _go = null;
            }

            yield return null;
        }

        private IEnumerator BuildPanel(string uxmlPath, float widthUnits, float heightUnits)
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            Assert.IsNotNull(panelSettings, "KitchenPanelSettings が見つかりません。");
            Assert.IsNotNull(uxml, $"{uxmlPath} が見つかりません。");

            _go = new GameObject("LayoutTestPanel");
            _go.SetActive(false); // UIDocument の OnEnable を配線が終わるまで待たせる。
            WorldSpacePanelFactory.Configure(_go, panelSettings, uxml, widthUnits, heightUnits);
            _go.SetActive(true);

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator Settle()
        {
            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }
        }

        private VisualElement Root => _go.GetComponent<UIDocument>().rootVisualElement;

        // ---------------------------------------------------------------- レシピの板

        [UnityTest]
        public IEnumerator レシピの板は工程の中身をスクロール無しで1画面に収める()
        {
            yield return BuildPanel(RecipeUxmlPath, RecipeWidthUnits, RecipeHeightUnits);

            var panel = _go.AddComponent<RecipePanel>();
            panel.Refresh(new CookSession(RecipeJson.Parse(RecipeJsonText)));

            yield return Settle();

            Assert.IsNull(Root.Q<ScrollView>(),
                "レシピの板に ScrollView が残っています（1画面に収める作りです）。");

            var content = Root.Q<VisualElement>("contentArea");
            var section = Root.Q<VisualElement>("currentSection");
            var text = Root.Q<VisualElement>("currentText");
            var instruction = Root.Q<Label>("currentInstruction");
            var nextLabel = Root.Q<Label>("nextLabel");
            Assert.IsNotNull(content);
            Assert.IsNotNull(section);
            Assert.IsNotNull(text);

            // 右の列の中身が列からはみ出していない（layout は親から見た px の矩形。
            // worldBound は板のローカル単位＝px÷100 なので使わない）。
            Assert.LessOrEqual(instruction.layout.yMax, text.layout.height + 0.5f,
                "60 文字の説明が右の列からはみ出しています。");
            Assert.LessOrEqual(nextLabel.layout.yMax, text.layout.height + 0.5f,
                "「次: …」が右の列からはみ出しています。");
            Assert.LessOrEqual(text.layout.height, content.layout.height + 0.5f,
                "本文が枠からはみ出しています。");

            // 添え行の無い 100 文字は素の 6px のまま入る（実測）。落とすのは添え行が付いたとき。
            Assert.AreEqual(0, panel.TextDensity,
                $"添え行の無い 100 文字で文字を落としています（{instruction.resolvedStyle.fontSize}px）。");

            // 説明が潰れていない（最低でも3行ぶんの高さで出ている）。
            var lineHeight = instruction.resolvedStyle.fontSize * 1.4f;
            Assert.GreaterOrEqual(instruction.layout.height, lineHeight * 3f,
                $"説明が3行ぶんも出ていません（{instruction.layout.height}px）。");
        }

        [UnityTest]
        public IEnumerator レシピの板は左に正方形の画像右に説明を置く()
        {
            yield return BuildPanel(RecipeUxmlPath, RecipeWidthUnits, RecipeHeightUnits);

            var panel = _go.AddComponent<RecipePanel>();
            panel.Refresh(new CookSession(RecipeJson.Parse(RecipeJsonText)));

            yield return Settle();

            var image = Root.Q<VisualElement>("currentImage");
            var text = Root.Q<VisualElement>("currentText");
            Assert.IsNotNull(image, "工程の画像の場所がありません。");
            Assert.IsNotNull(text, "説明の列がありません。");

            // layout は親（currentSection）から見た px の矩形。worldBound は板のローカル単位なので使わない。
            var imageRect = image.layout;
            var textRect = text.layout;

            Assert.Greater(imageRect.width, 20f, "画像が潰れています。");
            Assert.AreEqual(imageRect.width, imageRect.height, 1f,
                "工程の画像が正方形になっていません（契約の工程画像は 1:1）。");
            Assert.GreaterOrEqual(textRect.xMin, imageRect.xMax - 0.5f,
                "説明が画像の上に重なっています（左右に分けていない）。");
            Assert.Greater(textRect.width, 40f, "説明の列が狭すぎます。");
        }

        /// <summary>
        /// 頭（工程の点列と進捗%）は薄い。板の高さの 1/10 を目安にする（190px なら 19px 以内）。
        /// </summary>
        [UnityTest]
        public IEnumerator レシピの板の頭は薄い()
        {
            yield return BuildPanel(RecipeUxmlPath, RecipeWidthUnits, RecipeHeightUnits);

            var panel = _go.AddComponent<RecipePanel>();
            panel.Refresh(new CookSession(RecipeJson.Parse(RecipeJsonText)));

            yield return Settle();

            var header = Root.Q<VisualElement>("header");
            Assert.IsNotNull(header);
            Assert.LessOrEqual(header.resolvedStyle.height, RecipeHeightUnits / 10f,
                "頭が板の 1/10 より厚いままです。");

            // 「一覧へ」「配置」は下のボタンの行へ移した（頭を薄くするため）。
            var buttonRow = Root.Q<VisualElement>("buttonRow");
            foreach (var name in new[] { "backToListButton", "placementButton", "backButton", "nextButton" })
            {
                var button = Root.Q<Button>(name);
                Assert.IsNotNull(button, $"Button '{name}' がありません。");
                Assert.IsTrue(IsDescendantOf(button, buttonRow),
                    $"Button '{name}' が下のボタンの行にありません。");
            }
        }

        /// <summary>
        /// 説明の上限を 60 → 100 文字に緩めた（Docs/MANOR.md §4）。最悪の場合——100 文字の説明に
        /// グループの添え行が2本——でも右の列に収まること。板は置き場所を覚える対象なので
        /// 広げられず、説明を切り詰めるのも設計で禁じているので、収まらなければ
        /// <see cref="RecipePanel"/> が右の列の文字を一段（それでも駄目ならもう一段）落とす。
        /// </summary>
        [UnityTest]
        public IEnumerator レシピの板は100文字の説明とグループの添え行2本を右の列に収める()
        {
            yield return BuildPanel(RecipeUxmlPath, RecipeWidthUnits, RecipeHeightUnits);

            var panel = _go.AddComponent<RecipePanel>();
            panel.Refresh(new CookSession(RecipeJson.Parse(GroupRecipeJsonText)));

            yield return Settle();

            var text = Root.Q<VisualElement>("currentText");
            var instruction = Root.Q<Label>("currentInstruction");
            var notes = Root.Q<Label>("groupNotes");
            var nextLabel = Root.Q<Label>("nextLabel");
            Assert.IsNotNull(notes, "グループの添え行の置き場がありません。");

            // 添え行が2本出ている（(A) と (B)）。
            Assert.AreNotEqual(DisplayStyle.None, notes.resolvedStyle.display,
                "グループの添え行が畳まれたままです。");
            Assert.AreEqual(2, notes.text.Split('\n').Length,
                $"添え行が2本になっていません: 「{notes.text}」");
            StringAssert.Contains("(A)＝", notes.text);
            StringAssert.Contains("(B)＝", notes.text);

            // 本文は出典のまま（開いた材料名を混ぜない）。
            Assert.AreEqual(LongGroupInstruction, instruction.text,
                "説明の本文が書き換わっています。");

            // 説明・添え行・「次: …」のどれも右の列からはみ出していない。
            Assert.LessOrEqual(instruction.layout.yMax, text.layout.height + 0.5f,
                "100 文字の説明が右の列からはみ出しています。");
            Assert.LessOrEqual(notes.layout.yMax, text.layout.height + 0.5f,
                "グループの添え行が右の列からはみ出しています。");
            Assert.LessOrEqual(nextLabel.layout.yMax, text.layout.height + 0.5f,
                "「次: …」が右の列からはみ出しています。");

            // 落とすのは2段まで。3段目が要る＝この寸法では入り切っていない。
            Assert.LessOrEqual(panel.TextDensity, RecipePanel.MaxTextDensity);

            // 落とした後でも読める大きさ（4.5px ≒ 9mm）を下回らない。
            // この最悪の組み合わせは実測で2段目（4.5px）まで使い切る——ここが余白の底。
            Assert.GreaterOrEqual(instruction.resolvedStyle.fontSize, 4.5f - 0.01f,
                $"説明が {instruction.resolvedStyle.fontSize}px まで小さくなっています。");
        }

        /// <summary>
        /// 文字の大きさが「大」（本文 7.5px）でも、100 文字の説明＋添え行が列からはみ出さないこと。
        /// ここは必ず1段は落ちる——落ちなければ逃げ道が働いていない。
        /// </summary>
        [UnityTest]
        public IEnumerator 文字が大きいときは右の列の文字を一段落として収める()
        {
            yield return BuildPanel(RecipeUxmlPath, RecipeWidthUnits, RecipeHeightUnits);

            var panel = _go.AddComponent<RecipePanel>();
            DisplaySettingsApplier.ApplyFontScale(DisplayScale.Large, panel);
            panel.Refresh(new CookSession(RecipeJson.Parse(GroupRecipeJsonText)));

            yield return Settle();

            var text = Root.Q<VisualElement>("currentText");
            var nextLabel = Root.Q<Label>("nextLabel");

            Assert.Greater(panel.TextDensity, 0,
                "文字「大」で 100 文字の説明が入り切っているはずがありません（判定が働いていない）。");
            Assert.LessOrEqual(nextLabel.layout.yMax, text.layout.height + 0.5f,
                $"文字「大」で右の列からはみ出しています（中身 {nextLabel.layout.yMax}px / 列 {text.layout.height}px）。");
        }

        private static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
        {
            for (var e = element.parent; e != null; e = e.parent)
            {
                if (e == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- 材料の板

        /// <summary>
        /// 見本のレシピは材料 13 点。16 点まではスクロール無しで全部見えること。
        /// </summary>
        [UnityTest]
        public IEnumerator 材料の板は16点までスクロール無しで全部見える()
        {
            yield return BuildPanel(IngredientsUxmlPath, IngredientsWidthUnits, IngredientsHeightUnits);

            var panel = _go.AddComponent<IngredientsPanel>();
            panel.BindRecipe(BuildRecipeWithIngredients(16));

            yield return Settle();

            var scroll = Root.Q<ScrollView>("ingredientScroll");
            Assert.IsNotNull(scroll, "材料の ScrollView がありません。");

            var rows = Root.Query<VisualElement>(className: "ingredient-row").ToList();
            Assert.AreEqual(16, rows.Count, "材料の行が並んでいません。");

            var contentHeight = scroll.contentContainer.resolvedStyle.height;
            var viewportHeight = scroll.contentViewport.resolvedStyle.height;

            Assert.LessOrEqual(contentHeight, viewportHeight,
                $"材料 16 点が表示領域に収まっていません（中身 {contentHeight}px / 枠 {viewportHeight}px）。"
                + " theme.uss の .ingredient-row を詰めるか、板をもう少し大きくすること。");
        }

        [UnityTest]
        public IEnumerator 材料の行が詰まっている()
        {
            yield return BuildPanel(IngredientsUxmlPath, IngredientsWidthUnits, IngredientsHeightUnits);

            var panel = _go.AddComponent<IngredientsPanel>();
            panel.BindRecipe(BuildRecipeWithIngredients(4));

            yield return Settle();

            var rows = Root.Query<VisualElement>(className: "ingredient-row").ToList();
            Assert.AreEqual(4, rows.Count);

            // 1行ぶんの送り（次の行の頭 − この行の頭）。10px ＝ 2cm を上限にする。
            var pitch = rows[1].layout.y - rows[0].layout.y;
            Assert.LessOrEqual(pitch, 12f, $"材料の行の送りが {pitch}px あります（詰まっていません）。");
            Assert.Greater(pitch, 3f, "行が重なっています。");
        }

        /// <summary>
        /// <c>ingredients_used</c> を持たないレシピ（manor から取り込んだもの）でも、
        /// 説明に名前が出てくる材料の行が黄色く光ること。
        /// </summary>
        [UnityTest]
        public IEnumerator 使う材料が書かれていなくても説明から拾って行が光る()
        {
            yield return BuildPanel(IngredientsUxmlPath, IngredientsWidthUnits, IngredientsHeightUnits);

            const string json = @"{
  ""id"": ""infer"",
  ""title"": ""取り込み"",
  ""phases"": [{""id"": ""cook"", ""title"": ""作る""}],
  ""ingredients"": [
    {""name"": ""ごはん"", ""qty"": ""300"", ""unit"": ""g""},
    {""name"": ""ごま油"", ""qty"": ""大さじ1""},
    {""name"": ""しょうゆ"", ""qty"": ""小さじ1"", ""group"": ""A""},
    {""name"": ""酒"", ""qty"": ""小さじ1"", ""group"": ""A""}
  ],
  ""steps"": [
    {""index"": 1, ""phase"": ""cook"", ""title"": ""炒める"", ""instruction"": ""ごはんを炒め、(A)を回し入れる。""}
  ]
}";

            var session = new CookSession(RecipeJson.Parse(json));
            var panel = _go.AddComponent<IngredientsPanel>();
            panel.BindRecipe(session.Recipe);
            panel.Refresh(session);

            yield return Settle();

            Assert.IsEmpty(session.Recipe.Steps[0].IngredientsUsed,
                "この検算はレシピ側が空であることが前提です。");

            var rows = Root.Query<VisualElement>(className: "ingredient-row").ToList();
            Assert.AreEqual(4, rows.Count);

            bool Lit(int index) => rows[index].ClassListContains("ingredient-row--highlight");

            Assert.IsTrue(Lit(0), "説明に出てくる「ごはん」が光っていません。");
            Assert.IsTrue(Lit(2), "(A) の「しょうゆ」が光っていません。");
            Assert.IsTrue(Lit(3), "(A) の「酒」が光っていません。");
            Assert.IsFalse(Lit(1), "説明に出てこない「ごま油」まで光っています。");
        }

        // ---------------------------------------------------------------- 手のひらメニュー

        /// <summary>
        /// 配置モードの4つの釦（保存・元に戻す・板を手元に・やめる）が板からはみ出さないこと。
        /// 板の寸法は <c>KitchenSceneBuilder.PlacementMenu*Units</c> と同じ 130×92。
        /// はみ出すと、実機で押したい釦が板の外に出て配置モードから出られなくなる。
        /// </summary>
        [UnityTest]
        public IEnumerator 手のひらメニューは配置の4つの釦を収める()
        {
            yield return BuildPanel(PlacementMenuUxmlPath, PlacementMenuWidthUnits, PlacementMenuHeightUnits);

            var menu = _go.AddComponent<PlacementMenuPanel>();
            yield return Settle();

            menu.SetPlacing(true);
            yield return Settle();

            // worldBound は板のローカル単位（UI px ÷ PixelsPerUnit）で返る。UI px に戻して比べる。
            const float toPixels = WorldSpacePanelFactory.PanelPixelsPerUnit;

            foreach (var name in new[] { "saveButton", "undoButton", "recallButton", "cancelButton" })
            {
                var button = Root.Q<Button>(name);
                Assert.IsNotNull(button, $"手のひらメニューに {name} がありません。");

                var rect = button.worldBound;
                var width = rect.width * toPixels;
                var height = rect.height * toPixels;
                Assert.Greater(width, 0f, $"{name} の幅が 0 です（レイアウトが未確定）。");

                // 最小 4cm 角（20px）を満たすこと。
                Assert.GreaterOrEqual(width, 20f, $"{name} が 4cm より細いです（{width}px）。");
                Assert.GreaterOrEqual(height, 20f, $"{name} が 4cm より低いです（{height}px）。");

                Assert.LessOrEqual(rect.xMax * toPixels, PlacementMenuWidthUnits + 0.5f,
                    $"{name} が板の右へ {rect.xMax * toPixels - PlacementMenuWidthUnits}px はみ出しています。");
                Assert.LessOrEqual(rect.yMax * toPixels, PlacementMenuHeightUnits + 0.5f,
                    $"{name} が板の下へ {rect.yMax * toPixels - PlacementMenuHeightUnits}px はみ出しています。");
            }

            // 調理中は「配置」1つだけ（誤って「保存」を押さないように）。
            menu.SetPlacing(false);
            yield return Settle();

            Assert.AreEqual(DisplayStyle.None,
                Root.Q<VisualElement>(PlacementMenuPanel.PlacingGroupName).resolvedStyle.display,
                "調理中なのに配置の釦が出ています。");
            Assert.AreEqual(DisplayStyle.Flex,
                Root.Q<VisualElement>(PlacementMenuPanel.IdleGroupName).resolvedStyle.display,
                "調理中に「配置」が出ていません。");
        }

        private static Recipe BuildRecipeWithIngredients(int count)
        {
            var ingredients = new List<Ingredient>(count);
            for (var i = 0; i < count; i++)
            {
                ingredients.Add(new Ingredient($"材料{i:00}", "100", "g", "みじん切り", string.Empty));
            }

            return new Recipe("layout", "検算", null, null, 2, 20, ingredients, null, null, null);
        }
    }
}
#endif
