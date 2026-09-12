using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using KitchenXR.Net;
using NUnit.Framework;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// manor のレシピ帳と結ぶ口の検算（P3。設計 §8・manor の ADR-015 D3）。
    /// **本物の manor へは繋がない**——通信は <see cref="IHttpTransport"/> ごと差し替える。
    ///
    /// ここで押さえるのは主人の指示の4点:
    ///   1. <c>Set-Cookie</c> から <c>manor_session</c> を取り出して持つ
    ///   2. 以後の頼みに <c>Cookie:</c> 見出しとして自分で付ける（Android の自動 cookie に頼らない）
    ///   3. **401 が返ったら1度だけ**入り直して同じ頼みを送り直す（2度目の 401 では諦める）
    ///   4. 一覧は取れたら写し、取れないときはその写しを出す
    /// </summary>
    public class ManorClientTests
    {
        private const string BaseUrl = "https://manor.example.invalid";
        private const string Passcode = "あいことば";
        private const string Cookie = "s3ss10n-value";

        private static ManorSettings Settings() =>
            ManorSettings.Parse($"{{\"base_url\": \"{BaseUrl}\", \"passcode\": \"{Passcode}\"}}");

        /// <summary>差し替えの通信。頼まれたものを全部控え、返す中身は試験が決める。</summary>
        private sealed class FakeTransport : IHttpTransport
        {
            public readonly List<HttpRequest> Requests = new List<HttpRequest>();
            public Func<HttpRequest, int, HttpResponse> Responder;

            public UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken token)
            {
                Requests.Add(request);
                var response = Responder?.Invoke(request, Requests.Count - 1) ?? HttpResponse.Offline;
                return UniTask.FromResult(response);
            }

            public int CountOf(string pathFragment)
            {
                var count = 0;
                foreach (var request in Requests)
                {
                    if (request.Url.Contains(pathFragment))
                    {
                        count++;
                    }
                }

                return count;
            }

            public string CookieOf(int index) =>
                Requests[index].Headers.TryGetValue("Cookie", out var value) ? value : null;
        }

        private static HttpResponse LoginOk() =>
            new HttpResponse(200, "{\"ok\":true,\"mode\":\"passcode\"}", new Dictionary<string, string>
            {
                { "Set-Cookie", $"{ManorClient.SessionCookieName}={Cookie}; HttpOnly; Path=/; SameSite=lax; Max-Age=86400" },
            });

        private static HttpResponse Json(string body) => new HttpResponse(200, body);

        // ---------------------------------------------------------------- cookie

        [Test]
        public void SetCookieからmanor_sessionだけを取り出す()
        {
            Assert.AreEqual(Cookie, ManorClient.ExtractSessionCookie(
                $"{ManorClient.SessionCookieName}={Cookie}; HttpOnly; Path=/; SameSite=lax"));

            // UnityWebRequest は同じ名前の見出しをカンマで繋いで1つにすることがある。
            Assert.AreEqual(Cookie, ManorClient.ExtractSessionCookie(
                $"other=1; Path=/, {ManorClient.SessionCookieName}={Cookie}; Path=/"));

            Assert.IsEmpty(ManorClient.ExtractSessionCookie("manor_user=kazu; Path=/"),
                "見ている利用者の cookie（manor_user）は認証の cookie ではありません。");
            Assert.IsEmpty(ManorClient.ExtractSessionCookie(null));
        }

        [Test]
        public void 入ったあとの頼みにはCookie見出しが自分で付く()
        {
            var transport = new FakeTransport
            {
                Responder = (request, _) =>
                    request.Url.EndsWith("/auth/login") ? LoginOk() : Json("{\"items\":[]}"),
            };

            var client = new ManorClient(Settings(), transport);
            var result = client.ListRecipesAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(Cookie, client.SessionCookie, "Set-Cookie から cookie を持っていません。");

            Assert.AreEqual(2, transport.Requests.Count, "ログイン → 一覧 の2回のはずです。");
            Assert.IsNull(transport.CookieOf(0), "ログインの頼みに cookie は要りません。");
            Assert.AreEqual($"{ManorClient.SessionCookieName}={Cookie}", transport.CookieOf(1),
                "一覧の頼みに Cookie 見出しが付いていません（Android の自動 cookie には頼らない）。");
        }

        [Test]
        public void loopbackのmanorはcookieを返さないがそれでも通る()
        {
            var transport = new FakeTransport
            {
                Responder = (request, _) => request.Url.EndsWith("/auth/login")
                    ? Json("{\"ok\":true,\"mode\":\"loopback\"}")
                    : Json("{\"items\":[]}"),
            };

            var client = new ManorClient(Settings(), transport);
            var result = client.ListRecipesAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(client.IsLoggedIn);
            Assert.IsEmpty(client.SessionCookie);
        }

        // ---------------------------------------------------------------- 401

        [Test]
        public void 認証が切れていたら1度だけ入り直して同じ頼みを送り直す()
        {
            var expiredCookie = "expired";
            var loginCount = 0;

            var transport = new FakeTransport();
            transport.Responder = (request, _) =>
            {
                if (request.Url.EndsWith("/auth/login"))
                {
                    loginCount++;
                    return loginCount == 1
                        ? new HttpResponse(200, "{\"ok\":true}", new Dictionary<string, string>
                        {
                            { "Set-Cookie", $"{ManorClient.SessionCookieName}={expiredCookie}; Path=/" },
                        })
                        : LoginOk();
                }

                // 古い cookie なら 401、入り直した後の cookie なら 200。
                var cookie = request.Headers.TryGetValue("Cookie", out var value) ? value : string.Empty;
                return cookie.Contains(Cookie) ? Json("{\"items\":[]}") : new HttpResponse(401, "");
            };

            var client = new ManorClient(Settings(), transport);
            var result = client.ListRecipesAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccess, "入り直したあとの送り直しが通っていません。");
            Assert.AreEqual(2, loginCount, "入り直しは1度だけのはずです。");
            Assert.AreEqual(2, transport.CountOf("/kitchen/recipes"),
                "同じ頼みを送るのは（最初＋送り直しの）2回だけのはずです。");
        }

        [Test]
        public void 入り直しても断られたら諦める()
        {
            var loginCount = 0;
            var transport = new FakeTransport();
            transport.Responder = (request, _) =>
            {
                if (request.Url.EndsWith("/auth/login"))
                {
                    loginCount++;
                    return LoginOk();
                }

                return new HttpResponse(401, "");
            };

            var client = new ManorClient(Settings(), transport);
            var result = client.ListRecipesAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(401, result.StatusCode);
            Assert.AreEqual(2, loginCount, "入り直しを繰り返しています（manor 側で 429 になります）。");
            Assert.AreEqual(2, transport.CountOf("/kitchen/recipes"), "送り直しは1回だけのはずです。");
        }

        [Test]
        public void 合言葉が違えば入れずオフラインでもない()
        {
            var transport = new FakeTransport
            {
                Responder = (request, _) => new HttpResponse(401, "{\"detail\":\"passcode が違います\"}"),
            };

            var client = new ManorClient(Settings(), transport);
            var result = client.LoginAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(result.IsOffline, "サーバは答えている（合言葉が違う）ので「繋がらない」ではありません。");
            Assert.IsFalse(client.IsLoggedIn);
        }

        [Test]
        public void 繋がらないときはオフラインとして返る()
        {
            var transport = new FakeTransport { Responder = (_, __) => HttpResponse.Offline };
            var client = new ManorClient(Settings(), transport);

            var result = client.ListRecipesAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsOffline);
        }

        [Test]
        public void manor未設定なら通信そのものをしない()
        {
            var transport = new FakeTransport { Responder = (_, __) => Json("{}") };
            var client = new ManorClient(ManorSettings.NotConfigured(), transport);

            Assert.IsFalse(client.IsConfigured);

            var result = client.ListRecipesAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess);
            Assert.IsEmpty(transport.Requests, "manor 未設定なのに通信しました。");
        }

        // ---------------------------------------------------------------- 調理セッション

        [Test]
        public void 調理セッションの口が契約どおりに読み書きできる()
        {
            var transport = new FakeTransport();
            transport.Responder = (request, _) =>
            {
                if (request.Url.EndsWith("/auth/login")) return LoginOk();
                if (request.Url.EndsWith("/cook-sessions/current")) return Json("{\"id\":7,\"recipe_id\":12,\"current\":3}");
                if (request.Url.EndsWith("/cook-sessions")) return Json("{\"id\":7,\"current\":1}");
                if (request.Url.EndsWith("/events")) return Json("{\"current\":2,\"progress\":0.222}");
                if (request.Url.EndsWith("/end")) return Json("{\"id\":7,\"ended\":true}");
                return new HttpResponse(404, "");
            };

            var client = new ManorClient(Settings(), transport);

            var started = client.StartSessionAsync("12").GetAwaiter().GetResult();
            Assert.IsTrue(started.IsSuccess);
            Assert.AreEqual(7, started.Value.Id);
            Assert.AreEqual(1, started.Value.Current);

            var moved = client.PostEventAsync(7, "next", 2).GetAwaiter().GetResult();
            Assert.IsTrue(moved.IsSuccess);
            Assert.AreEqual(2, moved.Value.Current);
            Assert.AreEqual(0.222, moved.Value.Progress, 0.0001);

            var current = client.CurrentSessionAsync().GetAwaiter().GetResult();
            Assert.IsTrue(current.IsSuccess);
            Assert.IsTrue(current.Value.Exists);
            Assert.AreEqual("12", current.Value.RecipeId);
            Assert.AreEqual(3, current.Value.Current);

            var ended = client.EndSessionAsync(7).GetAwaiter().GetResult();
            Assert.IsTrue(ended.IsSuccess);
        }

        [Test]
        public void 未終了のセッションが無ければ復帰しない()
        {
            var transport = new FakeTransport
            {
                Responder = (request, _) => request.Url.EndsWith("/auth/login")
                    ? LoginOk()
                    : Json("{\"id\":null,\"recipe_id\":null,\"current\":null}"),
            };

            var client = new ManorClient(Settings(), transport);
            var current = client.CurrentSessionAsync().GetAwaiter().GetResult();

            Assert.IsTrue(current.IsSuccess);
            Assert.IsFalse(current.Value.Exists, "manor は未終了が無いとき id を null で返します。");
        }

        [Test]
        public void 見本のレシピはmanorへ送らない()
        {
            var transport = new FakeTransport { Responder = (_, __) => LoginOk() };
            var client = new ManorClient(Settings(), transport);

            // 見本（Resources）の id は "chahan" のような文字列。manor のレシピ id は整数。
            var started = client.StartSessionAsync("chahan").GetAwaiter().GetResult();

            Assert.IsFalse(started.IsSuccess);
            Assert.IsEmpty(transport.Requests, "manor に無いレシピで調理セッションを作ろうとしました。");
        }

        // ---------------------------------------------------------------- 一覧の写し

        [Test]
        public void 一覧は取れたら写し取れないときはその写しを出す()
        {
            var root = Path.Combine(Path.GetTempPath(), "KitchenXRTests", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new RecipeStore(root, null);
                Assert.IsFalse(store.HasLocalIndex);

                const string listJson = @"{""items"":[
                    {""id"":12,""title"":""照り焼き"",""total_minutes"":25,""kcal"":420.0,""category"":""主菜""},
                    {""id"":13,""title"":""味噌汁"",""total_minutes"":10,""cuisine"":""和食""}]}";

                // 取れた → そのまま写す。
                store.SaveIndexJson(listJson);
                Assert.IsTrue(store.HasLocalIndex, "一覧が index.json へ写されていません。");

                // 取れない（次の起動で manor が寝ている）→ 写しから出す。
                var cached = store.LoadIndexJson();
                var items = RecipeListJson.Parse(cached);

                Assert.AreEqual(2, items.Count);
                Assert.AreEqual("12", items[0].Id, "manor の id は整数だが、手元では文字列で扱う。");
                Assert.AreEqual("照り焼き", items[0].Title);
                StringAssert.Contains("25分", items[0].DetailLine());
                StringAssert.Contains("主菜", items[0].DetailLine());
                StringAssert.Contains("420kcal", items[0].DetailLine());

                // kcal が未推定なら出さない・分類は cuisine で代える。
                StringAssert.DoesNotContain("kcal", items[1].DetailLine());
                StringAssert.Contains("和食", items[1].DetailLine());
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void 壊れた一覧でも読めた行だけを出す()
        {
            var items = RecipeListJson.Parse(
                @"{""items"":[{""id"":1,""title"":""良い行""},{""title"":""idが無い""},""文字列"",{""id"":2}]}");

            Assert.AreEqual(1, items.Count, "読めない行で一覧そのものが落ちてはいけません。");
            Assert.AreEqual("良い行", items[0].Title);

            Assert.IsEmpty(RecipeListJson.Parse("これは JSON ではない"));
            Assert.IsEmpty(RecipeListJson.Parse(null));
        }

        [Test]
        public void 一覧は20件で打ち切る()
        {
            var rows = new List<string>();
            for (var i = 1; i <= 30; i++)
            {
                rows.Add($"{{\"id\":{i},\"title\":\"レシピ{i}\"}}");
            }

            var items = RecipeListJson.Parse("{\"items\":[" + string.Join(",", rows) + "]}");

            Assert.AreEqual(RecipeListJson.MaxItems, items.Count, "一覧は最大 20 件（主人の指示）。");
        }

        // ---------------------------------------------------------------- manor.json

        [Test]
        public void manorJsonが無ければ見本だけで動く()
        {
            var missing = ManorSettings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            Assert.IsFalse(missing.IsConfigured);

            Assert.IsFalse(ManorSettings.Parse("{}").IsConfigured, "base_url が無ければ未設定。");
            Assert.IsFalse(ManorSettings.Parse("壊れている").IsConfigured, "壊れていても落ちない。");

            var ok = ManorSettings.Parse("{\"base_url\": \"http://192.168.0.2:8765/\", \"passcode\": \"x\"}");
            Assert.IsTrue(ok.IsConfigured);
            Assert.AreEqual("http://192.168.0.2:8765", ok.BaseUrl, "末尾の / は落とす。");
            Assert.AreEqual("http://192.168.0.2:8765/api/v1/auth/login", ok.Url("/api/v1/auth/login"));
        }

        [Test]
        public void 動画リストは一覧の口から生のJSONで取れる()
        {
            var transport = new FakeTransport
            {
                Responder = (request, _) =>
                    request.Url.EndsWith("/auth/login")
                        ? LoginOk()
                        : Json("{\"items\":[{\"title\":\"a\",\"video_id\":\"abcdefghijk\"}]}"),
            };

            var client = new ManorClient(Settings(), transport);
            var result = client.ListMediaAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(transport.Requests[1].Url.EndsWith("/api/v1/kitchen/media"),
                "動画リストの口が ADR-016 と違います。");
            Assert.AreEqual(1, KitchenXR.Domain.MediaJson.Parse(result.Value).Count);
        }
    }
}
