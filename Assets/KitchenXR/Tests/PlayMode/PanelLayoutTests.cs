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
    /// 「1画面に収まっているか」の検算（2026-09-13 主人の実機確認 v1.0.6）。
    ///
    /// 主人の言葉:
    ///   - レシピの板「スクロールは極力なくしたい。1画面の中に進捗・画像・説明が入っていないと
    ///     毎回スクロールしなきゃいけないのは UX 低下」
    ///   - 材料の板「縦スクロールしないと全部の材料を一目で見れないのは UX 的に…」
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

        /// <summary>契約の上限（工程の説明は 60 文字以内）に合わせた最悪の1文。</summary>
        private const string LongInstruction =
            "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをんあいうえおかきくけこさしすせそた";

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
                "レシピの板に ScrollView が残っています（主人「スクロールは極力なくしたい」）。");

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

            // 説明が潰れていない（60 文字が最低でも3行ぶんの高さで出ている）。
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
        /// 頭（工程の点列と進捗%）は薄い——主人「進捗が画面の上側を結構多く占めている」。
        /// 板の高さの 1/10 を目安にする（190px なら 19px 以内）。
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
        /// 見本の炒飯は材料 13 点。16 点まではスクロール無しで全部見えること
        /// （主人「縦スクロールしないと全部の材料を一目で見れないのは UX 的に…」）。
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

        // ---------------------------------------------------------------- 手のひらメニュー

        /// <summary>
        /// 配置モードの4つの釦（保存・元に戻す・板を手元に・やめる）が板からはみ出さないこと
        /// （設計 §11 追補 2026-09-13。主人「配置の確定等も手元に表示してほしい」）。
        /// 板の寸法は <c>KitchenSceneBuilder.PlacementMenu*Units</c> と同じ 130×92。
        /// はみ出すと、実機で押したい釦が板の外に出て**配置モードから出られなくなる**。
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

                // 設計 §7 の最小 4cm 角（20px）を満たすこと。
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
