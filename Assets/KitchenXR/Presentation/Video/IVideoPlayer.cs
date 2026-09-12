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

        /// <summary>
        /// 窓を触った場所をそのまま WebView へ渡す（2026-09-13 主人との相談で決めた方針3
        /// 「次の動画は埋め込みプレイヤー自身の関連動画で選ぶ。動画の窓への触りをレイ（とポーク）で通す」）。
        ///
        /// <paramref name="u"/>・<paramref name="v"/> は**絵の中の位置**（0〜1。u は左→右、
        /// v は上→下）。WebView の論理ピクセルへ写すのは包む側の仕事で、板は比だけを渡す
        /// ——解像度は向き（16:9 ⇄ 9:16）で変わるので、板が知る必要が無い。
        ///
        /// **ここは釦ではない。** <see cref="PokePress"/> の「押し下げで発火・600ms 間引き」は
        /// 通さず、触った通りに Down／Drag／Up を素直に流す（関連動画を選ぶには
        /// WebView 側がクリックと見なす一連の触りが要る）。
        /// </summary>
        void Touch(VideoTouchPhase phase, float u, float v);

        /// <summary>
        /// WebView の履歴を1つ戻る（<c>TLabWebView.GoBack</c>）。
        /// 関連動画を触った先で youtube.com 本体へ遷移することがあるため、操作部に逃げ道を置く。
        /// </summary>
        void GoBack();
    }
}
