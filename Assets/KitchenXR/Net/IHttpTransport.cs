using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace KitchenXR.Net
{
    /// <summary>
    /// HTTP の1往復。<see cref="ManorClient"/> がネットに触れる唯一の入口で、
    /// 試験ではこれを差し替える（EditMode 試験は本物の manor へ繋がない）。
    /// <see cref="IRecipeImageDownloader"/> と同じ役回り。
    /// </summary>
    public interface IHttpTransport
    {
        /// <summary>
        /// 送って返す。**例外は投げない**——通信そのものが成立しなかったときは
        /// <see cref="HttpResponse.Offline"/>（status 0）を返す。
        /// オフラインは異常ではなく前提なので、呼び出し側は分岐で扱う（設計 §11 追補）。
        /// </summary>
        UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken token);
    }

    /// <summary>1回分の頼み。</summary>
    public sealed class HttpRequest
    {
        public string Method { get; }
        public string Url { get; }

        /// <summary>付ける見出し（<c>Cookie</c> はここに入る。Android の自動 cookie には頼らない）。</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>JSON の本体。GET など本体の無い頼みでは null。</summary>
        public string JsonBody { get; }

        public HttpRequest(
            string method, string url,
            IReadOnlyDictionary<string, string> headers = null, string jsonBody = null)
        {
            Method = method;
            Url = url;
            Headers = headers ?? new Dictionary<string, string>();
            JsonBody = jsonBody;
        }
    }

    /// <summary>1回分の返り。</summary>
    public sealed class HttpResponse
    {
        /// <summary>HTTP の状態。**0 は「繋がらなかった」**（サーバの答えではない）。</summary>
        public int StatusCode { get; }

        public string Body { get; }

        /// <summary>返ってきた見出し（<c>Set-Cookie</c> をここから拾う）。鍵は大文字小文字を区別しない。</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        public HttpResponse(int statusCode, string body, IReadOnlyDictionary<string, string> headers = null)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;

            // 見出しの鍵は大文字小文字を区別しない（UnityWebRequest は "Set-Cookie" を返すが、
            // 経路によっては "set-cookie" のこともある）。渡されたものをここで写し替えて揃える。
            var normalized = EmptyHeaders();
            if (headers != null)
            {
                foreach (var pair in headers)
                {
                    normalized[pair.Key] = pair.Value;
                }
            }

            Headers = normalized;
        }

        /// <summary>繋がらなかった（機内モード・電子レンジ・manor が寝ている）。</summary>
        public static HttpResponse Offline => new HttpResponse(0, string.Empty);

        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

        public bool IsUnauthorized => StatusCode == 401;

        /// <summary>繋がらなかったのか（サーバの答えが無い）。</summary>
        public bool IsOffline => StatusCode == 0;

        /// <summary>見出しを1つ引く（大文字小文字を区別しない）。無ければ空。</summary>
        public string Header(string name)
        {
            if (Headers == null || string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return Headers.TryGetValue(name, out var value) ? value ?? string.Empty : string.Empty;
        }

        /// <summary>大文字小文字を区別しない、空の見出し表。</summary>
        public static Dictionary<string, string> EmptyHeaders() =>
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}
