using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Net;
using UnityEngine;

namespace KitchenXR.Presentation.Video
{
    /// <summary>
    /// 一覧の行に出す絵（サムネイル）の保管庫（2026-09-13 主人の実機確認 v1.0.9）。
    ///
    /// 主人「再生リストは…右横に置いて縦スクロールできた方がいいかも（YouTube を Web で見るときの
    /// 画面みたいにサムネ＋タイトル）」。絵は動画 id ごとに決まる場所にあり（<c>i.ytimg.com</c>）、
    /// 一度取れば変わらないので**手元に置いて次からは通信しない**。
    ///
    /// 流儀は <see cref="RecipeStore"/> の画像と同じ（設計 §11 追補）——
    /// 通信は <see cref="IRecipeImageDownloader"/> の裏（試験ではこれを差し替える）、
    /// 置き場は <c>persistentDataPath/media-thumbs/&lt;video_id&gt;.jpg</c>、
    /// **失敗は黙る**（絵が出ないだけで一覧は選べる。台所で通信が切れるのは普通のこと）。
    ///
    /// <see cref="RecipeStore"/> をそのまま使わないのは、あちらが「レシピ id ＋ 画像の鍵」で
    /// 掘る作りで、`Net/` は別担当の持ち物だから（動画のために触りたくない）。
    /// 取ってくる口（<see cref="IRecipeImageDownloader"/>）だけ借りる。
    ///
    /// **作った <see cref="Texture2D"/> は呼び出し側のもの**——板が消えるときに
    /// <see cref="VideoPanel"/> が捨てる（ここは持ち続けない。板を作り直すたびに増えていく）。
    /// </summary>
    public sealed class MediaThumbnailCache
    {
        /// <summary>置き場のフォルダ名（<c>persistentDataPath</c> の下）。</summary>
        public const string DirectoryName = "media-thumbs";

        private readonly string _rootDirectory;
        private readonly IRecipeImageDownloader _downloader;

        /// <summary>一度失敗した URL は二度と叩かない（一覧を並べ直すたびに待たされないため）。</summary>
        private readonly HashSet<string> _failedUrls = new HashSet<string>();

        /// <summary>同じ絵を2つの行から頼まれたときに通信を1回にする。</summary>
        private readonly Dictionary<string, UniTask<bool>> _inFlight =
            new Dictionary<string, UniTask<bool>>();

        public MediaThumbnailCache(string rootDirectory, IRecipeImageDownloader downloader)
        {
            _rootDirectory = rootDirectory;
            _downloader = downloader;
        }

        /// <summary>実機・Editor で使う既定の保管庫。</summary>
        public static MediaThumbnailCache CreateDefault() =>
            new MediaThumbnailCache(
                Path.Combine(Application.persistentDataPath, DirectoryName),
                new UnityWebRequestImageDownloader());

        /// <summary>絵の置き場（中身は取得したそのまま。<c>Texture2D.LoadImage</c> が形を見分ける）。</summary>
        public string PathFor(string videoId) =>
            Path.Combine(_rootDirectory, Sanitize(videoId) + ".jpg");

        public bool HasLocal(string videoId) => File.Exists(PathFor(videoId));

        /// <summary>
        /// 絵を1枚。手元にあれば通信しない。取れなければ <see langword="null"/>（行は下地のまま）。
        /// </summary>
        public async UniTask<Texture2D> LoadAsync(
            string videoId, string url, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(videoId))
            {
                return null;
            }

            var path = PathFor(videoId);
            if (!File.Exists(path) && !await EnsureLocalAsync(path, url, token))
            {
                return null;
            }

            return ReadTexture(path);
        }

        private async UniTask<bool> EnsureLocalAsync(string path, string url, CancellationToken token)
        {
            if (string.IsNullOrEmpty(url) || _downloader == null || _failedUrls.Contains(url))
            {
                return false;
            }

            if (_inFlight.TryGetValue(path, out var running))
            {
                return await running;
            }

            var task = DownloadAsync(path, url, token);
            _inFlight[path] = task;

            try
            {
                return await task;
            }
            finally
            {
                _inFlight.Remove(path);
            }
        }

        private async UniTask<bool> DownloadAsync(string path, string url, CancellationToken token)
        {
            var bytes = await _downloader.GetBytesAsync(url, token);
            if (bytes == null || bytes.Length == 0)
            {
                _failedUrls.Add(url);
                return false;
            }

            try
            {
                Directory.CreateDirectory(_rootDirectory);
                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch (IOException)
            {
                // 書けなくても絵は出せる……が、次も通信することになる。黙って諦める（設計 §7 の流儀）。
                _failedUrls.Add(url);
                return false;
            }
        }

        private static Texture2D ReadTexture(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                return null;
            }

            // mipmap は要らない（板の行の大きさは決まっている）。linear=false は sRGB のまま。
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(bytes))
            {
                return texture;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }

            return null;
        }

        /// <summary>id はファイル名になるので、余計な記号を落とす（<c>MediaJson</c> が既に絞っているが念のため）。</summary>
        private static string Sanitize(string value)
        {
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }
}
