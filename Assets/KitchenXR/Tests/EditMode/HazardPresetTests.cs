using KitchenXR.Presentation.Hazard;
using NUnit.Framework;
using UnityEngine;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 注意の板のプリセット（<c>Resources/Hazards/presets.json</c>）が読めること。
    /// 実機で黙って空になると「注意の板を足す」が押せるのに何も並ばない、が起きる。
    /// </summary>
    public sealed class HazardPresetTests
    {
        [Test]
        public void プリセットがResourcesから読める()
        {
            var catalog = HazardPresetCatalog.LoadFromResources();

            Assert.GreaterOrEqual(catalog.Count, 5,
                "火気注意・熱い・刃物・滑りやすい・電子レンジの5種類は在るはずです。");

            Assert.IsTrue(catalog.TryGet("fire", out var fire), "fire（火気注意）がありません。");
            Assert.AreEqual("火気注意", fire.Title);
            Assert.AreEqual(HazardAccent.Red, fire.Accent, "火気は赤の帯です。");
            Assert.IsNotEmpty(fire.Body, "本文が空だと板が題名だけになります。");
        }

        [Test]
        public void 題名と本文が板に収まる長さである()
        {
            foreach (var preset in HazardPresetCatalog.LoadFromResources().Presets)
            {
                // 板は 100×60 px（20×12cm）。帯の題名は1行・本文は2行までを目安にする。
                Assert.LessOrEqual(preset.Title.Length, 10, $"{preset.Id}: 題名が帯に収まりません。");
                Assert.LessOrEqual(preset.Body.Length, 28, $"{preset.Id}: 本文が板に収まりません。");
            }
        }

        [Test]
        public void 絵文字を使っていない()
        {
            foreach (var preset in HazardPresetCatalog.LoadFromResources().Presets)
            {
                foreach (var c in preset.Mark + preset.Title + preset.Body)
                {
                    // 絵文字は基本多言語面の外（サロゲートペア）に居る。記号か色だけで示す約束。
                    Assert.IsFalse(char.IsSurrogate(c),
                        $"{preset.Id}: 絵文字が混じっています（フォントのアトラスに依るので使わない）。");
                }
            }
        }

        [Test]
        public void 壊れたJSONは空の一覧になる()
        {
            Assert.AreEqual(0, HazardPresetCatalog.Parse("{ これは JSON ではない").Count);
            Assert.AreEqual(0, HazardPresetCatalog.Parse(string.Empty).Count);
            Assert.AreEqual(0, HazardPresetCatalog.Parse("{}").Count);
        }

        [Test]
        public void 題名の無い行は飛ばす()
        {
            var catalog = HazardPresetCatalog.Parse(
                "{\"presets\":[{\"id\":\"a\"},{\"id\":\"b\",\"title\":\"熱い\"},{\"title\":\"鍵なし\"}]}");

            Assert.AreEqual(1, catalog.Count, "id と題名の両方が在る行だけ残るはずです。");
            Assert.IsTrue(catalog.TryGet("b", out _));
        }

        [Test]
        public void 知らない帯の色は琥珀へ落ちる()
        {
            Assert.AreEqual(HazardAccent.Amber, HazardPresetCatalog.ParseAccent("むらさき"));
            Assert.AreEqual(HazardAccent.Amber, HazardPresetCatalog.ParseAccent(null));
            Assert.AreEqual(HazardAccent.Red, HazardPresetCatalog.ParseAccent("red"));
        }

        [Test]
        public void 注意の音のクリップがResourcesにある()
        {
            var clip = Resources.Load<AudioClip>(HazardSound.ClipResourcePath);
            Assert.IsNotNull(clip, $"Resources/{HazardSound.ClipResourcePath} が読めません。");
            Assert.Greater(clip.length, 0.1f, "短すぎて「低い音」に聞こえません。");
            Assert.Less(clip.length, 0.5f, "長すぎます（調理の邪魔になる）。");
        }
    }
}
