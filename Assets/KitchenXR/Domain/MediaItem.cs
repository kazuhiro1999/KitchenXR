namespace KitchenXR.Domain
{
    /// <summary>
    /// 動画の一覧の1件（設計 §6・ROADMAP P4）。
    ///
    /// 一覧の正は manor（ADR-016。`GET /api/v1/kitchen/media`）で、同梱の
    /// `StreamingAssets/media.json` は manor 未設定のときの見本（設計 §11 追補「動画の一覧は manor が正」）。
    /// 主人が Web で書き換える前提なので、型はこれ以上増やさない。
    ///
    /// 2026-09-13 主人の実機確認（v1.0.9）「再生リストは…右横に置いて縦スクロールできた方がいいかも
    /// （YouTube を Web で見るときの画面みたいにサムネ＋タイトル）」——行に絵を出すため
    /// <see cref="ThumbnailUrl"/> を足した。manor が `thumbnail_url` を返さなくても
    /// <see cref="MediaJson"/> が YouTube の既定の絵（`i.ytimg.com/vi/&lt;id&gt;/hqdefault.jpg`）を組み立てるので、
    /// **ここは必ず埋まっている**（板は「無いかもしれない」を気にしなくてよい）。
    /// </summary>
    public sealed class MediaItem
    {
        public MediaItem(string title, string videoId, string thumbnailUrl = null)
        {
            Title = title;
            VideoId = videoId;
            ThumbnailUrl = thumbnailUrl;
        }

        /// <summary>板に出す題名。</summary>
        public string Title { get; }

        /// <summary>YouTube の動画 id（`https://www.youtube.com/watch?v=<これ>`）。</summary>
        public string VideoId { get; }

        /// <summary>一覧の行に出す絵の URL（無ければ null。取得と保存は板の側）。</summary>
        public string ThumbnailUrl { get; }

        public override string ToString() => $"{Title} ({VideoId})";
    }
}
