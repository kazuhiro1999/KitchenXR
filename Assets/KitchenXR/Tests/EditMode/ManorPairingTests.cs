using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Net;
using NUnit.Framework;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 端末のペアリング（v1.0.10。manor の ADR-017 D1・D2・D3・D6）の検算。
    /// **本物の manor へは繋がない**——通信は <see cref="IHttpTransport"/> ごと差し替える。
    ///
    /// 押さえるのは契約の要所:
    ///   1. 控え（`manor-device.json`）の往復・壊れた JSON・鍵の破棄
    ///   2. 探索の応答（UDP 8791 の返り）の読み——**純粋関数**なのでここで見る
    ///   3. 鍵があれば <c>Authorization: Bearer</c> で送り、**cookie のログインはしない**
    ///   4. 401 なら鍵を捨てて <c>DeviceRevoked</c> を上げる（Bootstrap がやり直す）
    ///   5. `pair/start` → `pair/poll` の JSON が ADR のとおりに読める
    /// </summary>
    public class ManorPairingTests
    {
        private const string BaseUrl = "https://manor.example.invalid";
        private const string Token = "dev1ce-t0ken";

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "KitchenXRPairTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private ManorDeviceFile DeviceFile() =>
            new ManorDeviceFile(Path.Combine(_root, ManorDeviceFile.FileName));

        private static ManorSettings Settings() =>
            ManorSettings.Parse($"{{\"base_url\": \"{BaseUrl}\"}}");

        /// <summary>差し替えの通信。頼まれたものを全部控え、返す中身は試験が決める。</summary>
        private sealed class FakeTransport : IHttpTransport
        {
            public readonly List<HttpRequest> Requests = new List<HttpRequest>();
            public Func<HttpRequest, HttpResponse> Responder;

            public UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken token)
            {
                Requests.Add(request);
                return UniTask.FromResult(Responder?.Invoke(request) ?? HttpResponse.Offline);
            }

            public string HeaderOf(int index, string name) =>
                Requests[index].Headers.TryGetValue(name, out var value) ? value : null;
        }

        // ---------------------------------------------------------------- 控え

        [Test]
        public void 控えは書いたものがそのまま読める()
        {
            var file = DeviceFile();
            Assert.IsNull(file.Load(), "何も書いていないのに控えがあります。");

            file.Save(BaseUrl + "/", Token, "dev-1", "kazu", "2026-09-13T10:00:00Z");

            var device = file.Load();
            Assert.IsNotNull(device);
            Assert.AreEqual(BaseUrl, device.BaseUrl, "末尾の / は落とす。");
            Assert.AreEqual(Token, device.Token);
            Assert.AreEqual("dev-1", device.DeviceId);
            Assert.AreEqual("kazu", device.UserId);
            Assert.AreEqual("2026-09-13T10:00:00Z", device.PairedAt);
            Assert.IsTrue(device.HasToken);
            Assert.IsTrue(device.HasBaseUrl);
        }

        [Test]
        public void 壊れた控えは無かったことにする()
        {
            var path = Path.Combine(_root, ManorDeviceFile.FileName);
            File.WriteAllText(path, "これは JSON ではない");

            var file = new ManorDeviceFile(path);
            Assert.IsNull(file.Load(), "壊れた控えを読んでしまいました（ペアリングからやり直すべきです）。");

            Assert.IsNull(ManorDeviceFile.Parse("{}"), "中身が空なら持っていても意味がありません。");
            Assert.IsNull(ManorDeviceFile.Parse(null));
            Assert.IsNull(ManorDeviceFile.Parse("[1,2,3]"), "形が違っても落ちない。");

            // 鍵だけ・URL だけは残す（探索をやり直せば繋がる／ペアリングすれば繋がる）。
            Assert.IsNotNull(ManorDeviceFile.Parse("{\"base_url\":\"http://x\"}"));
            Assert.IsNotNull(ManorDeviceFile.Parse("{\"token\":\"t\"}"));
        }

        [Test]
        public void 鍵を捨てても繋ぎ先は残る()
        {
            var file = DeviceFile();
            file.Save(BaseUrl, Token, "dev-1", "kazu");

            file.ForgetToken();

            var device = file.Load();
            Assert.IsNotNull(device, "繋ぎ先まで消えました（もう一度探索させることになります）。");
            Assert.IsFalse(device.HasToken, "鍵が残っています（失効した鍵は二度と通りません）。");
            Assert.AreEqual(BaseUrl, device.BaseUrl);

            file.Delete();
            Assert.IsNull(file.Load());
        }

        [Test]
        public void 繋ぎ先だけを書き換えても鍵は残る()
        {
            var file = DeviceFile();
            file.Save(BaseUrl, Token, "dev-1", "kazu");

            file.SaveBaseUrl("http://192.168.0.9:8789/");

            var device = file.Load();
            Assert.AreEqual("http://192.168.0.9:8789", device.BaseUrl, "探索で見つけた口を覚えていません。");
            Assert.AreEqual(Token, device.Token, "住所が変わっただけで鍵を捨てました。");
        }

        // ---------------------------------------------------------------- 探索

        [Test]
        public void 探索の応答からbase_urlを読む()
        {
            const string reply =
                "{\"kind\":\"manor\",\"name\":\"KAZU-PC\",\"base_url\":\"http://192.168.0.2:8789/\",\"version\":\"2.3.0\"}";

            Assert.AreEqual("http://192.168.0.2:8789", ManorDiscovery.ParseBaseUrl(reply), "末尾の / は落とす。");

            var announcement = ManorDiscovery.ParseAnnouncement(reply);
            Assert.AreEqual("KAZU-PC", announcement.Name);
            Assert.AreEqual("2.3.0", announcement.Version);
        }

        [Test]
        public void manor以外の応答は拾わない()
        {
            // 同じ口に別の何かが答えることはある（家の中の機器は喋りたがる）。
            Assert.IsEmpty(ManorDiscovery.ParseBaseUrl(
                "{\"kind\":\"printer\",\"base_url\":\"http://192.168.0.5\"}"));

            Assert.IsEmpty(ManorDiscovery.ParseBaseUrl("{\"kind\":\"manor\"}"),
                "繋ぎ先の無い返りは使えません。");
            Assert.IsEmpty(ManorDiscovery.ParseBaseUrl("壊れている"), "壊れていても落ちない。");
            Assert.IsEmpty(ManorDiscovery.ParseBaseUrl(string.Empty));
            Assert.IsEmpty(ManorDiscovery.ParseBaseUrl(null));

            Assert.AreEqual("manor-discover v1", ManorDiscovery.Probe, "探索の文言が ADR-017 D3 と違います。");
            Assert.AreEqual(8791, ManorDiscovery.Port, "探索の口が ADR-017 D3 と違います。");
        }

        // ---------------------------------------------------------------- 鍵で叩く

        [Test]
        public void 鍵があればBearerで送りログインはしない()
        {
            var transport = new FakeTransport { Responder = _ => new HttpResponse(200, "{\"items\":[]}") };

            var client = new ManorClient(Settings(), transport);
            client.UseDeviceToken(Token);

            Assert.IsTrue(client.IsConfigured);
            Assert.IsTrue(client.HasDeviceToken);

            var result = client.ListRecipesAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, transport.Requests.Count, "鍵があるのに合言葉で入ろうとしました。");
            Assert.AreEqual($"Bearer {Token}", transport.HeaderOf(0, "Authorization"),
                "Authorization: Bearer が付いていません（ADR-017 D1）。");
            Assert.IsNull(transport.HeaderOf(0, "Cookie"), "鍵の経路に cookie は付けません。");
            Assert.IsFalse(client.IsLoggedIn);
        }

        [Test]
        public void 鍵が失効したら捨てて出来事を上げる()
        {
            var transport = new FakeTransport { Responder = _ => new HttpResponse(401, "{\"detail\":\"失効\"}") };

            var client = new ManorClient(Settings(), transport);
            client.UseDeviceToken(Token);

            var revoked = 0;
            client.DeviceRevoked += () => revoked++;

            var result = client.ListRecipesAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(401, result.StatusCode);
            Assert.AreEqual(1, revoked, "DeviceRevoked が上がっていません（Bootstrap がやり直せません）。");
            Assert.IsFalse(client.HasDeviceToken, "通らない鍵を持ち続けています。");
            Assert.IsFalse(client.IsConfigured, "鍵を捨てたのに叩けることになっています。");

            Assert.AreEqual(1, transport.Requests.Count,
                "401 のあとに入り直しました（鍵の経路では入り直せません。番号を出し直します）。");
        }

        [Test]
        public void 鍵が無ければ叩かずペアリングの口だけが使える()
        {
            var transport = new FakeTransport { Responder = _ => new HttpResponse(200, "{}") };
            var client = new ManorClient(Settings(), transport);

            Assert.IsTrue(client.HasBaseUrl);
            Assert.IsFalse(client.IsConfigured);

            Assert.IsFalse(client.ListRecipesAsync().GetAwaiter().GetResult().IsSuccess);
            Assert.IsEmpty(transport.Requests, "鍵が無いのに料理長の口を叩きました。");
        }

        // ---------------------------------------------------------------- ペアリング

        [Test]
        public void pairStartは番号と間隔を読む()
        {
            var transport = new FakeTransport
            {
                Responder = _ => new HttpResponse(200,
                    "{\"pair_id\":\"p-123\",\"code\":\"482913\",\"expires_in\":300,\"poll_after\":2}"),
            };

            var client = new ManorClient(Settings(), transport);
            var started = client.PairStartAsync("Quest 3 (Oculus Quest)").GetAwaiter().GetResult();

            Assert.IsTrue(started.IsSuccess);
            Assert.AreEqual("p-123", started.Value.PairId);
            Assert.AreEqual("482913", started.Value.Code);
            Assert.AreEqual(300, started.Value.ExpiresInSeconds);
            Assert.AreEqual(2, started.Value.PollAfterSeconds);

            Assert.AreEqual($"{BaseUrl}/api/v1/devices/pair/start", transport.Requests[0].Url,
                "ペアリングの口が ADR-017 D2 と違います。");
            Assert.IsNull(transport.HeaderOf(0, "Authorization"), "ペアリングに認証は要りません。");
            StringAssert.Contains("\"kind\":\"kitchenxr\"", transport.Requests[0].JsonBody);
            StringAssert.Contains("Quest 3", transport.Requests[0].JsonBody);
        }

        [Test]
        public void pairStartの返りが読めなければ断りとして返る()
        {
            var transport = new FakeTransport { Responder = _ => new HttpResponse(200, "{\"code\":\"482913\"}") };
            var client = new ManorClient(Settings(), transport);

            // pair_id が無ければ訊きに行けない（番号だけ出しても意味が無い）。
            Assert.IsFalse(client.PairStartAsync("Quest 3").GetAwaiter().GetResult().IsSuccess);
        }

        [Test]
        public void pairPollは3つの状態を読む()
        {
            var body = "{\"status\":\"pending\"}";
            var transport = new FakeTransport { Responder = _ => new HttpResponse(200, body) };
            var client = new ManorClient(Settings(), transport);

            var pending = client.PairPollAsync("p-123").GetAwaiter().GetResult();
            Assert.IsTrue(pending.IsSuccess);
            Assert.IsTrue(pending.Value.IsPending);
            Assert.IsFalse(pending.Value.IsApproved);
            Assert.IsFalse(pending.Value.IsExpired);
            StringAssert.Contains("\"pair_id\":\"p-123\"", transport.Requests[0].JsonBody);
            Assert.AreEqual($"{BaseUrl}/api/v1/devices/pair/poll", transport.Requests[0].Url);

            body = $"{{\"status\":\"approved\",\"token\":\"{Token}\",\"device_id\":\"dev-1\",\"user_id\":\"kazu\"}}";
            var approved = client.PairPollAsync("p-123").GetAwaiter().GetResult();
            Assert.IsTrue(approved.Value.IsApproved);
            Assert.AreEqual(Token, approved.Value.Token);
            Assert.AreEqual("dev-1", approved.Value.DeviceId);
            Assert.AreEqual("kazu", approved.Value.UserId);

            body = "{\"status\":\"expired\"}";
            Assert.IsTrue(client.PairPollAsync("p-123").GetAwaiter().GetResult().Value.IsExpired);

            // 知らない語が来たら「取り直す」側に寄せる（端末に4つめの語を覚えさせない）。
            body = "{\"status\":\"used\"}";
            Assert.IsTrue(client.PairPollAsync("p-123").GetAwaiter().GetResult().Value.IsExpired);

            // approved と言われても鍵が空なら許可とは見なさない（控えても通らない）。
            body = "{\"status\":\"approved\"}";
            Assert.IsFalse(client.PairPollAsync("p-123").GetAwaiter().GetResult().Value.IsApproved);
        }

        [Test]
        public void 場所が分からなければペアリングも始めない()
        {
            var transport = new FakeTransport { Responder = _ => new HttpResponse(200, "{}") };
            var client = new ManorClient(ManorSettings.NotConfigured(), transport);

            Assert.IsFalse(client.PairStartAsync("Quest 3").GetAwaiter().GetResult().IsSuccess);
            Assert.IsFalse(client.PairPollAsync("p-123").GetAwaiter().GetResult().IsSuccess);
            Assert.IsEmpty(transport.Requests, "manor の場所が分からないのに通信しました。");
        }

        [Test]
        public void 探索で見つけた口を当てられる()
        {
            var transport = new FakeTransport { Responder = _ => new HttpResponse(200, "{\"items\":[]}") };
            var client = new ManorClient(ManorSettings.NotConfigured(), transport);

            Assert.IsFalse(client.HasBaseUrl);

            client.UseBaseUrl("http://192.168.0.2:8789/", "探索");
            client.UseDeviceToken(Token);

            Assert.IsTrue(client.IsConfigured);
            Assert.IsTrue(client.ListRecipesAsync().GetAwaiter().GetResult().IsSuccess);
            Assert.AreEqual("http://192.168.0.2:8789/api/v1/kitchen/recipes", transport.Requests[0].Url);
        }
    }
}
