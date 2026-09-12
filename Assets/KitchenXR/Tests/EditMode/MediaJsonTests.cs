using System.Linq;
using KitchenXR.Domain;
using NUnit.Framework;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// `media.json` の読み。テキストエディタで直に書くファイルなので、
    /// 1行の書き損じで板が丸ごと死なないことがここの主題。
    /// </summary>
    public class MediaJsonTests
    {
        [Test]
        public void 題名と動画idを読む()
        {
            var items = MediaJson.Parse(@"[{""title"":""ショーツ"",""video_id"":""abcdefghijk""}]");

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("ショーツ", items[0].Title);
            Assert.AreEqual("abcdefghijk", items[0].VideoId);
        }

        [Test]
        public void 八件を超えたら読まない()
        {
            var rows = Enumerable.Range(0, 20)
                .Select(i => $@"{{""title"":""v{i}"",""video_id"":""aaaaaaaaa{i:00}""}}");
            var items = MediaJson.Parse("[" + string.Join(",", rows) + "]");

            Assert.AreEqual(MediaJson.MaxItems, items.Count, "板に出すのは最大8件（設計 P4）。");
            Assert.AreEqual("v0", items[0].Title, "先頭から8件を採る。");
            Assert.AreEqual("v7", items[7].Title);
        }

        [Test]
        public void 不正な行は飛ばして残りを読む()
        {
            var json = @"[
                {""title"":""良い"",""video_id"":""abcdefghijk""},
                {""title"":""idが無い""},
                {""title"":""idが空"",""video_id"":""""},
                ""文字列だけの行"",
                {""title"":""記号入りのid"",""video_id"":""abc');alert(1);//""},
                {""title"":""短すぎるid"",""video_id"":""ab""},
                {""title"":""もう一つ良い"",""video_id"":""ZZZZZZZZZZZ""}
            ]";

            var items = MediaJson.Parse(json);

            Assert.AreEqual(2, items.Count, "読めた行だけが残るはずです。");
            CollectionAssert.AreEqual(new[] { "良い", "もう一つ良い" }, items.Select(i => i.Title).ToArray());
        }

        /// <summary>
        /// 動画 id は <c>youtube.html</c> の <c>loadVideo('…')</c> にそのまま埋まる。
        /// 記号を通すと JavaScript を差し込めてしまうので、英数字と - _ だけに絞る。
        /// </summary>
        [Test]
        public void JavaScriptを差し込めるidは通さない()
        {
            Assert.IsNull(MediaJson.NormalizeVideoId("abc');alert(1);//"));
            Assert.IsNull(MediaJson.NormalizeVideoId("abc\"def"));
            Assert.IsNull(MediaJson.NormalizeVideoId("abc def"));
            Assert.IsNull(MediaJson.NormalizeVideoId("<script>"));
        }

        [Test]
        public void URLを貼られてもidを取り出す()
        {
            Assert.AreEqual("YbJOTdZBX1g",
                MediaJson.NormalizeVideoId("https://www.youtube.com/watch?v=YbJOTdZBX1g"));
            Assert.AreEqual("YbJOTdZBX1g",
                MediaJson.NormalizeVideoId("https://youtu.be/YbJOTdZBX1g"));
            Assert.AreEqual("YbJOTdZBX1g",
                MediaJson.NormalizeVideoId("https://www.youtube.com/shorts/YbJOTdZBX1g"));
        }

        [Test]
        public void 題名を書き忘れてもidで出す()
        {
            var items = MediaJson.Parse(@"[{""video_id"":""abcdefghijk""}]");

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("abcdefghijk", items[0].Title);
        }

        /// <summary>
        /// manor の一覧の <c>thumbnail_url</c> を読む。
        /// </summary>
        [Test]
        public void サムネイルのURLを読む()
        {
            var items = MediaJson.Parse(
                @"{""items"":[{""title"":""あ"",""video_id"":""abcdefghijk"",
                  ""thumbnail_url"":""https://example.test/a.jpg""}]}");

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("https://example.test/a.jpg", items[0].ThumbnailUrl);
        }

        /// <summary>
        /// manor が絵の URL を返さなくても、動画 id から組み立てる
        /// ——板は「絵が無い行」を作らずに済む。
        /// </summary>
        [Test]
        public void サムネイルのURLが無ければidから組み立てる()
        {
            var items = MediaJson.Parse(@"[{""title"":""あ"",""video_id"":""abcdefghijk""}]");

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg", items[0].ThumbnailUrl);
            Assert.AreEqual(items[0].ThumbnailUrl, MediaJson.DefaultThumbnailUrl("abcdefghijk"));
        }

        /// <summary>URL に見えないもの（空白・相対パス）は捨てて既定へ落とす。</summary>
        [Test]
        public void サムネイルのURLが壊れていても既定へ落ちる()
        {
            var items = MediaJson.Parse(
                @"[{""video_id"":""abcdefghijk"",""thumbnail_url"":""   ""},
                   {""video_id"":""bbcdefghijk"",""thumbnail_url"":""/relative/a.jpg""}]");

            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(MediaJson.DefaultThumbnailUrl("abcdefghijk"), items[0].ThumbnailUrl);
            Assert.AreEqual(MediaJson.DefaultThumbnailUrl("bbcdefghijk"), items[1].ThumbnailUrl);
        }

        [Test]
        public void 壊れたJSONでも空の一覧を返す()
        {
            Assert.IsEmpty(MediaJson.Parse("[{ こわれている"));
            Assert.IsEmpty(MediaJson.Parse(string.Empty));
            Assert.IsEmpty(MediaJson.Parse(null));
            Assert.IsEmpty(MediaJson.Parse("42"));
        }

        [Test]
        public void 同梱の見本が読める()
        {
            var path = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "Assets/StreamingAssets/media.json");
            Assert.IsTrue(System.IO.File.Exists(path), $"同梱の見本がありません: {path}");

            var items = MediaJson.Parse(System.IO.File.ReadAllText(path));
            Assert.IsNotEmpty(items, "同梱の見本が1件も読めません（板が空になります）。");
            Assert.LessOrEqual(items.Count, MediaJson.MaxItems);
        }
    }
}
