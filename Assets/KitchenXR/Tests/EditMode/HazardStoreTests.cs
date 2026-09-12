using System;
using System.Collections.Generic;
using System.IO;
using KitchenXR.Presentation.Hazard;
using NUnit.Framework;
using UnityEngine;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 控え（<c>zones.json</c>・<c>hazards.json</c>）の往復。壊れたファイルで起動が
    /// 止まらないことも一緒に見る——台所で電源が落ちれば書きかけが残り得る。
    /// </summary>
    public sealed class HazardStoreTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "KitchenXR-Hazard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, true);
                }
            }
            catch (IOException)
            {
                // 掃除に失敗しても結果は変わらない。
            }
        }

        private HazardZoneFile ZoneFile() => new HazardZoneFile(Path.Combine(_directory, HazardZoneFile.FileName));

        private HazardPanelFile PanelFile() =>
            new HazardPanelFile(Path.Combine(_directory, HazardPanelFile.FileName));

        // ---------------------------------------------------------------- 領域

        [Test]
        public void 領域を書いて読み戻せる()
        {
            var file = ZoneFile();
            var zone = new HazardZone(
                "abc", HazardZone.StoveKind, new Vector3(0.2f, 0.88f, 1.3f), 0.62f, 0.51f, 24.5f, 0.88f);

            Assert.IsTrue(file.Save(new[] { zone }));

            var read = ZoneFile().Load();
            Assert.AreEqual(1, read.Count);
            Assert.AreEqual("abc", read[0].Id);
            Assert.AreEqual(HazardZone.StoveKind, read[0].Kind);
            Assert.AreEqual(0.2f, read[0].Center.x, 1e-4f);
            Assert.AreEqual(0.88f, read[0].Center.y, 1e-4f);
            Assert.AreEqual(1.3f, read[0].Center.z, 1e-4f);
            Assert.AreEqual(0.62f, read[0].SizeX, 1e-4f);
            Assert.AreEqual(0.51f, read[0].SizeZ, 1e-4f);
            Assert.AreEqual(24.5f, read[0].YawDegrees, 1e-3f);
            Assert.AreEqual(0.88f, read[0].Height, 1e-4f);
        }

        [Test]
        public void 領域は複数持てる()
        {
            var zones = new List<HazardZone>
            {
                new HazardZone("a", "stove", new Vector3(0f, 0.9f, 1f), 0.6f, 0.5f, 0f, 0.9f),
                new HazardZone("b", "oven", new Vector3(1f, 0.6f, 1f), 0.5f, 0.5f, 15f, 0.6f),
            };

            ZoneFile().Save(zones);

            var read = ZoneFile().Load();
            Assert.AreEqual(2, read.Count);
            Assert.AreEqual("oven", read[1].Kind, "種類（コンロ・オーブン）を分けて持てるはずです。");
        }

        [Test]
        public void ファイルが無ければ空の一覧()
        {
            Assert.IsEmpty(ZoneFile().Load());
            Assert.IsEmpty(PanelFile().Load());
        }

        [Test]
        public void 壊れた領域の控えは空の一覧になる()
        {
            File.WriteAllText(Path.Combine(_directory, HazardZoneFile.FileName), "{ 壊れた");
            Assert.IsEmpty(ZoneFile().Load());
        }

        [Test]
        public void 数でない値と小さすぎる矩形の行は飛ばす()
        {
            File.WriteAllText(Path.Combine(_directory, HazardZoneFile.FileName),
                "{\"zones\":["
                + "{\"id\":\"bad\",\"center\":{\"x\":\"あ\",\"y\":0,\"z\":0},\"size\":{\"x\":1,\"z\":1},"
                + "\"yaw\":0,\"height\":0},"
                + "{\"id\":\"tiny\",\"center\":{\"x\":0,\"y\":0,\"z\":0},\"size\":{\"x\":0.01,\"z\":1},"
                + "\"yaw\":0,\"height\":0},"
                + "{\"id\":\"ok\",\"center\":{\"x\":0,\"y\":0.9,\"z\":0},\"size\":{\"x\":0.6,\"z\":0.5},"
                + "\"yaw\":0,\"height\":0.9}]}");

            var read = ZoneFile().Load();
            Assert.AreEqual(1, read.Count, "読める行だけ残るはずです。");
            Assert.AreEqual("ok", read[0].Id);
        }

        // ---------------------------------------------------------------- 注意の板

        [Test]
        public void 注意の板の鍵と種類を書いて読み戻せる()
        {
            var records = new[]
            {
                new HazardPanelRecord(HazardBoards.KeyPrefix + "1111", "fire"),
                new HazardPanelRecord(HazardBoards.KeyPrefix + "2222", "blade"),
            };

            Assert.IsTrue(PanelFile().Save(records));

            var read = PanelFile().Load();
            Assert.AreEqual(2, read.Count);
            Assert.AreEqual(HazardBoards.KeyPrefix + "1111", read[0].Key);
            Assert.AreEqual("fire", read[0].PresetId);
            Assert.AreEqual("blade", read[1].PresetId);
        }

        [Test]
        public void 鍵か種類が欠けた行は飛ばす()
        {
            File.WriteAllText(Path.Combine(_directory, HazardPanelFile.FileName),
                "{\"panels\":[{\"key\":\"panel.hazard.a\"},{\"preset\":\"fire\"},"
                + "{\"key\":\"panel.hazard.b\",\"preset\":\"hot\"}]}");

            var read = PanelFile().Load();
            Assert.AreEqual(1, read.Count);
            Assert.AreEqual("panel.hazard.b", read[0].Key);
        }

        [Test]
        public void 壊れた注意の板の控えは空の一覧になる()
        {
            File.WriteAllText(Path.Combine(_directory, HazardPanelFile.FileName), "[1,2,3]");
            Assert.IsEmpty(PanelFile().Load());
        }
    }
}
