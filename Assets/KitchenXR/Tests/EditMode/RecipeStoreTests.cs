using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Net;
using NUnit.Framework;
using UnityEngine;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// オフライン前提の保管庫の検算（設計 §11 追補）。
    /// 主人は調理中に電子レンジを使う——そのとき通信は切れる。だから
    /// 「一度取ったものはローカルから出る」「取れなかったものは札で代える」
    /// 「同じものを何度も取りに行かない」の3つを機械で示す。
    /// ネットには出ない（<see cref="IRecipeImageDownloader"/> を差し替える）。
    /// </summary>
    public class RecipeStoreTests
    {
        private const string RecipeId = "R1";
        private const string ImageUrl = "https://example.invalid/step1.jpg";

        private const string SampleJson = @"{
  ""id"": ""R1"",
  ""title"": ""試験用レシピ"",
  ""phases"": [{""id"": ""prep"", ""title"": ""下ごしらえ""}],
  ""steps"": [
    {""index"": 1, ""phase"": ""prep"", ""title"": ""切る"", ""instruction"": ""切る。"",
     ""image"": ""https://example.invalid/step1.jpg""}
  ]
}";

        private string _root;

        private sealed class FakeDownloader : IRecipeImageDownloader
        {
            public readonly List<string> Requested = new List<string>();
            public readonly Dictionary<string, byte[]> Responses = new Dictionary<string, byte[]>();

            public UniTask<byte[]> GetBytesAsync(string url, CancellationToken token)
            {
                Requested.Add(url);
                return UniTask.FromResult(Responses.TryGetValue(url, out var bytes) ? bytes : null);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "KitchenXRTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static byte[] SmallPng()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var bytes = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            return bytes;
        }

        [Test]
        public void 見本のJSONは初回にローカルへ写され次からはローカルを読む()
        {
            var store = new RecipeStore(_root, new FakeDownloader());

            Assert.IsFalse(store.HasLocalRecipe(RecipeId));

            var first = store.LoadRecipe(RecipeId, SampleJson);
            Assert.AreEqual("試験用レシピ", first.Title);
            Assert.IsTrue(store.HasLocalRecipe(RecipeId), "レシピ JSON が persistentDataPath 側に残っていません。");

            // 2回目は見本を渡さなくても読める（＝Resources ではなくローカルを見ている）。
            var second = store.LoadRecipe(RecipeId, null);
            Assert.AreEqual("試験用レシピ", second.Title);
            Assert.AreEqual(1, second.Steps.Count);
        }

        [Test]
        public void 画像は一度取ったらローカルから出る()
        {
            var downloader = new FakeDownloader();
            downloader.Responses[ImageUrl] = SmallPng();
            var store = new RecipeStore(_root, downloader);

            var key = RecipeStore.StepImageKey(1);
            var texture = store.LoadImageAsync(RecipeId, key, ImageUrl).GetAwaiter().GetResult();

            Assert.IsNotNull(texture, "取得した画像がテクスチャになっていません。");
            Assert.IsTrue(store.HasLocalImage(RecipeId, key), "画像がローカルに保存されていません。");
            UnityEngine.Object.DestroyImmediate(texture);

            // 2回目は通信しない（電子レンジで切れていても出る）。
            var again = store.LoadImageAsync(RecipeId, key, ImageUrl).GetAwaiter().GetResult();
            Assert.IsNotNull(again);
            UnityEngine.Object.DestroyImmediate(again);

            Assert.AreEqual(1, downloader.Requested.Count,
                "保存済みの画像をもう一度取りに行きました: " + string.Join(", ", downloader.Requested));
        }

        [Test]
        public void URLの無い工程は取りに行かず札で代える()
        {
            var downloader = new FakeDownloader();
            var store = new RecipeStore(_root, downloader);

            var texture = store.LoadImageAsync(RecipeId, RecipeStore.StepImageKey(1), null).GetAwaiter().GetResult();

            Assert.IsNull(texture, "URL の無い工程は null（呼び出し側が材料名の札で代える）。");
            Assert.IsEmpty(downloader.Requested, "URL が無いのに取りに行きました。");
        }

        [Test]
        public void 取れなかったURLは同じ起動で二度は叩かない()
        {
            var downloader = new FakeDownloader(); // 応答を登録しない＝取れない。
            var store = new RecipeStore(_root, downloader);

            var key = RecipeStore.StepImageKey(1);
            var first = store.LoadImageAsync(RecipeId, key, ImageUrl).GetAwaiter().GetResult();
            var second = store.LoadImageAsync(RecipeId, key, ImageUrl).GetAwaiter().GetResult();

            Assert.IsNull(first);
            Assert.IsNull(second);
            Assert.AreEqual(1, downloader.Requested.Count,
                "取れなかった URL を工程を描き直すたびに叩いています（通信の無い台所で毎回待たされる）。");
            Assert.IsFalse(store.HasLocalImage(RecipeId, key));
        }

        [Test]
        public void 先読みはheroと全工程を手元に揃える()
        {
            var downloader = new FakeDownloader();
            downloader.Responses[ImageUrl] = SmallPng();
            var store = new RecipeStore(_root, downloader);

            var recipe = store.LoadRecipe(RecipeId, SampleJson);
            store.PrefetchAsync(recipe).GetAwaiter().GetResult();

            Assert.IsTrue(store.HasLocalImage(RecipeId, RecipeStore.StepImageKey(1)),
                "先読みで工程の画像が手元に来ていません。");
            Assert.IsFalse(store.HasLocalImage(RecipeId, RecipeStore.HeroImageKey),
                "hero_image の無いレシピで hero が作られています。");
        }
    }
}
