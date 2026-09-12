using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Net;
using NUnit.Framework;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 動画の一覧の保管庫（設計 §6・ROADMAP P4）。
    /// 「初回だけ同梱の見本を写し、以後は手元のものだけを読む」——主人が PC から
    /// `persistentDataPath/media.json` を差し替えれば、アプリを入れ直さずに一覧が変わる
    /// （書き方は `Docs/media-json.md`）。
    /// </summary>
    public class MediaStoreTests
    {
        private string _root;

        private string LocalPath => Path.Combine(_root, MediaStore.FileName);

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "KitchenXRMediaTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private MediaStore Create(FakeBundledTextReader reader) =>
            new MediaStore(LocalPath, "bundled://media.json", reader);

        [Test]
        public void 初回は同梱の見本を手元へ写してから読む()
        {
            var reader = new FakeBundledTextReader(@"[{""title"":""見本"",""video_id"":""abcdefghijk""}]");
            var store = Create(reader);

            var items = store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("見本", items[0].Title);
            Assert.IsTrue(File.Exists(LocalPath), "同梱の見本が手元へ写っていません。");
            Assert.AreEqual(1, reader.ReadCount);
        }

        [Test]
        public void 端末側を直していれば同梱より手元が勝つ()
        {
            var reader = new FakeBundledTextReader(@"[{""title"":""見本"",""video_id"":""abcdefghijk""}]");
            var store = Create(reader);
            store.LoadAsync().GetAwaiter().GetResult(); // ここで控えができる。

            // 主人が adb で端末側を書き換えた、の想定。
            File.WriteAllText(LocalPath, @"[{""title"":""主人が入れたもの"",""video_id"":""ZZZZZZZZZZZ""}]");

            var items = store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual("主人が入れたもの", items[0].Title, "端末側で直したものが同梱に上書きされました。");
            Assert.IsTrue(File.Exists(store.BundledCopyPath), "同梱の控えが作られていません。");
        }

        [Test]
        public void 同梱も手元も無ければ空の一覧を返す()
        {
            var store = Create(new FakeBundledTextReader(null));

            var items = store.LoadAsync().GetAwaiter().GetResult();

            Assert.IsEmpty(items, "読めないときは空の一覧（板は「一覧がありません」を出す）。");
        }

        [Test]
        public void 手元のものが壊れていても落ちない()
        {
            File.WriteAllText(LocalPath, "{ こわれている");
            var store = Create(new FakeBundledTextReader(null));

            var items = store.LoadAsync().GetAwaiter().GetResult();

            Assert.IsEmpty(items);
        }

        private sealed class FakeBundledTextReader : IBundledTextReader
        {
            private readonly string _content;

            public FakeBundledTextReader(string content)
            {
                _content = content;
            }

            public int ReadCount { get; private set; }

            public UniTask<string> ReadAsync(string path, CancellationToken token)
            {
                ReadCount++;
                return UniTask.FromResult(_content);
            }
        }

        [Test]
        public void 同梱が新しくなり端末側が手つかずなら入れ替える()
        {
            var first = new FakeBundledTextReader(@"[{""title"":""見本"",""video_id"":""abcdefghijk""}]");
            Create(first).LoadAsync().GetAwaiter().GetResult();

            var second = new FakeBundledTextReader(@"[{""title"":""新しい見本"",""video_id"":""bbbbbbbbbbb""}]");
            var items = Create(second).LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual("新しい見本", items[0].Title, "APK を入れ替えたのに古い一覧のままです（主人の 2026-09-13 の指摘）。");
        }

        [Test]
        public void 同梱が新しくなっても端末側を直していれば端末側を守る()
        {
            var first = new FakeBundledTextReader(@"[{""title"":""見本"",""video_id"":""abcdefghijk""}]");
            Create(first).LoadAsync().GetAwaiter().GetResult();
            File.WriteAllText(LocalPath, @"[{""title"":""主人が入れたもの"",""video_id"":""ZZZZZZZZZZZ""}]");

            var second = new FakeBundledTextReader(@"[{""title"":""新しい見本"",""video_id"":""bbbbbbbbbbb""}]");
            var items = Create(second).LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual("主人が入れたもの", items[0].Title, "端末側で直したものが同梱に上書きされました。");
        }

        [Test]
        public void manorの一覧を写すと以後はそれを読み同梱が変わっても守られる()
        {
            var reader = new FakeBundledTextReader(@"[{""title"":""見本"",""video_id"":""abcdefghijk""}]");
            var store = Create(reader);
            store.LoadAsync().GetAwaiter().GetResult();

            var remote = store.SaveRemote(@"{""items"":[{""title"":""manor の一覧"",""video_id"":""mmmmmmmmmmm""}]}");
            Assert.AreEqual("manor の一覧", remote[0].Title);

            var newer = new FakeBundledTextReader(@"[{""title"":""新しい見本"",""video_id"":""bbbbbbbbbbb""}]");
            var items = Create(newer).LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual("manor の一覧", items[0].Title, "manor の一覧が同梱に上書きされました。");
        }

        [Test]
        public void 控えが無ければ同梱を正として手元を入れ替える()
        {
            // v1.0.7 以前から上げた想定: 手元はあるが控えが無い。
            File.WriteAllText(LocalPath, @"[{""title"":""古い版の見本"",""video_id"":""ZZZZZZZZZZZ""}]");

            var reader = new FakeBundledTextReader(@"[{""title"":""見本"",""video_id"":""abcdefghijk""}]");
            var store = Create(reader);
            var items = store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual("見本", items[0].Title,
                "控えが無い（古い版から上げた）のに手元を守っています（v1.0.8 で主人が踏んだ穴）。");
            Assert.IsTrue(File.Exists(store.BundledCopyPath), "同梱の控えが作られていません。");
        }
    }
}
