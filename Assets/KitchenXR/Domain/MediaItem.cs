namespace KitchenXR.Domain
{
    /// <summary>
    /// 動画の一覧の1件。一覧の正は manor（`GET /api/v1/kitchen/media`）で、同梱の
    /// `StreamingAssets/media.json` は manor 未設定のときの見本。Web で書き換える前提なので
    /// 型はこれ以上増やさない。
    ///
    /// <see cref="ThumbnailUrl"/> は必ず埋まっている——manor が `thumbnail_url` を返さなくても
    /// <see cref="MediaJson"/> が YouTube の既定の絵を組み立てるので、板は「絵が無い行」を
    /// 気にしなくてよい。
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
