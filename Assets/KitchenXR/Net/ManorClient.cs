using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Net
{
    /// <summary>
    /// manor の料理長のレシピ帳を読む口（設計 §8・manor の ADR-015 D3・ADR-017）。
    /// **読む側**に徹する——登録・取り込み・編集は manor の Web（`/kitchen/recipes`）の仕事で、
    /// XR からは行わない（依存は kitchen-xr → manor の一方向。設計 §0）。
    ///
    /// ## 端末の鍵（v1.0.10。ADR-017 D1・D6）
    ///
    /// 主役は <c>Authorization: Bearer &lt;鍵&gt;</c>。鍵はペアリング（<see cref="PairStartAsync"/>
    /// で6桁の番号を貰い、主人が manor の Web で許可し、<see cref="PairPollAsync"/> で受け取る）で
    /// 降りてきて、<see cref="ManorDeviceFile"/> に控える。合言葉は**もう平文で持たない**。
    ///
    /// **鍵で叩いて 401 が返ったら鍵を捨てて <see cref="DeviceRevoked"/> を上げる**
    /// （主人が Web で失効させた・manor の home を作り直した）。入り直しはここではやらない——
    /// やり直しとは「番号を出して主人に許可してもらう」ことなので、板を持っている
    /// <c>Bootstrap</c> の仕事である。
    ///
    /// ## cookie の経路も残してある
    ///
    /// manor は <c>POST /api/v1/auth/login {passcode}</c> に <c>Set-Cookie: manor_session=…</c>
    /// で答える（ADR-005 §2 D4。寿命 24 時間）。この道を通るのは
    /// <see cref="UseCookieLogin"/> を呼んだときだけで、**<c>manor.json</c> の合言葉は読まない**
    /// （v1.0.10 で廃止）。残しているのは試験と、将来 tailnet 越しに cookie を使う場合のため。
    /// <see cref="UnityEngine.Networking.UnityWebRequest"/> は Android ではプラットフォームの
    /// cookie 入れを使い回して「いつ付くか・いつ消えるか」が見えないので、<c>Set-Cookie</c> から
    /// 値だけを取り出して自分で持ち、頼みに <c>Cookie:</c> 見出しとして手で付ける（主人の指示）。
    /// この経路だけは 401 で**1度だけ入り直して**同じ頼みを送り直す（cookie は 24 時間で切れる）。
    ///
    /// ## 例外を投げない
    ///
    /// オフラインは前提（設計 §11 追補）。全ての口は <see cref="ManorResult{T}"/> を返し、
    /// 呼び出し側は「取れた／繋がらない／断られた」で分岐する。
    /// </summary>
    public sealed class ManorClient
    {
        public const string SessionCookieName = "manor_session";

        /// <summary>端末の種類（ADR-017 D1 の <c>web_device.kind</c>）。manor 側はこの語で束ねる。</summary>
        public const string DeviceKind = "kitchenxr";

        private const string LoginPath = "/api/v1/auth/login";
        private const string RecipesPath = "/api/v1/kitchen/recipes";

        /// <summary>動画リスト（manor ADR-016）。XR が読むのは一覧だけで、編集は Web 側。</summary>
        private const string MediaPath = "/api/v1/kitchen/media";
        private const string CookSessionsPath = "/api/v1/kitchen/cook-sessions";

        /// <summary>ペアリングの2つの口（ADR-017 D2。**認証は要らない**）。</summary>
        private const string PairStartPath = "/api/v1/devices/pair/start";
        private const string PairPollPath = "/api/v1/devices/pair/poll";

        /// <summary>
        /// 繋ぎ先。探索や控えで後から決まることがあるので差し替えられる（<see cref="UseBaseUrl"/>）。
        /// </summary>
        private ManorSettings _settings;

        private readonly IHttpTransport _transport;

        /// <summary>端末の鍵（Bearer に付ける。無ければ空）。</summary>
        private string _deviceToken = string.Empty;

        /// <summary>cookie の経路を使うときの合言葉（<see cref="UseCookieLogin"/> でだけ入る）。</summary>
        private string _passcode = string.Empty;
        private bool _useCookieLogin;

        /// <summary>今持っている <c>manor_session</c> の値（無ければ空）。</summary>
        public string SessionCookie { get; private set; } = string.Empty;

        /// <summary>一度でも入れたか（loopback では cookie が返らないので、入れた事実だけを持つ）。</summary>
        public bool IsLoggedIn { get; private set; }

        /// <summary>
        /// 鍵が通らなくなった（401）。鍵はもう捨ててある——受けた側はペアリングをやり直す。
        /// </summary>
        public event System.Action DeviceRevoked;

        public ManorClient(ManorSettings settings, IHttpTransport transport)
        {
            _settings = settings ?? ManorSettings.NotConfigured();
            _transport = transport;
        }

        /// <summary>
        /// 実機・Editor で使う既定の口。<c>manor.json</c> は <c>base_url</c> の上書きだけを読む
        /// （無ければ控えか探索で決める。<see cref="UseBaseUrl"/>）。
        /// </summary>
        public static ManorClient CreateDefault() =>
            new ManorClient(ManorSettings.LoadDefault(), new UnityWebRequestHttpTransport());

        public ManorSettings Settings => _settings;

        /// <summary>繋ぎ先が決まっているか（ペアリングの口はこれだけで叩ける）。</summary>
        public bool HasBaseUrl => _settings.IsConfigured && _transport != null;

        /// <summary>端末の鍵を持っているか。</summary>
        public bool HasDeviceToken => !string.IsNullOrEmpty(_deviceToken);

        /// <summary>
        /// 料理長の口を叩けるか（繋ぎ先が決まっていて、鍵か合言葉のどちらかがある）。
        /// false なら見本だけで動く。
        /// </summary>
        public bool IsConfigured => HasBaseUrl && (HasDeviceToken || _useCookieLogin);

        /// <summary>
        /// 繋ぎ先を当てる（控えに覚えていた口・探索で見つけた口）。
        /// <paramref name="source"/> は札とログに出す出どころの一言。
        /// </summary>
        public void UseBaseUrl(string baseUrl, string source = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return;
            }

            _settings = ManorSettings.ForBaseUrl(baseUrl, source);
        }

        /// <summary>端末の鍵を当てる（控えから読んだ・ペアリングで受け取った）。</summary>
        public void UseDeviceToken(string token)
        {
            _deviceToken = token ?? string.Empty;
        }

        /// <summary>
        /// 鍵を捨てる（失効した・控えを消した）。<see cref="DeviceRevoked"/> は上げない
        /// ——自分で捨てたときに呼び出し側へ知らせても意味が無い。
        /// </summary>
        public void ForgetDeviceToken()
        {
            _deviceToken = string.Empty;
        }

        /// <summary>
        /// cookie の経路を使う（試験と、将来 tailnet 越しに cookie を使う場合）。
        /// **<c>manor.json</c> の合言葉からここへ入ることはない**（v1.0.10 で廃止）。
        /// </summary>
        public void UseCookieLogin(string passcode)
        {
            _passcode = passcode ?? string.Empty;
            _useCookieLogin = true;
        }

        // ---------------------------------------------------------------- ペアリング

        /// <summary>
        /// 番号を貰う（ADR-017 D2-1。**認証は要らない**——主人が Web で許可しない限り何も起きない）。
        /// 繋ぎ先さえ決まっていれば叩ける（鍵はまだ無い）。
        /// </summary>
        public async UniTask<ManorResult<PairStart>> PairStartAsync(
            string name, string kind = DeviceKind, CancellationToken token = default)
        {
            if (!HasBaseUrl)
            {
                return ManorResult<PairStart>.Failed(-1, "manor の場所が分かりません");
            }

            var body = JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                { "name", string.IsNullOrWhiteSpace(name) ? "Quest 3" : name.Trim() },
                { "kind", string.IsNullOrWhiteSpace(kind) ? DeviceKind : kind },
            });

            var response = await _transport.SendAsync(
                new HttpRequest("POST", _settings.Url(PairStartPath), null, body), token);

            if (!response.IsSuccess)
            {
                return Failure<PairStart>(response);
            }

            var obj = ParseObject(response.Body);
            var pairId = Text(obj?["pair_id"]);
            var code = Text(obj?["code"]);
            if (string.IsNullOrEmpty(pairId) || string.IsNullOrEmpty(code))
            {
                return ManorResult<PairStart>.Failed(response.StatusCode, "返りの形が読めません");
            }

            return ManorResult<PairStart>.Ok(
                new PairStart(pairId, code, Int(obj["expires_in"], PairStart.DefaultExpiresIn),
                    Int(obj["poll_after"], PairStart.DefaultPollAfter)),
                response.StatusCode);
        }

        /// <summary>
        /// 許可されたかを訊く（ADR-017 D2-2。<c>poll_after</c> 秒おきに叩く約束）。
        /// 返る語は <c>pending</c> / <c>approved</c> / <c>expired</c> の3つだけ（ADR-017 §4.2）。
        /// <c>approved</c> の鍵は**一度しか返らない**ので、受けたら必ず控える。
        /// </summary>
        public async UniTask<ManorResult<PairPoll>> PairPollAsync(
            string pairId, CancellationToken token = default)
        {
            if (!HasBaseUrl)
            {
                return ManorResult<PairPoll>.Failed(-1, "manor の場所が分かりません");
            }

            if (string.IsNullOrEmpty(pairId))
            {
                return ManorResult<PairPoll>.Failed(-1, "pair_id が空です");
            }

            var body = JsonConvert.SerializeObject(new Dictionary<string, string> { { "pair_id", pairId } });
            var response = await _transport.SendAsync(
                new HttpRequest("POST", _settings.Url(PairPollPath), null, body), token);

            if (!response.IsSuccess)
            {
                return Failure<PairPoll>(response);
            }

            var obj = ParseObject(response.Body);
            var status = Text(obj?["status"]);
            if (string.IsNullOrEmpty(status))
            {
                return ManorResult<PairPoll>.Failed(response.StatusCode, "返りの形が読めません");
            }

            return ManorResult<PairPoll>.Ok(
                new PairPoll(status, Text(obj["token"]), Text(obj["device_id"]), Text(obj["user_id"])),
                response.StatusCode);
        }

        // ---------------------------------------------------------------- ログイン（cookie）

        /// <summary>
        /// 合言葉で入る。返ってきた <c>Set-Cookie</c> から <c>manor_session</c> を取り出して持つ。
        /// loopback の manor は cookie を返さない（<c>{"ok":true,"mode":"loopback"}</c>）——
        /// それも成功として扱う。<see cref="UseCookieLogin"/> を呼んでいなければ何もしない。
        /// </summary>
        public async UniTask<ManorResult<bool>> LoginAsync(CancellationToken token = default)
        {
            if (!HasBaseUrl || !_useCookieLogin)
            {
                return ManorResult<bool>.Failed(-1, "合言葉の経路は使いません（端末の鍵で叩きます）");
            }

            var body = JsonConvert.SerializeObject(new Dictionary<string, string> { { "passcode", _passcode } });
            var response = await _transport.SendAsync(
                new HttpRequest("POST", _settings.Url(LoginPath), null, body), token);

            if (response.IsOffline)
            {
                return ManorResult<bool>.Offline();
            }

            if (!response.IsSuccess)
            {
                IsLoggedIn = false;
                return ManorResult<bool>.Failed(response.StatusCode,
                    response.IsUnauthorized ? "合言葉が違います" : $"ログインできません（{response.StatusCode}）");
            }

            var cookie = ExtractSessionCookie(response.Header("Set-Cookie"));
            if (!string.IsNullOrEmpty(cookie))
            {
                SessionCookie = cookie;
            }

            IsLoggedIn = true;
            return ManorResult<bool>.Ok(true, response.StatusCode);
        }

        /// <summary>
        /// <c>Set-Cookie</c> の値から <c>manor_session</c> の中身だけを取り出す。
        ///
        /// 実際に返るのは
        /// <c>manor_session=abc123; HttpOnly; Path=/; SameSite=lax; Max-Age=86400</c> のような1行。
        /// <see cref="UnityEngine.Networking.UnityWebRequest.GetResponseHeaders"/> は同じ名前の見出しを
        /// カンマで繋いで1つにしてしまうので、**セミコロンとカンマの両方を区切りとして見る**。
        /// 属性（Path・Expires など）は捨てる——送り返すのは名前と値だけでよい。
        /// </summary>
        public static string ExtractSessionCookie(string setCookieHeader)
        {
            if (string.IsNullOrEmpty(setCookieHeader))
            {
                return string.Empty;
            }

            foreach (var piece in setCookieHeader.Split(';', ','))
            {
                var trimmed = piece.Trim();
                if (!trimmed.StartsWith(SessionCookieName + "=", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var value = trimmed.Substring(SessionCookieName.Length + 1).Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        // ---------------------------------------------------------------- レシピ帳

        /// <summary>一覧の生の JSON（<c>{"items":[…]}</c>）。呼び出し側がそのまま index.json へ写す。</summary>
        public async UniTask<ManorResult<string>> ListRecipesAsync(CancellationToken token = default)
        {
            var response = await SendWithAuthAsync("GET", _settings.Url(RecipesPath), null, token);
            return TextResult(response);
        }

        /// <summary>
        /// 動画リストの生の JSON（<c>{"items":[…]}</c>。<c>MediaJson.Parse</c> がそのまま読める形）。
        /// 呼び出し側が <c>MediaStore</c> の手元へ写す——圏外のときは写しを読む（レシピ帳と同じ流儀）。
        /// </summary>
        public async UniTask<ManorResult<string>> ListMediaAsync(CancellationToken token = default)
        {
            var response = await SendWithAuthAsync("GET", _settings.Url(MediaPath), null, token);
            return TextResult(response);
        }

        /// <summary>レシピ1本の生の契約 JSON。呼び出し側がそのまま <see cref="RecipeStore"/> へ写す。</summary>
        public async UniTask<ManorResult<string>> GetRecipeAsync(string recipeId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(recipeId))
            {
                return ManorResult<string>.Failed(-1, "レシピ id が空です");
            }

            var response = await SendWithAuthAsync("GET", _settings.Url($"{RecipesPath}/{recipeId}"), null, token);
            return TextResult(response);
        }

        // ---------------------------------------------------------------- 調理セッション

        /// <summary>
        /// 調理開始。manor は「同じ利用者の未終了セッションがあればそれを返す」ので、
        /// ここで二重に作られることはない（ADR-015 D3・`recipes.start_session`）。
        /// </summary>
        public async UniTask<ManorResult<CookSessionRef>> StartSessionAsync(
            string recipeId, CancellationToken token = default)
        {
            if (!int.TryParse(recipeId, out var numericId))
            {
                // 見本（Resources の炒飯）の id は文字列。manor に無いものなので送らない。
                return ManorResult<CookSessionRef>.Failed(-1, "manor のレシピではありません");
            }

            var body = JsonConvert.SerializeObject(new Dictionary<string, int> { { "recipe_id", numericId } });
            var response = await SendWithAuthAsync("POST", _settings.Url(CookSessionsPath), body, token);
            if (!response.IsSuccess)
            {
                return Failure<CookSessionRef>(response);
            }

            var obj = ParseObject(response.Body);
            return obj == null
                ? ManorResult<CookSessionRef>.Failed(response.StatusCode, "返りの形が読めません")
                : ManorResult<CookSessionRef>.Ok(
                    new CookSessionRef(NullableInt(obj["id"]), recipeId, Int(obj["current"], 1)),
                    response.StatusCode);
        }

        /// <summary>
        /// 工程の合図（<c>next</c> / <c>prev</c> / <c>timer_start</c> / <c>done</c>）。
        /// manor 側の受け付ける種類はこの4つだけ（`recipes.VALID_EVENT_TYPES`）。
        /// </summary>
        public async UniTask<ManorResult<CookProgress>> PostEventAsync(
            int sessionId, string type, int? step = null, CancellationToken token = default)
        {
            var payload = new Dictionary<string, object> { { "type", type } };
            if (step.HasValue)
            {
                payload["step"] = step.Value;
            }

            var response = await SendWithAuthAsync(
                "POST", _settings.Url($"{CookSessionsPath}/{sessionId}/events"),
                JsonConvert.SerializeObject(payload), token);

            if (!response.IsSuccess)
            {
                return Failure<CookProgress>(response);
            }

            var obj = ParseObject(response.Body);
            return obj == null
                ? ManorResult<CookProgress>.Failed(response.StatusCode, "返りの形が読めません")
                : ManorResult<CookProgress>.Ok(
                    new CookProgress(Int(obj["current"], 1), Number(obj["progress"])), response.StatusCode);
        }

        /// <summary>
        /// 途中起動の復帰（設計 §5・ROADMAP P5）。未終了が無ければ
        /// manor は <c>{"id": null, "recipe_id": null, "current": null}</c> を返す
        /// ——その場合 <see cref="CookSessionRef.Exists"/> が false。
        /// </summary>
        public async UniTask<ManorResult<CookSessionRef>> CurrentSessionAsync(CancellationToken token = default)
        {
            var response = await SendWithAuthAsync("GET", _settings.Url($"{CookSessionsPath}/current"), null, token);
            if (!response.IsSuccess)
            {
                return Failure<CookSessionRef>(response);
            }

            var obj = ParseObject(response.Body);
            return obj == null
                ? ManorResult<CookSessionRef>.Failed(response.StatusCode, "返りの形が読めません")
                : ManorResult<CookSessionRef>.Ok(
                    new CookSessionRef(NullableInt(obj["id"]), Text(obj["recipe_id"]), Int(obj["current"], 1)),
                    response.StatusCode);
        }

        /// <summary>終了（manor 側で <c>times_cooked</c> が増える）。manor は冪等なので送り直してよい。</summary>
        public async UniTask<ManorResult<bool>> EndSessionAsync(int sessionId, CancellationToken token = default)
        {
            var response = await SendWithAuthAsync(
                "POST", _settings.Url($"{CookSessionsPath}/{sessionId}/end"), string.Empty, token);

            return response.IsSuccess
                ? ManorResult<bool>.Ok(true, response.StatusCode)
                : Failure<bool>(response);
        }

        // ---------------------------------------------------------------- 送り口

        /// <summary>
        /// 認証を付けて送る。道は2本で、**鍵があれば鍵だけ**を使う（cookie のログインはしない）。
        ///
        ///   - 端末の鍵: <c>Authorization: Bearer</c>。401 なら**鍵を捨てて
        ///     <see cref="DeviceRevoked"/>**（入り直しはできない。番号を出して主人に許可してもらう）
        ///   - cookie: 401 なら1度だけ入り直して同じ頼みを送り直す（寿命 24 時間）
        /// </summary>
        private async UniTask<HttpResponse> SendWithAuthAsync(
            string method, string url, string body, CancellationToken token)
        {
            if (!IsConfigured)
            {
                return HttpResponse.Offline;
            }

            if (HasDeviceToken)
            {
                var bearer = await _transport.SendAsync(new HttpRequest(method, url, AuthHeaders(), body), token);
                if (bearer.IsUnauthorized)
                {
                    // 主人が Web で失効させた（ADR-017 D1）。持っていても二度と通らないので捨てる。
                    Debug.LogWarning("[KitchenXR] 端末の鍵が通りませんでした。ペアリングをやり直します。");
                    ForgetDeviceToken();
                    DeviceRevoked?.Invoke();
                }

                return bearer;
            }

            // まだ一度も入っていなければ先に入る（cookie の経路。設計 §8）。
            if (!IsLoggedIn)
            {
                await LoginAsync(token);
            }

            var response = await _transport.SendAsync(new HttpRequest(method, url, AuthHeaders(), body), token);
            if (!response.IsUnauthorized)
            {
                return response;
            }

            // cookie の寿命は 24 時間。切れていたら入り直して**1回だけ**送り直す。
            Debug.Log("[KitchenXR] manor の合言葉の期限が切れていたので入り直します。");
            SessionCookie = string.Empty;
            IsLoggedIn = false;

            var login = await LoginAsync(token);
            if (!login.IsSuccess)
            {
                return response; // 入り直せない。最初の 401 をそのまま返す（無限に叩かない）。
            }

            return await _transport.SendAsync(new HttpRequest(method, url, AuthHeaders(), body), token);
        }

        /// <summary>付ける認証の見出し。鍵があれば Bearer だけ、無ければ cookie だけ。</summary>
        private IReadOnlyDictionary<string, string> AuthHeaders()
        {
            var headers = new Dictionary<string, string>();
            if (HasDeviceToken)
            {
                headers["Authorization"] = $"Bearer {_deviceToken}";
                return headers;
            }

            if (!string.IsNullOrEmpty(SessionCookie))
            {
                headers["Cookie"] = $"{SessionCookieName}={SessionCookie}";
            }

            return headers;
        }

        // ---------------------------------------------------------------- 小物

        private static ManorResult<string> TextResult(HttpResponse response) =>
            response.IsSuccess
                ? ManorResult<string>.Ok(response.Body, response.StatusCode)
                : Failure<string>(response);

        private static ManorResult<T> Failure<T>(HttpResponse response) =>
            response.IsOffline
                ? ManorResult<T>.Offline()
                : ManorResult<T>.Failed(response.StatusCode, $"manor が断りました（{response.StatusCode}）");

        private static JObject ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Text(JToken token) =>
            token == null || token.Type == JTokenType.Null ? string.Empty : token.ToString();

        private static int Int(JToken token, int fallback)
        {
            var text = Text(token);
            return int.TryParse(text, out var value) ? value : fallback;
        }

        private static int? NullableInt(JToken token)
        {
            var text = Text(token);
            return int.TryParse(text, out var value) ? value : (int?)null;
        }

        private static double Number(JToken token)
        {
            var text = Text(token);
            return double.TryParse(
                text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : 0d;
        }
    }
}
