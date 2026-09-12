using System;
using System.IO;
using System.Text;
using KitchenXR.Presentation;
using NUnit.Framework;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 表示の設定の読み書き。肝心なのは壊れていても起動が止まらないこと——設定ファイルは
    /// <c>adb push</c> で触れる場所にあり手で書き換えられる前提なので、空・壊れた JSON・
    /// 知らない段のどれが来ても既定（中・中）に落ちる。
    /// </summary>
    public class DisplaySettingsTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "KitchenXRSettingsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        private DisplaySettings NewSettings() =>
            new DisplaySettings(Path.Combine(_directory, DisplaySettings.FileName));

        private void WriteRaw(string text) =>
            File.WriteAllText(Path.Combine(_directory, DisplaySettings.FileName), text, Encoding.UTF8);

        [Test]
        public void 既定は中と中()
        {
            var settings = NewSettings();
            Assert.AreEqual(DisplayScale.Medium, settings.FontScale);
            Assert.AreEqual(DisplayScale.Medium, settings.PanelScale);
        }

        [Test]
        public void 保存して読み直すと同じ段が戻る()
        {
            var saved = NewSettings();
            saved.FontScale = DisplayScale.Large;
            saved.PanelScale = DisplayScale.Small;
            saved.Save();

            var loaded = NewSettings();
            Assert.IsTrue(loaded.Load(), "保存したのに読めませんでした。");
            Assert.AreEqual(DisplayScale.Large, loaded.FontScale);
            Assert.AreEqual(DisplayScale.Small, loaded.PanelScale);
        }

        [Test]
        public void ファイルが無ければ既定に戻る()
        {
            var settings = NewSettings();
            settings.FontScale = DisplayScale.Large;

            Assert.IsFalse(settings.Load(), "無いファイルを読めたことになっています。");
            Assert.AreEqual(DisplayScale.Medium, settings.FontScale);
            Assert.AreEqual(DisplayScale.Medium, settings.PanelScale);
        }

        [Test]
        public void 壊れたJSONは既定に戻る()
        {
            WriteRaw("{\"font_scale\": \"large\"");  // 閉じ括弧が無い。

            var settings = NewSettings();
            Assert.IsFalse(settings.Load(), "壊れた JSON を読めたことになっています。");
            Assert.AreEqual(DisplayScale.Medium, settings.FontScale);
            Assert.AreEqual(DisplayScale.Medium, settings.PanelScale);
        }

        [Test]
        public void 知らない段は既定に読み替える()
        {
            WriteRaw("{\"font_scale\":\"huge\",\"panel_scale\":7}");

            var settings = NewSettings();
            settings.Load();
            Assert.AreEqual(DisplayScale.Medium, settings.FontScale, "知らない文字列が既定に落ちていません。");
            Assert.AreEqual(DisplayScale.Medium, settings.PanelScale, "数値が既定に落ちていません。");
        }

        [Test]
        public void 片方だけ書かれていても残りは既定()
        {
            WriteRaw("{\"panel_scale\":\"large\"}");

            var settings = NewSettings();
            settings.Load();
            Assert.AreEqual(DisplayScale.Medium, settings.FontScale);
            Assert.AreEqual(DisplayScale.Large, settings.PanelScale);
        }

        [Test]
        public void 壊れた設定を保存し直すと直る()
        {
            WriteRaw("これは JSON ではない");

            var settings = NewSettings();
            settings.Load();
            settings.FontScale = DisplayScale.Small;
            settings.Save();

            var again = NewSettings();
            Assert.IsTrue(again.Load());
            Assert.AreEqual(DisplayScale.Small, again.FontScale);
        }

        // ---------------------------------------------------------------- 当て方（純粋な変換だけ）

        [Test]
        public void 板の縮尺の係数は小中大で1未満と1と1より大()
        {
            Assert.Less(DisplaySettingsApplier.PanelScaleFactor(DisplayScale.Small), 1f);
            Assert.AreEqual(1f, DisplaySettingsApplier.PanelScaleFactor(DisplayScale.Medium), 0.0001f);
            Assert.Greater(DisplaySettingsApplier.PanelScaleFactor(DisplayScale.Large), 1f);
        }

        [Test]
        public void 中は文字のクラスを付けない()
        {
            Assert.IsNull(DisplaySettingsApplier.FontScaleClass(DisplayScale.Medium),
                "中は theme.uss の既定をそのまま使う（クラスを付けない）。");
            Assert.AreEqual(DisplaySettingsApplier.FontScaleSmallClass,
                DisplaySettingsApplier.FontScaleClass(DisplayScale.Small));
            Assert.AreEqual(DisplaySettingsApplier.FontScaleLargeClass,
                DisplaySettingsApplier.FontScaleClass(DisplayScale.Large));
        }
    }
}
