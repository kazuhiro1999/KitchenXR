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
    /// 動画の一覧（`media.json`）のローカル保管庫。
    ///
    /// レシピと同じ流儀（<see cref="RecipeStore"/>）: 読むのは常に
    /// <c>Application.persistentDataPath/media.json</c>。そこに無い初回だけ、アプリに同梱した
    /// <c>StreamingAssets/media.json</c> を写す。以後は PC から（adb push などで）そちらを
    /// 書き換える——アプリを入れ直さずに一覧を変えられるのが狙い。
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

        /// <summary>後から書き換える側のファイル（実機では persistentDataPath）。</summary>
        public string LocalPath => _localPath;

        /// <summary>アプリに同梱した見本（StreamingAssets）。</summary>
        public string BundledPath => _bundledPath;

        public bool HasLocalFile => File.Exists(_localPath);

        /// <summary>
        /// 前回写した同梱の中身の控え（<c>media.bundled.json</c>）。
        /// 同梱（APK）を入れ替えたのか、端末側を直したのかを見分けるために持つ。
        /// </summary>
        public string BundledCopyPath => Path.Combine(
            Path.GetDirectoryName(_localPath) ?? string.Empty, BundledCopyFileName);

        public const string BundledCopyFileName = "media.bundled.json";

        /// <summary>
        /// 一覧を読む。手元（<c>persistentDataPath/media.json</c>）が正。
        ///
        /// 毎回同梱（StreamingAssets）も読み、前回写した控えと違えば「APK 側が変わった」と見なす。
        /// そのとき手元が控えと同じ（＝端末側は誰も直していない）なら新しい同梱で置き換え、
        /// 手元が控えと違う（＝adb で端末側を直した）なら手元を守る。控えが無いときは同梱を正とする
        /// ——端末側を直した形跡が無いのに手元を守ると、APK を入れ替えても一覧が変わらない。
        ///
        /// manor に繋がるときは、この後で <see cref="SaveRemote"/> が manor の一覧で手元を上書きする
        /// （manor が正。同梱は manor 未設定のときの見本）。
        /// </summary>
        public async UniTask<IReadOnlyList<MediaItem>> LoadAsync(CancellationToken token = default)
        {
            var bundled = _reader != null ? await _reader.ReadAsync(_bundledPath, token) : null;
            if (!string.IsNullOrWhiteSpace(bundled))
            {
                ReconcileBundled(bundled);
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
        private void ReconcileBundled(string bundled)
        {
            var previousCopy = ReadOrNull(BundledCopyPath);
            var local = ReadOrNull(_localPath);

            if (local == null || previousCopy == null)
            {
                // 初回、または控えが無い。同梱を正とする。
                if (local != bundled)
                {
                    Save(bundled);
                }
            }
            else if (previousCopy != bundled && local == previousCopy)
            {
                Save(bundled); // APK 側が変わり、端末側は手つかず。
                Debug.Log("[KitchenXR] 動画の一覧: 同梱が新しくなったので手元を入れ替えました。");
            }
            else if (previousCopy != bundled)
            {
                Debug.Log("[KitchenXR] 動画の一覧: 同梱も端末側も変わっているので、端末側を残します。");
            }

            if (previousCopy != bundled)
            {
                WriteText(BundledCopyPath, bundled);
            }
        }

        private static string ReadOrNull(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static void WriteText(string path, string text)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, text, Encoding.UTF8);
        }

        /// <summary>
        /// manor から取れた一覧を手元へ写す（manor が正）。以後、圏外なら <see cref="LoadAsync"/> が
        /// これを読む。同梱の控えには触らない——次に同梱が変わっても、manor の一覧は守られる
        /// （手元 ≠ 控え なので「端末側が直されている」側に倒れる）。
        /// </summary>
        public IReadOnlyList<MediaItem> SaveRemote(string json)
        {
            var items = MediaJson.Parse(json);
            if (!string.IsNullOrWhiteSpace(json))
            {
                Save(json);
            }

            return items;
        }

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
