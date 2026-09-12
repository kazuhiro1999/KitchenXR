namespace KitchenXR.Presentation.Video
{
    /// <summary>
    /// 動画の板が動画そのものに頼むこと（設計 §6）。
    ///
    /// 主人の <c>YoutubePlayer</c>（＝<c>TLabWebView</c>）はこの口の裏にだけ現れる
    /// （<see cref="YoutubePlayerBridge"/>）。板は WebView を知らないので、
    /// Editor では <see cref="NullVideoPlayer"/> を挿しておけば一覧もボタンも同じように動く。
    ///
    /// 文字入力は置かない（設計 §6。主人「キーボード入力周りは若干使いにくかった」）ので、
    /// 開く動画は一覧から選ぶ <see cref="Load"/> だけ。
    ///
    /// 2026-09-13 主人の実機確認（v1.0.6）「再生を押しても反応がない。原因が分からないので
    /// エラーや失敗時にどこかに表示してほしい」——板が中の様子を札に出せるように
    /// <see cref="StatusLine"/> と <see cref="LastError"/> を足した。
    /// あわせて <see cref="TapCenter"/>: Android の WebView は**人の操作を伴わない再生**を
    /// 拒むことがある（<c>mediaPlaybackRequiresUserGesture</c>）。JS の <c>play()</c> で
    /// 反応が無いとき、板が WebView の中央に「触った」ことにして再生の許しを得る。
    /// </summary>
    public interface IVideoPlayer
    {
        /// <summary>実際に絵が出るか（Android の実機だけ true。Editor では札を出す）。</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 動画の側が「再生中」または「読み込み中」だと**自分で報告している**か
        /// （板の押した／押していないの覚えではなく、WebView から返ってきた状態）。
        /// </summary>
        bool IsReportedPlaying { get; }

        /// <summary>中の様子の一行（WebView の状態・HTML の読込・プレイヤーの状態）。札に出す。</summary>
        string StatusLine { get; }

        /// <summary>最後に起きた失敗（無ければ null）。札に出す。</summary>
        string LastError { get; }

        /// <summary>動画を読み込んで再生する。</summary>
        void Load(string videoId);

        void Play();

        void Pause();

        /// <summary>0〜100。</summary>
        void SetVolume(int volume);

        /// <summary>16:9 ⇄ 9:16。WebView 側の解像度と HTML の器も合わせる。</summary>
        void SetAspect(VideoAspect aspect);

        /// <summary>WebView のページの中央を一度タップする（人の操作の代わり）。</summary>
        void TapCenter();
    }
}
