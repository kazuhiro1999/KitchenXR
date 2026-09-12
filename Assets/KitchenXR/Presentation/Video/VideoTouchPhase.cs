namespace KitchenXR.Presentation.Video
{
    /// <summary>
    /// 動画の窓への「触り」の段。
    ///
    /// 数をそのまま並べてあるのは偶然ではない——<c>TLabWebView.TouchEvent</c> の
    /// <c>eventNum</c>（<c>WebViewInputListener.WebTouchEvent</c> の DOWN=0・UP=1・DRAG=2）と
    /// 同じ番号にしてある。包む側（<c>YoutubePlayerBridge</c>）で数え直す手間を省くため。
    /// </summary>
    public enum VideoTouchPhase
    {
        /// <summary>指（レイ）が窓に触れた。</summary>
        Down = 0,

        /// <summary>離れた。</summary>
        Up = 1,

        /// <summary>触れたまま動いた。</summary>
        Drag = 2,
    }
}
