using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace KitchenXR.Domain
{
    /// <summary>
    /// `media.json` の読み（設計 §6・ROADMAP P4）。
    ///
    /// 形は `[{ "title": "…", "video_id": "…" }]` だけ。主人が PC のテキストエディタで直に書く
    /// ものなので、**壊れた行で全部を落とさない**——読めない行は黙って飛ばし、読めた行だけを返す
    /// （1文字のタイプミスで動画の板が丸ごと死ぬのが一番困る）。
    ///
    /// `video_id` は JavaScript の文字列に埋め込まれる（主人の <c>youtube.html</c> の
    /// <c>loadVideo('…')</c>）ので、**英数字と - _ 以外は通さない**。URL を貼られたときだけは
    /// 親切に id を取り出す（`watch?v=`・`youtu.be/`・`/shorts/`・`/embed/`）。
    /// </summary>
    public static class MediaJson
    {
        /// <summary>板に出す上限（設計 P4「最大 8 件」）。これを超えた分は読まない。</summary>
        /// <summary>manor の動画リストを読むようになった（ADR-016）ので 8 → 20。板の一覧は縦スクロールで受ける。</summary>
        public const int MaxItems = 20;

        /// <summary>題名の上限。長すぎる題名で行が崩れないように切る（表示の都合）。</summary>
        public const int MaxTitleLength = 40;

        /// <summary>YouTube の動画 id として通す形。JS へ埋めるので記号は入れない。</summary>
        private static readonly Regex VideoIdPattern = new Regex("^[A-Za-z0-9_-]{5,32}$", RegexOptions.Compiled);

        /// <summary>URL を貼られたときに id を取り出す（主人が PC から書き換える前提の親切）。</summary>
        private static readonly Regex UrlIdPattern = new Regex(
            @"(?:v=|youtu\.be/|/shorts/|/embed/|/live/)([A-Za-z0-9_-]{5,32})", RegexOptions.Compiled);

        /// <summary>
        /// manor が絵の URL を返さないときに組み立てる先（2026-09-13 主人の実機確認 v1.0.9）。
        /// YouTube のサムネイルは動画 id から決まる場所にあり、鍵も要らない。
        /// <c>hqdefault</c>（480×360）を選ぶのは、板の行が 9.2cm ＝ 46 UI px で
        /// <c>mqdefault</c>（320×180）でも足りるが、9:16 のショーツだと横が切れて見えるため
        /// ——大きい方を取って板の側で収める。
        /// </summary>
        public static string DefaultThumbnailUrl(string videoId) =>
            string.IsNullOrEmpty(videoId) ? null : $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";

        /// <summary>
        /// `media.json` の中身を読む。壊れていれば空の一覧を返す（例外は投げない）。
        /// </summary>
        public static IReadOnlyList<MediaItem> Parse(string json)
        {
            var items = new List<MediaItem>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return items;
            }

            JToken root;
            try
            {
                root = JToken.Parse(json);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // 丸ごと壊れている。板は「一覧がありません」を出す。
                return items;
            }

            // 配列そのもの、または { "items": [...] } のどちらでも読む。
            var array = root as JArray ?? (root as JObject)?["items"] as JArray;
            if (array == null)
            {
                return items;
            }

            foreach (var element in array)
            {
                if (items.Count >= MaxItems)
                {
                    break;
                }

                var item = ParseOne(element);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private static MediaItem ParseOne(JToken element)
        {
            if (!(element is JObject obj))
            {
                return null; // 文字列だけの行など。飛ばす。
            }

            var videoId = NormalizeVideoId(
                (string)obj["video_id"] ?? (string)obj["videoId"] ?? (string)obj["url"]);
            if (videoId == null)
            {
                return null;
            }

            var title = ((string)obj["title"] ?? string.Empty).Trim();
            if (title.Length == 0)
            {
                title = videoId; // 題名を書き忘れても選べるように。
            }

            if (title.Length > MaxTitleLength)
            {
                title = title.Substring(0, MaxTitleLength - 1) + "…";
            }

            // 絵の URL（ADR-016 の `thumbnail_url`）。無ければ id から組み立てる——
            // 板は「絵が無い行」を作らずに済み、manor の契約が育っても壊れない。
            var thumbnail = ((string)obj["thumbnail_url"] ?? (string)obj["thumbnailUrl"]
                             ?? (string)obj["thumbnail"] ?? string.Empty).Trim();
            if (thumbnail.Length == 0 || !thumbnail.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
            {
                thumbnail = DefaultThumbnailUrl(videoId);
            }

            return new MediaItem(title, videoId, thumbnail);
        }

        /// <summary>
        /// 動画の id を取り出す。そのままの id・URL のどちらでも受ける。
        /// 通せないものは <see langword="null"/>（その行は飛ばす）。
        /// </summary>
        public static string NormalizeVideoId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var value = raw.Trim();

            if (VideoIdPattern.IsMatch(value))
            {
                return value;
            }

            var match = UrlIdPattern.Match(value);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
