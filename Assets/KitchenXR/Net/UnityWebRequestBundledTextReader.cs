using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace KitchenXR.Net
{
    /// <summary>
    /// 実機・Editor で使う <see cref="IBundledTextReader"/>。
    /// Android の <c>StreamingAssets</c> は APK の中なので <c>UnityWebRequest</c> で読む
    /// （Windows の Editor では素のパスがそのまま通る）。
    /// </summary>
    public sealed class UnityWebRequestBundledTextReader : IBundledTextReader
    {
        private readonly int _timeoutSeconds;

        public UnityWebRequestBundledTextReader(int timeoutSeconds = 10)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        public async UniTask<string> ReadAsync(string path, CancellationToken token)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            using var request = UnityWebRequest.Get(path);
            request.timeout = _timeoutSeconds;

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KitchenXR] 同梱テキストを読めませんでした: {path} ({e.Message})");
                return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[KitchenXR] 同梱テキストを読めませんでした: {path} ({request.error})");
                return null;
            }

            return request.downloadHandler?.text;
        }
    }
}
