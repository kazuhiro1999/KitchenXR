using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using UnityEngine;

namespace KitchenXR.Net
{
    /// <summary>
    /// 動画の一覧（`media.json`）のローカル保管庫（設計 §6・ROADMAP P4）。
    ///
    /// レシピと同じ流儀（<see cref="RecipeStore"/>）:
    /// **読むのは常に <c>Application.persistentDataPath/media.json</c>**。
    /// そこに無い初回だけ、アプリに同梱した <c>StreamingAssets/media.json</c> を写す。
    /// 以後は主人が PC から（adb push などで）そちらを書き換える——
    /// アプリを入れ直さずに一覧を変えられるのが狙い（書き方は `Docs/media-json.md`）。
    ///
    /// 壊れた JSON でも落とさない: 読めた行だけを返す（<see cref="MediaJson"/>）。
    /// </summary>
    public sealed class MediaStore
    {
        public const string FileName = "media.json";

        private readonly string _localPath;
        private readonly string _bundledPath;
        private readonly IBundledTextReader _reader;

        public MediaStore(string localPath, string bundledPath, IBundledTextReader reader)
        {
            _localPath = localPath;
            _bundledPath = bundledPath;
            _reader = reader;
        }

        /// <summary>実機・Editor で使う既定の保管庫。</summary>
        public static MediaStore CreateDefault() =>
            new MediaStore(
                Path.Combine(Application.persistentDataPath, FileName),
                Path.Combine(Application.streamingAssetsPath, FileName),
                new UnityWebRequestBundledTextReader());

        /// <summary>主人が書き換える側のファイル（実機では persistentDataPath）。</summary>
        public string LocalPath => _localPath;

        /// <summary>アプリに同梱した見本（StreamingAssets）。</summary>
        public string BundledPath => _bundledPath;

        public bool HasLocalFile => File.Exists(_localPath);

        /// <summary>
        /// 一覧を読む。手元に無ければ同梱の見本を写してから読む。
        /// どちらも読めなければ空の一覧（板は「一覧がありません」を出す）。
        /// </summary>
        public async UniTask<IReadOnlyList<MediaItem>> LoadAsync(CancellationToken token = default)
        {
            if (!File.Exists(_localPath))
            {
                var bundled = _reader != null ? await _reader.ReadAsync(_bundledPath, token) : null;
                if (!string.IsNullOrWhiteSpace(bundled))
                {
                    Save(bundled);
                }
            }

            if (!File.Exists(_localPath))
            {
                Debug.LogWarning($"[KitchenXR] 動画の一覧がありません: {_localPath}");
                return new List<MediaItem>();
            }

            string json;
            try
            {
                json = File.ReadAllText(_localPath, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 動画の一覧を読めませんでした: {_localPath} ({e.Message})");
                return new List<MediaItem>();
            }

            return MediaJson.Parse(json);
        }

        /// <summary>同梱の見本を手元へ写す（初回だけ）。</summary>
        public void Save(string json)
        {
            var directory = Path.GetDirectoryName(_localPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_localPath, json, Encoding.UTF8);
        }
    }
}
