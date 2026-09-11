using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace KitchenXR.Net
{
    /// <summary>
    /// 実機で使う取得口。<c>UnityWebRequestTexture</c> で取り、**そのままの中身**を返す
    /// （<see cref="RecipeStore"/> がそれをローカルへ保存する。次からは通信しない。設計 §11 追補）。
    /// 電子レンジ等で通信が切れているときは null を返し、呼び出し側が札で代える。
    /// </summary>
    public sealed class UnityWebRequestImageDownloader : IRecipeImageDownloader
    {
        private readonly int _timeoutSeconds;

        public UnityWebRequestImageDownloader(int timeoutSeconds = 10)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        public async UniTask<byte[]> GetBytesAsync(string url, CancellationToken token)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            using var request = UnityWebRequestTexture.GetTexture(url);
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
                Debug.LogWarning($"[KitchenXR] 画像の取得に失敗しました（保存済みが無ければ札で代えます）: {url} ({e.Message})");
                return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[KitchenXR] 画像の取得に失敗しました: {url} ({request.error})");
                return null;
            }

            var bytes = request.downloadHandler?.data;
            if (bytes != null && bytes.Length > 0)
            {
                return bytes;
            }

            // DownloadHandlerTexture が生のバイト列を手放していたときの保険。
            var texture = DownloadHandlerTexture.GetContent(request);
            return texture != null ? texture.EncodeToPNG() : null;
        }
    }
}
