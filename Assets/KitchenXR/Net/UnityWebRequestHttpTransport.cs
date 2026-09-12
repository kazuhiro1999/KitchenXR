using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace KitchenXR.Net
{
    /// <summary>
    /// 実機で使う HTTP の口（<see cref="UnityWebRequest"/>）。
    ///
    /// cookie は自分で扱う。<see cref="UnityWebRequest"/> は Android ではプラットフォームの
    /// cookie 入れを使い回すことがあり、いつ消えるか・いつ付くかが読めない。ここでは
    /// <c>Set-Cookie</c> をそのまま呼び出し側（<see cref="ManorClient"/>）へ渡すだけにして、
    /// 保持と付け直しは全部あちらの仕事にする——そうすれば試験でも同じ道が通る。
    ///
    /// 例外は投げない。繋がらなければ <see cref="HttpResponse.Offline"/>（status 0）
    /// ——電子レンジで Wi-Fi が切れるのは異常ではなく前提。
    /// </summary>
    public sealed class UnityWebRequestHttpTransport : IHttpTransport
    {
        private readonly int _timeoutSeconds;

        public UnityWebRequestHttpTransport(int timeoutSeconds = 8)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        public async UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken token)
        {
            if (request == null || string.IsNullOrEmpty(request.Url))
            {
                return HttpResponse.Offline;
            }

            using var uwr = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds,
            };

            if (!string.IsNullOrEmpty(request.JsonBody))
            {
                uwr.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.JsonBody));
                uwr.SetRequestHeader("Content-Type", "application/json");
            }

            foreach (var header in request.Headers)
            {
                uwr.SetRequestHeader(header.Key, header.Value);
            }

            try
            {
                await uwr.SendWebRequest().ToUniTask(cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnityWebRequestException)
            {
                // 4xx・5xx もここへ来る（UniTask は成功以外を例外にする）。
                // サーバが答えているのか繋がっていないのかは responseCode で見分ける。
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KitchenXR] manor への通信に失敗しました: {request.Url} ({e.Message})");
                return HttpResponse.Offline;
            }

            if (uwr.result == UnityWebRequest.Result.ConnectionError || uwr.responseCode == 0)
            {
                return HttpResponse.Offline;
            }

            var headers = HttpResponse.EmptyHeaders();
            var responseHeaders = uwr.GetResponseHeaders();
            if (responseHeaders != null)
            {
                foreach (var pair in responseHeaders)
                {
                    headers[pair.Key] = pair.Value;
                }
            }

            return new HttpResponse((int)uwr.responseCode, uwr.downloadHandler?.text, headers);
        }
    }
}
