namespace KitchenXR.Domain
{
    /// <summary>
    /// 動画の一覧の1件（設計 §6・ROADMAP P4）。
    ///
    /// 一覧は manor には置かない——**クライアントのローカル設定**（`StreamingAssets/media.json`）で持つ
    /// （設計 §2「動画の URL 一覧は manor に置かず、クライアントのローカル設定で持つ」）。
    /// 主人が PC から書き換える前提なので、型はこれ以上増やさない（題名と動画の id だけ）。
    /// </summary>
    public sealed class MediaItem
    {
        public MediaItem(string title, string videoId)
        {
            Title = title;
            VideoId = videoId;
        }

        /// <summary>板に出す題名。</summary>
        public string Title { get; }

        /// <summary>YouTube の動画 id（`https://www.youtube.com/watch?v=<これ>`）。</summary>
        public string VideoId { get; }

        public override string ToString() => $"{Title} ({VideoId})";
    }
}
