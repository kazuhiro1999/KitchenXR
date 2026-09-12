using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Net
{
    /// <summary>
    /// manor の料理長のレシピ帳を読む口（設計 §8・manor の ADR-015 D3）。
    /// **読む側**に徹する——登録・取り込み・編集は manor の Web（`/kitchen/recipes`）の仕事で、
    /// XR からは行わない（依存は kitchen-xr → manor の一方向。設計 §0）。
    ///
    /// ## cookie を自分で扱う理由
    ///
    /// manor は <c>POST /api/v1/auth/login {passcode}</c> に <c>Set-Cookie: manor_session=…</c>
    /// で答える（ADR-005 §2 D4。寿命 24 時間）。<see cref="UnityEngine.Networking.UnityWebRequest"/>
    /// は Android ではプラットフォームの cookie 入れを使い回すので、
    /// 「いつ付くか・いつ消えるか」がこちらから見えない——**それに頼らない**（主人の指示）。
    /// ここでは <c>Set-Cookie</c> から <c>manor_session</c> の値だけを取り出して自分で持ち、
    /// 以後の頼みに <c>Cookie:</c> 見出しとして手で付ける。
    ///
    /// ## 401 のとき
    ///
    /// cookie の寿命は 24 時間。主人が前の晩に使ったまま翌朝また使えば当然切れている。
    /// **401 が返ったら1度だけ入り直して、同じ頼みをもう1度送る**。
    /// 2度目も 401 なら諦めて「繋がりません」として返す（合言葉が違う可能性がある。
    /// manor 側は試行が多すぎると 429 を返すので、無限に叩かない）。
    ///
    /// ## 例外を投げない
    ///
    /// オフラインは前提（設計 §11 追補）。全ての口は <see cref="ManorResult{T}"/> を返し、
    /// 呼び出し側は「取れた／繋がらない／断られた」で分岐する。
    /// </summary>
    public sealed class ManorClient
    {
        public const string SessionCookieName = "manor_session";

        private const string LoginPath = "/api/v1/auth/login";
        private const string RecipesPath = "/api/v1/kitchen/recipes";

        /// <summary>動画リスト（manor ADR-016）。XR が読むのは一覧だけで、編集は Web 側。</summary>
        private const string MediaPath = "/api/v1/kitchen/media";
        private const string CookSessionsPath = "/api/v1/kitchen/cook-sessions";

        private readonly ManorSettings _settings;
        private readonly IHttpTransport _transport;

        /// <summary>今持っている <c>manor_session</c> の値（無ければ空）。</summary>
        public string SessionCookie { get; private set; } = string.Empty;

        /// <summary>一度でも入れたか（loopback では cookie が返らないので、入れた事実だけを持つ）。</summary>
        public bool IsLoggedIn { get; private set; }

        public ManorClient(ManorSettings settings, IHttpTransport transport)
        {
            _settings = settings ?? ManorSettings.NotConfigured();
            _transport = transport;
        }

        /// <summary>実機・Editor で使う既定の口。manor.json が無ければ <see cref="IsConfigured"/> が false。</summary>
        public static ManorClient CreateDefault() =>
            new ManorClient(ManorSettings.LoadDefault(), new UnityWebRequestHttpTransport());

        public ManorSettings Settings => _settings;

        /// <summary>manor.json が置かれているか。false なら見本だけで動く。</summary>
        public bool IsConfigured => _settings.IsConfigured && _transport != null;

        // ---------------------------------------------------------------- ログイン

        /// <summary>
        /// 合言葉で入る。返ってきた <c>Set-Cookie</c> から <c>manor_session</c> を取り出して持つ。
        /// loopback の manor は cookie を返さない（<c>{"ok":true,"mode":"loopback"}</c>）——
        /// それも成功として扱う。
        /// </summary>
        public async UniTask<ManorResult<bool>> LoginAsync(CancellationToken token = default)
        {
            if (!IsConfigured)
            {
                return ManorResult<bool>.Failed(-1, "manor 未設定");
            }

            var body = JsonConvert.SerializeObject(new Dictionary<string, string> { { "passcode", _settings.Passcode } });
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
        /// cookie を付けて送る。**401 なら1度だけ入り直して、同じ頼みをもう1度送る**。
        /// 入り直しに失敗したら、最初の 401 をそのまま返す（呼び出し側は「繋がらない」と同じに扱える）。
        /// </summary>
        private async UniTask<HttpResponse> SendWithAuthAsync(
            string method, string url, string body, CancellationToken token)
        {
            if (!IsConfigured)
            {
                return HttpResponse.Offline;
            }

            // まだ一度も入っていなければ先に入る（起動時のログイン。設計 §8）。
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

        private IReadOnlyDictionary<string, string> AuthHeaders()
        {
            var headers = new Dictionary<string, string>();
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
