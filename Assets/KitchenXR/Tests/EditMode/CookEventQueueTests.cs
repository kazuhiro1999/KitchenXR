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
    /// 進行の記録の待ち行列の検算（送れないことで調理を止めない）。
    /// 積んだ順に送られる・送れなかったら残る・送れた分だけ消える、の3つを示す。
    /// 本物の manor へは繋がない（<see cref="IHttpTransport"/> ごと差し替える）。
    /// </summary>
    public class CookEventQueueTests
    {
        private const string BaseUrl = "https://manor.example.invalid";

        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(
                Path.GetTempPath(), "KitchenXRTests", Guid.NewGuid().ToString("N"), CookEventQueue.FileName);
        }

        [TearDown]
        public void TearDown()
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>差し替えの通信。どの口が何回叩かれたかを順に控える。</summary>
        private sealed class FakeTransport : IHttpTransport
        {
            public readonly List<string> Sent = new List<string>();
            public bool Online = true;

            public UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken token)
            {
                if (request.Url.EndsWith("/auth/login"))
                {
                    return UniTask.FromResult(Online
                        ? new HttpResponse(200, "{\"ok\":true,\"mode\":\"loopback\"}")
                        : HttpResponse.Offline);
                }

                if (!Online)
                {
                    return UniTask.FromResult(HttpResponse.Offline);
                }

                // 「どのセッションに何を送ったか」を短い文字列で控える。
                var body = request.JsonBody ?? string.Empty;
                var marker = request.Url.EndsWith("/end")
                    ? "end"
                    : body.Contains("\"prev\"") ? "prev" : "next";

                Sent.Add($"{SessionIdOf(request.Url)}:{marker}");
                return UniTask.FromResult(new HttpResponse(200, "{\"current\":1,\"progress\":0.1}"));
            }

            private static string SessionIdOf(string url)
            {
                var parts = url.Split('/');
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i] == "cook-sessions")
                    {
                        return parts[i + 1];
                    }
                }

                return "?";
            }
        }

        /// <summary>常に「断る」通信（4xx）。行列が詰まらないことを見る。</summary>
        private sealed class RejectingTransport : IHttpTransport
        {
            public int Calls;

            public UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken token)
            {
                if (request.Url.EndsWith("/auth/login"))
                {
                    return UniTask.FromResult(new HttpResponse(200, "{\"ok\":true}"));
                }

                Calls++;
                return UniTask.FromResult(new HttpResponse(400, "{\"detail\":\"調理セッションは既に終了しています\"}"));
            }
        }

        /// <summary>
        /// 端末の鍵で叩く <see cref="ManorClient"/>。
        /// 鍵が無いと <c>IsConfigured</c> が false になり、待ち行列はそもそも送ろうとしない。
        /// </summary>
        private static ManorClient Client(IHttpTransport transport)
        {
            var client = new ManorClient(ManorSettings.Parse($"{{\"base_url\": \"{BaseUrl}\"}}"), transport);
            client.UseDeviceToken("device-token");
            return client;
        }

        // ---------------------------------------------------------------- 本題

        [Test]
        public void 積んだ順に送られて送れた分だけ消える()
        {
            var queue = new CookEventQueue(_path);
            queue.Enqueue(7, "next", 2);
            queue.Enqueue(7, "next", 3);
            queue.Enqueue(7, "prev", 2);
            queue.Enqueue(7, CookEventQueue.EndType);

            Assert.AreEqual(4, queue.PendingCount);

            var transport = new FakeTransport();
            var sent = queue.FlushAsync(Client(transport)).GetAwaiter().GetResult();

            Assert.AreEqual(4, sent);
            CollectionAssert.AreEqual(
                new[] { "7:next", "7:next", "7:prev", "7:end" }, transport.Sent,
                "積んだ順に送られていません（工程の前後が入れ替わると manor の current がずれます）。");
            Assert.AreEqual(0, queue.PendingCount, "送れたものが消えていません。");
            Assert.IsFalse(File.Exists(_path), "空になった行列のファイルが残っています。");
        }

        [Test]
        public void 送れないときは残って次の機会にまた先頭から送られる()
        {
            var queue = new CookEventQueue(_path);
            queue.Enqueue(7, "next", 2);
            queue.Enqueue(7, "next", 3);

            var transport = new FakeTransport { Online = false }; // 電子レンジ。
            var sent = queue.FlushAsync(Client(transport)).GetAwaiter().GetResult();

            Assert.AreEqual(0, sent);
            Assert.IsEmpty(transport.Sent);
            Assert.AreEqual(2, queue.PendingCount, "送れなかったものが消えました（進行の記録が失われます）。");

            // 通信が戻ったら、同じ順で送られる。
            transport.Online = true;
            var again = queue.FlushAsync(Client(transport)).GetAwaiter().GetResult();

            Assert.AreEqual(2, again);
            CollectionAssert.AreEqual(new[] { "7:next", "7:next" }, transport.Sent);
            Assert.AreEqual(0, queue.PendingCount);
        }

        [Test]
        public void 途中で切れたら送れた分だけ消えて残りは残る()
        {
            var queue = new CookEventQueue(_path);
            queue.Enqueue(7, "next", 2);
            queue.Enqueue(7, "next", 3);
            queue.Enqueue(7, "next", 4);

            // 1件送ったところで切れる通信。
            var calls = 0;
            var transport = new StubTransport(request =>
            {
                if (request.Url.EndsWith("/auth/login"))
                {
                    return new HttpResponse(200, "{\"ok\":true}");
                }

                calls++;
                return calls <= 1
                    ? new HttpResponse(200, "{\"current\":2,\"progress\":0.2}")
                    : HttpResponse.Offline;
            });

            var sent = queue.FlushAsync(Client(transport)).GetAwaiter().GetResult();

            Assert.AreEqual(1, sent);
            Assert.AreEqual(2, queue.PendingCount, "送れた1件だけが消え、残り2件が残るはずです。");
        }

        [Test]
        public void manorが断ったものは捨てて行列を詰まらせない()
        {
            var queue = new CookEventQueue(_path);
            queue.Enqueue(9, "next", 2);
            queue.Enqueue(9, "next", 3);

            var transport = new RejectingTransport();
            var sent = queue.FlushAsync(Client(transport)).GetAwaiter().GetResult();

            Assert.AreEqual(0, sent, "断られたものは「送れた」ではありません。");
            Assert.AreEqual(2, transport.Calls, "断られても次の1件へ進むはずです。");
            Assert.AreEqual(0, queue.PendingCount,
                "何度送っても通らないものを残すと、行列が永久に詰まって以後の記録が届きません。");
        }

        [Test]
        public void セッションの無い進行は積まない()
        {
            var queue = new CookEventQueue(_path);

            // 見本を進めているとき・manor 未設定のとき。
            queue.Enqueue(null, "next", 2);

            Assert.AreEqual(0, queue.PendingCount);
            Assert.IsFalse(File.Exists(_path));
        }

        [Test]
        public void 壊れた行は飛ばして読める行だけ送る()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path,
                "{\"session_id\":7,\"type\":\"next\",\"step\":2}\n"
                + "{\"session_id\":7,\"type\":\n"          // 電源断で千切れた行。
                + "\n"
                + "{\"session_id\":7,\"type\":\"end\"}\n");

            var queue = new CookEventQueue(_path);
            Assert.AreEqual(2, queue.PendingCount, "壊れた行で行列そのものが読めなくなってはいけません。");

            var transport = new FakeTransport();
            Assert.AreEqual(2, queue.FlushAsync(Client(transport)).GetAwaiter().GetResult());
            CollectionAssert.AreEqual(new[] { "7:next", "7:end" }, transport.Sent);
        }

        [Test]
        public void manor未設定なら送ろうとしない()
        {
            var queue = new CookEventQueue(_path);
            queue.Enqueue(7, "next", 2);

            var transport = new FakeTransport();
            var client = new ManorClient(ManorSettings.NotConfigured(), transport);

            Assert.AreEqual(0, queue.FlushAsync(client).GetAwaiter().GetResult());
            Assert.IsEmpty(transport.Sent);
            Assert.AreEqual(1, queue.PendingCount, "送れないなら残す（manor.json が置かれたら送られる）。");
        }

        /// <summary>その場で振る舞いを決める差し替えの通信。</summary>
        private sealed class StubTransport : IHttpTransport
        {
            private readonly Func<HttpRequest, HttpResponse> _responder;

            public StubTransport(Func<HttpRequest, HttpResponse> responder)
            {
                _responder = responder;
            }

            public UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken token) =>
                UniTask.FromResult(_responder(request));
        }
    }
}
