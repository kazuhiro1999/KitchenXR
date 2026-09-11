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
    /// </summary>
    public interface IVideoPlayer
    {
        /// <summary>実際に絵が出るか（Android の実機だけ true。Editor では札を出す）。</summary>
        bool IsAvailable { get; }

        /// <summary>動画を読み込んで再生する。</summary>
        void Load(string videoId);

        void Play();

        void Pause();

        /// <summary>0〜100。</summary>
        void SetVolume(int volume);

        /// <summary>16:9 ⇄ 9:16。WebView 側の解像度と HTML の器も合わせる。</summary>
        void SetAspect(VideoAspect aspect);
    }
}
