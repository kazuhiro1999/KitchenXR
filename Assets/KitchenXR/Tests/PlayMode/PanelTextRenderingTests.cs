#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace KitchenXR.Tests.PlayMode
{
    /// <summary>
    /// 「文字が1枚も描かれない」の再発を止める検算（2026-09-12 の実機不具合）。
    ///
    /// EditMode ではワールド空間パネルのスタイル解決が走らず `resolvedStyle` が既定値のままなので、
    /// フォントと文字の大きさが本当に効いているかは PlayMode でしか確かめられない。
    /// Kitchen.unity そのものは XR を起こしてしまうため、同じ PanelSettings と UXML で
    /// 板を1枚だけ立てて調べる。
    /// </summary>
    public class PanelTextRenderingTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string RecipeUxmlPath = "Assets/KitchenXR/Presentation/UI/RecipePanel.uxml";

        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_go);
                }
                else
                {
                    Object.DestroyImmediate(_go);
                }

                _go = null;
            }

            yield return null;
        }

        private IEnumerator BuildPanel()
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RecipeUxmlPath);
            Assert.IsNotNull(panelSettings, "KitchenPanelSettings が見つかりません。");
            Assert.IsNotNull(uxml, "RecipePanel.uxml が見つかりません。");

            _go = new GameObject("TestPanel");
            _go.transform.localScale = Vector3.one * 0.2f;

            var document = _go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = uxml;
            document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = new Vector2(260f, 190f);

            // スタイルとレイアウトが落ち着くまで数フレーム回す。
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        private VisualElement Root => _go.GetComponent<UIDocument>().rootVisualElement;

        [UnityTest]
        public IEnumerator 全てのLabelにフォントが解決されている()
        {
            yield return BuildPanel();

            var labels = Root.Query<Label>().ToList();
            Assert.IsNotEmpty(labels, "Label がありません。");

            foreach (var label in labels)
            {
                var definition = label.resolvedStyle.unityFontDefinition;
                Assert.IsTrue(definition.fontAsset != null || definition.font != null,
                    $"Label '{label.name}'（'{label.text}'）にフォントが解決されていません。"
                    + " theme.uss の -unity-font-definition が UI Toolkit 用の FontAsset を指しているか確かめること。");

                Assert.Greater(label.resolvedStyle.color.a, 0.01f, $"Label '{label.name}' の文字色が透明です。");
                Assert.Greater(label.resolvedStyle.fontSize, 0f, $"Label '{label.name}' の font-size が 0 です。");
            }
        }

        [UnityTest]
        public IEnumerator ボタンの文字にもフォントが解決されている()
        {
            yield return BuildPanel();

            foreach (var name in new[] { "nextButton", "backButton" })
            {
                var button = Root.Q<Button>(name);
                Assert.IsNotNull(button, $"Button '{name}' がありません。");

                var definition = button.resolvedStyle.unityFontDefinition;
                Assert.IsTrue(definition.fontAsset != null || definition.font != null,
                    $"Button '{name}' にフォントが解決されていません。");
                Assert.Greater(button.resolvedStyle.color.a, 0.01f);
            }
        }

        [UnityTest]
        public IEnumerator 文字の3段がthemeの大きさになっている()
        {
            yield return BuildPanel();

            // 単位の無い `--font-size-heading: 10` は var() 越しに型が合わず無視され、
            // 既定の 14px に戻ってしまう。theme の値がそのまま出ることを確かめる。
            Assert.AreEqual(10f, Root.Q<Label>("currentTitle").resolvedStyle.fontSize, 0.01f, "見出し");
            Assert.AreEqual(7f, Root.Q<Label>("currentInstruction").resolvedStyle.fontSize, 0.01f, "本文");
            Assert.AreEqual(5f, Root.Q<Label>("progressLabel").resolvedStyle.fontSize, 0.01f, "補助");
        }

        [UnityTest]
        public IEnumerator 板の地の色がthemeどおり白の半透明になっている()
        {
            yield return BuildPanel();

            var panel = Root.Q<VisualElement>("root");
            Assert.IsNotNull(panel);

            var background = panel.resolvedStyle.backgroundColor;
            Assert.AreEqual(1f, background.r, 0.02f);
            Assert.AreEqual(0.85f, background.a, 0.02f, "theme.uss の --color-panel-bg が効いていません。");
        }
    }
}
#endif
