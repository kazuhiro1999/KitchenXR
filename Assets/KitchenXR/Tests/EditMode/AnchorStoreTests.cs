using System;
using System.IO;
using System.Text;
using KitchenXR.Platform;
using KitchenXR.Platform.ArFoundation;
using NUnit.Framework;
using UnityEngine;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// P2（アンカーと配置モード）の**落ちない**ことの検算。
    ///
    /// 実機のアンカーそのものは Editor では動かない（XR Simulation は Save／Load／Erase の
    /// どれも非対応。AR Foundation の `simulation-anchors.md` の表）。
    /// だからここで確かめるのは、その周りの「必ず通る道」:
    ///   - 帳簿（`anchors.json`）と控え（`panels.json`）の書き戻し・鍵の上書き
    ///   - **壊れたファイルで落ちない**（読めなければ「無かった」として既定へ落ちる）
    ///   - <see cref="ArAnchorStore"/> は ARAnchorManager が無ければ false／null を返す
    ///     （例外を投げない＝呼び出し側が退避路へ進める）
    /// </summary>
    public class AnchorStoreTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "KitchenXR-P2-" + Guid.NewGuid().ToString("N"));
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
                // 掃除に失敗しても試験の結果は変わらない。
            }
        }

        private string PathFor(string name) => Path.Combine(_directory, name);

        // ---------------------------------------------------------------- anchors.json

        [Test]
        public void アンカーの鍵は保存して読み戻せる()
        {
            var path = PathFor(AnchorGuidFile.FileName);
            var guid = Guid.NewGuid();

            var written = new AnchorGuidFile(path);
            written.Set("panel.recipe", guid);
            Assert.IsTrue(written.Save(), "anchors.json を書けませんでした。");

            var read = new AnchorGuidFile(path);
            read.Load();

            Assert.IsTrue(read.TryGet("panel.recipe", out var restored), "鍵が読み戻せません。");
            Assert.AreEqual(guid, restored);
        }

        [Test]
        public void アンカーの鍵は同じ鍵で上書きされる()
        {
            var path = PathFor(AnchorGuidFile.FileName);
            var older = Guid.NewGuid();
            var newer = Guid.NewGuid();

            var file = new AnchorGuidFile(path);
            file.Set("panel.timer", older);
            file.Set("panel.timer", newer);
            file.Save();

            var read = new AnchorGuidFile(path);
            read.Load();

            Assert.AreEqual(1, read.Guids.Count, "同じ鍵が2つ残っています（板1枚＝鍵1つ。設計 §4.3）。");
            Assert.IsTrue(read.TryGet("panel.timer", out var restored));
            Assert.AreEqual(newer, restored);
        }

        [Test]
        public void 壊れたアンカーの帳簿でも落ちない()
        {
            var path = PathFor(AnchorGuidFile.FileName);
            File.WriteAllText(path, "{ これは JSON ではない", Encoding.UTF8);

            var file = new AnchorGuidFile(path);
            Assert.DoesNotThrow(() => file.Load());
            Assert.IsFalse(file.TryGet("panel.recipe", out _), "壊れたファイルから鍵が出てきました。");
        }

        [Test]
        public void GUIDでない値の鍵は捨てる()
        {
            var path = PathFor(AnchorGuidFile.FileName);
            var good = Guid.NewGuid();
            File.WriteAllText(path,
                "{\"panel.recipe\":\"ぜんぜんGUIDではない\",\"panel.timer\":\"" + good + "\"}", Encoding.UTF8);

            var file = new AnchorGuidFile(path);
            file.Load();

            Assert.IsFalse(file.TryGet("panel.recipe", out _), "GUID でない値を拾っています。");
            Assert.IsTrue(file.TryGet("panel.timer", out var restored), "良い鍵まで捨てています。");
            Assert.AreEqual(good, restored);
        }

        // ---------------------------------------------------------------- panels.json（退避路）

        [Test]
        public void 板の控えは保存して読み戻せる()
        {
            var path = PathFor(PanelPoseFile.FileName);
            var pose = new Pose(new Vector3(0.12f, 1.35f, 1.2f), Quaternion.Euler(0f, 25f, 0f));

            var written = new PanelPoseFile(path);
            written.Set("panel.ingredients", pose);
            Assert.IsTrue(written.Save(), "panels.json を書けませんでした。");

            var read = new PanelPoseFile(path);
            read.Load();

            Assert.IsTrue(read.TryGet("panel.ingredients", out var restored), "控えが読み戻せません。");
            Assert.AreEqual(pose.position.x, restored.position.x, 1e-4f);
            Assert.AreEqual(pose.position.y, restored.position.y, 1e-4f);
            Assert.AreEqual(pose.position.z, restored.position.z, 1e-4f);
            Assert.Less(Quaternion.Angle(pose.rotation, restored.rotation), 0.1f, "向きが変わっています。");
        }

        [Test]
        public void 板の控えは同じ鍵で上書きされる()
        {
            var path = PathFor(PanelPoseFile.FileName);

            var file = new PanelPoseFile(path);
            file.Set("panel.video", new Pose(Vector3.zero, Quaternion.identity));
            file.Set("panel.video", new Pose(new Vector3(1f, 2f, 3f), Quaternion.identity));
            file.Save();

            var read = new PanelPoseFile(path);
            read.Load();

            Assert.AreEqual(1, read.Poses.Count);
            Assert.IsTrue(read.TryGet("panel.video", out var restored));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), restored.position);
        }

        [Test]
        public void 壊れた板の控えでも落ちない()
        {
            var path = PathFor(PanelPoseFile.FileName);
            File.WriteAllText(path, "壊れています", Encoding.UTF8);

            var file = new PanelPoseFile(path);
            Assert.DoesNotThrow(() => file.Load());
            Assert.IsFalse(file.TryGet("panel.recipe", out _));
        }

        [Test]
        public void 数の欠けた控えの行は捨てる()
        {
            var path = PathFor(PanelPoseFile.FileName);

            // px が無い行と、回転が全部 0（長さ 0＝使えない）の行。どちらも既定へ落とす。
            File.WriteAllText(path,
                "{\"panel.recipe\":{\"py\":1,\"pz\":1,\"qx\":0,\"qy\":0,\"qz\":0,\"qw\":1}," +
                "\"panel.timer\":{\"px\":0,\"py\":0,\"pz\":0,\"qx\":0,\"qy\":0,\"qz\":0,\"qw\":0}," +
                "\"panel.video\":{\"px\":1,\"py\":2,\"pz\":3,\"qx\":0,\"qy\":0,\"qz\":0,\"qw\":1}}",
                Encoding.UTF8);

            var file = new PanelPoseFile(path);
            file.Load();

            Assert.IsFalse(file.TryGet("panel.recipe", out _), "数の欠けた行を拾っています。");
            Assert.IsFalse(file.TryGet("panel.timer", out _), "長さ 0 の回転を拾っています。");
            Assert.IsTrue(file.TryGet("panel.video", out _), "良い行まで捨てています。");
        }

        [Test]
        public void 控えを消すとファイルごと無くなる()
        {
            var path = PathFor(PanelPoseFile.FileName);

            var file = new PanelPoseFile(path);
            file.Set("panel.recipe", new Pose(Vector3.one, Quaternion.identity));
            file.Save();
            Assert.IsTrue(File.Exists(path));

            file.Delete();
            Assert.IsFalse(File.Exists(path), "控えが残っています。");
            Assert.AreEqual(0, file.Poses.Count);
        }

        // ---------------------------------------------------------------- ArAnchorStore

        [Test]
        public void マネージャが無ければアンカーの保存はfalseを返す()
        {
            var store = ArAnchorStore.CreateWithoutManager(new AnchorGuidFile(PathFor(AnchorGuidFile.FileName)));

            var saved = store.SaveAsync("panel.recipe", new Pose(Vector3.zero, Quaternion.identity))
                .GetAwaiter().GetResult();

            Assert.IsFalse(saved, "ARAnchorManager が無いのに保存できたことになっています。");
            Assert.IsFalse(File.Exists(PathFor(AnchorGuidFile.FileName)), "保存できていないのに帳簿を書いています。");
        }

        [Test]
        public void マネージャが無ければアンカーの読み出しはnullを返す()
        {
            var store = ArAnchorStore.CreateWithoutManager(new AnchorGuidFile(PathFor(AnchorGuidFile.FileName)));

            var pose = store.LoadAsync("panel.recipe").GetAwaiter().GetResult();

            Assert.IsNull(pose, "ARAnchorManager が無いのに Pose が返りました（退避路へ落ちません）。");
        }

        [Test]
        public void マネージャが無くてもClearは落ちずに帳簿を消す()
        {
            var path = PathFor(AnchorGuidFile.FileName);
            var guidFile = new AnchorGuidFile(path);
            guidFile.Set("panel.recipe", Guid.NewGuid());
            guidFile.Save();

            var store = ArAnchorStore.CreateWithoutManager(new AnchorGuidFile(path));

            Assert.DoesNotThrow(() => store.ClearAsync().GetAwaiter().GetResult());
            Assert.IsFalse(File.Exists(path), "帳簿が残っています。");
        }
    }
}
