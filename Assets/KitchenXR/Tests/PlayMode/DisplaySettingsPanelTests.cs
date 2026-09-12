#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
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
    /// 表示の設定が**本当に見た目に出る**ことの検算
    /// （2026-09-13 主人「パネルサイズと文字サイズですが、設定とかで変更できたらもっといい」）。
    ///
    /// 設定そのものの読み書きは EditMode（DisplaySettingsTests）。ここで確かめるのは
    ///   - 文字: `.root-panel` に付くクラスが `theme.uss` の `--font-size-*` を本当に上書きするか
    ///     （単位や var() の型が合わずに黙って既定へ戻る、が過去に2度起きている）
    ///   - 板: `localScale` が WorldSpacePanelFactory の値 × 係数になるか
    /// の2つ。どちらも resolvedStyle が要るので PlayMode でしか測れない。
    /// </summary>
    public class DisplaySettingsPanelTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string ListUxmlPath = "Assets/KitchenXR/Presentation/UI/RecipeListPanel.uxml";

        private const float PanelWidthUnits = 260f;
        private const float PanelHeightUnits = 190f;

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

        private IEnumerator BuildPanel()
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ListUxmlPath);
            Assert.IsNotNull(panelSettings, "KitchenPanelSettings が見つかりません。");
            Assert.IsNotNull(uxml, "RecipeListPanel.uxml が見つかりません。");

            _go = new GameObject("SettingsTestPanel");
            _go.SetActive(false);
            WorldSpacePanelFactory.Configure(_go, panelSettings, uxml, PanelWidthUnits, PanelHeightUnits);
            _go.SetActive(true);

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        private VisualElement Root => _go.GetComponent<UIDocument>().rootVisualElement;

        private static IEnumerator Settle()
        {
            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }
        }

        private static DisplaySettings NewSettings(DisplayScale font, DisplayScale panel) =>
            new DisplaySettings(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "KitchenXRPlayTests", System.Guid.NewGuid().ToString("N") + ".json"))
            {
                FontScale = font,
                PanelScale = panel,
            };

        private static IReadOnlyList<RecipeSummary> OneItem() => new List<RecipeSummary>
        {
            new RecipeSummary("chahan", "見本: 炒飯", 10, "見本", null, null, true),
        };

        [UnityTest]
        public IEnumerator 文字を大にすると本文の文字が大きくなる()
        {
            yield return BuildPanel();

            var panel = _go.AddComponent<RecipeListPanel>();
            panel.Show(OneItem(), string.Empty);
            yield return Settle();

            var title = Root.Q<Label>(className: "recipe-card__title");
            Assert.IsNotNull(title, "カードの題名がありません。");

            // 中（＝クラス無し）は theme の本文 6px。
            DisplaySettingsApplier.ApplyTo(NewSettings(DisplayScale.Medium, DisplayScale.Medium), panel);
            yield return Settle();
            Assert.AreEqual(6f, title.resolvedStyle.fontSize, 0.01f, "中のときの本文が theme の値ではありません。");

            DisplaySettingsApplier.ApplyTo(NewSettings(DisplayScale.Large, DisplayScale.Medium), panel);
            yield return Settle();

            var rootPanel = Root.Q<VisualElement>(className: "root-panel");
            Assert.IsTrue(rootPanel.ClassListContains(DisplaySettingsApplier.FontScaleLargeClass),
                "板の根に font-scale--large が付いていません。");
            Assert.AreEqual(7.5f, title.resolvedStyle.fontSize, 0.01f,
                "font-scale--large が --font-size-body を上書きしていません"
                + "（USS の長さに px を付け忘れると var() 越しに黙って無視される）。");

            DisplaySettingsApplier.ApplyTo(NewSettings(DisplayScale.Small, DisplayScale.Medium), panel);
            yield return Settle();
            Assert.IsFalse(rootPanel.ClassListContains(DisplaySettingsApplier.FontScaleLargeClass),
                "前の段のクラスが残っています。");
            Assert.AreEqual(5f, title.resolvedStyle.fontSize, 0.01f, "小のときの本文が合いません。");
        }

        [UnityTest]
        public IEnumerator 板を大にすると縮尺が変わる()
        {
            yield return BuildPanel();

            var panel = _go.AddComponent<RecipeListPanel>();
            yield return null;

            var baseScale = WorldSpacePanelFactory.PanelLocalScale;

            DisplaySettingsApplier.ApplyTo(NewSettings(DisplayScale.Medium, DisplayScale.Medium), panel);
            Assert.AreEqual(baseScale, _go.transform.localScale.x, 0.0001f,
                "中は v1.0.6 までと同じ大きさのはずです。");

            DisplaySettingsApplier.ApplyTo(NewSettings(DisplayScale.Medium, DisplayScale.Large), panel);
            Assert.AreEqual(baseScale * 1.2f, _go.transform.localScale.x, 0.0001f);
            Assert.AreEqual(baseScale * 1.2f, _go.transform.localScale.y, 0.0001f);
            Assert.AreEqual(baseScale * 1.2f, _go.transform.localScale.z, 0.0001f,
                "z も一緒に拡げないとコライダーの奥行きが合わなくなります。");

            DisplaySettingsApplier.ApplyTo(NewSettings(DisplayScale.Medium, DisplayScale.Small), panel);
            Assert.AreEqual(baseScale * 0.85f, _go.transform.localScale.x, 0.0001f);

            yield return null;
        }

        [UnityTest]
        public IEnumerator 設定は一覧と入れ替わりで出る()
        {
            yield return BuildPanel();

            var panel = _go.AddComponent<RecipeListPanel>();
            panel.Show(OneItem(), string.Empty);
            yield return Settle();

            var scroll = Root.Q<ScrollView>("recipeScroll");
            var section = Root.Q<VisualElement>("settingsSection");
            Assert.IsNotNull(section, "設定の区画が UXML にありません。");
            Assert.IsFalse(panel.IsShowingSettings, "最初から設定が出ています。");

            panel.ShowSettings(true);
            yield return Settle();

            Assert.IsTrue(panel.IsShowingSettings);
            Assert.AreEqual(DisplayStyle.None, scroll.resolvedStyle.display,
                "設定を出したのに一覧が残っています（重なって押し間違える）。");
            Assert.AreEqual(DisplayStyle.Flex, section.resolvedStyle.display);

            panel.ShowSettings(false);
            yield return Settle();
            Assert.IsFalse(panel.IsShowingSettings);
            Assert.AreEqual(DisplayStyle.Flex, scroll.resolvedStyle.display);
        }

        [UnityTest]
        public IEnumerator 今の段の釦だけが琥珀になる()
        {
            yield return BuildPanel();

            var panel = _go.AddComponent<RecipeListPanel>();
            yield return null;

            panel.SetDisplaySettings(DisplayScale.Large, DisplayScale.Small);
            yield return Settle();

            Assert.IsTrue(Root.Q<Button>("fontLargeButton").ClassListContains("kitchen-button--primary"),
                "今の文字の段が琥珀になっていません。");
            Assert.IsTrue(Root.Q<Button>("fontMediumButton").ClassListContains("kitchen-button--secondary"),
                "選んでいない段が琥珀のままです。");
            Assert.IsTrue(Root.Q<Button>("panelSmallButton").ClassListContains("kitchen-button--primary"));
            Assert.IsFalse(Root.Q<Button>("panelLargeButton").ClassListContains("kitchen-button--primary"));
        }
    }
}
#endif
