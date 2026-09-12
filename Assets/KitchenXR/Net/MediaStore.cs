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
        /// <summary>
        /// 前回写した同梱の中身の控え（<c>media.bundled.json</c>）。
        /// 同梱（APK）を入れ替えたのか、主人が端末側を直したのかを見分けるために持つ。
        /// </summary>
        public string BundledCopyPath => Path.Combine(
            Path.GetDirectoryName(_localPath) ?? string.Empty, BundledCopyFileName);

        public const string BundledCopyFileName = "media.bundled.json";

        /// <summary>
        /// 一覧を読む。手元（<c>persistentDataPath/media.json</c>）が正。
        ///
        /// 2026-09-13 主人の実機確認（v1.0.7）「bbno$ を追加したはずが一覧に無く、消したはずの
        /// 2018 が残っていた」——主人は同梱の見本（StreamingAssets）を直して APK を入れ直したが、
        /// v1.0.7 までは**初回だけ**同梱を写し、以後は手元しか見なかった。
        /// 直し: 毎回同梱も読み、前回写した控えと違えば「APK 側が変わった」と見なす。
        /// そのとき手元が控えと同じ（＝端末側は誰も直していない）なら新しい同梱で置き換え、
        /// 手元が控えと違う（＝主人が adb で端末側を直した）なら手元を守る。
        /// 控えが無い（v1.0.7 以前から上げた）ときは**同梱を正とする**——主人が端末側を直した形跡
        /// （控え）が無いのに手元を守ると、APK を入れ替えても一覧が変わらない（v1.0.8 で主人が
        /// まさにそれを踏んだ）。
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
                // 初回、または控えが無い（古い版から上げた）。同梱を正とする。
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
