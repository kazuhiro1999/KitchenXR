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
        public void 二回目は同梱を見ずに手元のものを読む()
        {
            var reader = new FakeBundledTextReader(@"[{""title"":""見本"",""video_id"":""abcdefghijk""}]");
            var store = Create(reader);
            store.LoadAsync().GetAwaiter().GetResult();

            // 主人が PC から書き換えた、の想定。
            File.WriteAllText(LocalPath, @"[{""title"":""主人が入れたもの"",""video_id"":""ZZZZZZZZZZZ""}]");

            var items = store.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual("主人が入れたもの", items[0].Title, "手元のものより同梱が優先されています。");
            Assert.AreEqual(1, reader.ReadCount, "手元にあるのに同梱を読みに行っています。");
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
    }
}
