namespace KitchenXR.Presentation.Video
{
    /// <summary>
    /// 動画の板が動画そのものに頼むこと。
    ///
    /// TLab の <c>YoutubePlayer</c>（＝<c>TLabWebView</c>）はこの口の裏にだけ現れる
    /// （<see cref="YoutubePlayerBridge"/>）。板は WebView を知らないので、
    /// Editor では <see cref="NullVideoPlayer"/> を挿しておけば一覧もボタンも同じように動く。
    ///
    /// 文字入力は置かないので、開く動画は一覧から選ぶ <see cref="Load"/> だけ。
    ///
    /// <see cref="TapCenter"/> があるのは、Android の WebView が人の操作を伴わない再生を拒む
    /// ことがあるため（<c>mediaPlaybackRequiresUserGesture</c>）。JS の <c>play()</c> で反応が
    /// 無いとき、板が WebView の中央に「触った」ことにして再生の許しを得る。
    /// </summary>
    public interface IVideoPlayer
    {
        /// <summary>実際に絵が出るか（Android の実機だけ true。Editor では札を出す）。</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 動画の側が「再生中」または「読み込み中」だと自分で報告しているか
        /// （板の押した／押していないの覚えではなく、WebView から返ってきた状態）。
        /// </summary>
        bool IsReportedPlaying { get; }

        /// <summary>中の様子の一行（WebView の状態・HTML の読込・プレイヤーの状態）。札に出す。</summary>
        string StatusLine { get; }

        /// <summary>
        /// <c>youtube.html</c> が読み込み終わっているか。
        /// 板はこれが立った瞬間に <see cref="SuppressPageScroll"/> を頼む
        /// （読み込みの前に JS を送っても消える）。
        /// </summary>
        bool IsHtmlLoaded { get; }

        /// <summary>
        /// 今 WebView が開いている URL（実機だけ。取れなければ <see langword="null"/>）。
        /// 板が 0.5 秒ごとにこれを読み、同梱の html（<c>http://localhost</c>）以外へ出ていたら
        /// id を抜いて埋め込みプレイヤーへ連れ戻す（<see cref="ReloadHtml"/> → <see cref="Load"/>）。
        /// </summary>
        string CurrentUrl { get; }

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
        /// 窓を触った場所をそのまま WebView へ渡す（次の動画は埋め込みプレイヤー自身の
        /// 関連動画で選ぶ）。
        ///
        /// <paramref name="u"/>・<paramref name="v"/> は絵の中の位置（0〜1。u は左→右、
        /// v は上→下）。WebView の論理ピクセルへ写すのは包む側の仕事で、板は比だけを渡す
        /// ——解像度は向き（16:9 ⇄ 9:16）で変わるので、板が知る必要が無い。
        ///
        /// ここは釦ではない。<see cref="PokePress"/> の「押し下げで発火・600ms 間引き」は
        /// 通さず、触った通りに Down／Drag／Up を素直に流す（関連動画を選ぶには
        /// WebView 側がクリックと見なす一連の触りが要る）。
        /// </summary>
        void Touch(VideoTouchPhase phase, float u, float v);

        /// <summary>
        /// 窓の1点を叩く（押し下げ → 80ms → 押し上げを同じ座標で）。
        ///
        /// 窓の <c>PointerDown</c>／<c>Move</c>／<c>Up</c> をそのまま DOWN／DRAG／UP で流すと、
        /// レイの着地点もポークの指先も揺れるので押し下げと押し上げの間に DRAG が挟まり、
        /// WebView が「スクロール」と見なしてクリックを出さない（横スクロールはできるのに
        /// タップが効かない）。だから板はタップと判定できたときだけこれを呼び、DRAG を混ぜない。
        ///
        /// <paramref name="u"/>・<paramref name="v"/> は <see cref="Touch"/> と同じ絵の中の比。
        /// </summary>
        void Tap(float u, float v);

        /// <summary>
        /// WebView の履歴を1つ戻る（<c>TLabWebView.GoBack</c>）。
        /// 関連動画を触った先で youtube.com 本体へ遷移することがあるため、操作部に逃げ道を置く。
        /// </summary>
        void GoBack();

        /// <summary>
        /// <c>youtube.html</c> を読み直す（埋め込みプレイヤーへ戻す）。
        /// 関連動画が top frame を <c>youtube.com/watch</c> へ運んでしまうと
        /// html の JS（<c>loadVideo</c>・<c>play</c>）が丸ごと消えるので、
        /// <see cref="Load"/> の前にこれで舞台を作り直す。
        /// </summary>
        void ReloadHtml();

        /// <summary>
        /// ページ全体の縦スクロール（オーバースクロール）を止める JS を送る。
        /// html は <c>body { height:100%; overflow:hidden }</c> だが、Android の WebView は
        /// 端でオーバースクロールし、DRAG を流すとページごと動く。html は書き換えないので、
        /// 読み込み完了後と向きを変えるたびに JS で塞ぐ。
        /// iframe の中の横スクロール（＝関連動画の帯）は殺さない。
        /// </summary>
        void SuppressPageScroll();
    }
}
