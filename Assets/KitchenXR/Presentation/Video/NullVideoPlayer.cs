using System.Collections.Generic;

namespace KitchenXR.Presentation.Video
{
    /// <summary>
    /// 動画を出さない <see cref="IVideoPlayer"/>。
    ///
    /// WebView は Android のプラグイン（`Assets/TLab/TLabWebView/Plugins/Android`）なので
    /// Editor では絵が出ない。Editor ではこれを挿しておき、板は「動画は実機で」の札を出す。
    /// 一覧もボタンもそのまま動く——頼まれたことを覚えるだけなので、PlayMode 試験の
    /// 「選ぶと Load が呼ばれる」の受け手も兼ねる。
    /// </summary>
    public sealed class NullVideoPlayer : IVideoPlayer
    {
        private readonly List<string> _loaded = new List<string>();

        public bool IsAvailable => false;

        /// <summary>試験が差し替える（実機の WebView が返す「再生中」の代わり）。</summary>
        public bool IsReportedPlaying { get; set; }

        public string StatusLine => "Editor（動画は実機で）";

        /// <summary>試験が差し替える（実機の「html 読込済」の代わり）。</summary>
        public bool IsHtmlLoaded { get; set; }

        /// <summary>
        /// 試験が差し替える（実機の <c>TLabWebView.GetUrl()</c> の代わり）。
        /// ここに youtube.com の URL を置くと、板が「関連動画へ出た」と見なして連れ戻す。
        /// </summary>
        public string CurrentUrl { get; set; }

        public string LastError { get; set; }

        /// <summary>頼まれた順の動画 id（試験が見る）。</summary>
        public IReadOnlyList<string> LoadedVideoIds => _loaded;

        public string LastVideoId { get; private set; }

        public int PlayCount { get; private set; }

        public int PauseCount { get; private set; }

        public int TapCount { get; private set; }

        public int ReloadHtmlCount { get; private set; }

        public int SuppressPageScrollCount { get; private set; }

        public int Volume { get; private set; } = -1;

        public VideoAspect Aspect { get; private set; } = VideoAspect.Landscape;

        public void Load(string videoId)
        {
            LastVideoId = videoId;
            _loaded.Add(videoId);
        }

        public void Play() => PlayCount++;

        public void Pause() => PauseCount++;

        public void SetVolume(int volume) => Volume = volume;

        public void SetAspect(VideoAspect aspect) => Aspect = aspect;

        public void TapCenter()
        {
            TapCount++;
            Tap(0.5f, 0.5f);
        }

        /// <summary>窓への触りの記録（PlayMode 試験が見る。段と、絵の中の比の位置）。</summary>
        public readonly struct TouchRecord
        {
            public TouchRecord(VideoTouchPhase phase, float u, float v)
            {
                Phase = phase;
                U = u;
                V = v;
            }

            public VideoTouchPhase Phase { get; }

            public float U { get; }

            public float V { get; }

            public override string ToString() => $"{Phase}({U:0.000}, {V:0.000})";
        }

        private readonly List<TouchRecord> _touches = new List<TouchRecord>();
        private readonly List<TouchRecord> _taps = new List<TouchRecord>();

        /// <summary>頼まれた順の触り（試験が見る）。</summary>
        public IReadOnlyList<TouchRecord> Touches => _touches;

        /// <summary>「叩いた」だけを抜いたもの（試験が見る。段は常に Down）。</summary>
        public IReadOnlyList<TouchRecord> Taps => _taps;

        public int GoBackCount { get; private set; }

        public void Touch(VideoTouchPhase phase, float u, float v) =>
            _touches.Add(new TouchRecord(phase, u, v));

        /// <summary>
        /// 実機の <see cref="YoutubePlayerBridge.Tap"/> は 80ms 空けて押し上げるが、
        /// ここでは待たずに両方を積む——試験が「同じ座標で Down → Up が届いた」を
        /// フレームを跨がずに確かめられるように。
        /// </summary>
        public void Tap(float u, float v)
        {
            _taps.Add(new TouchRecord(VideoTouchPhase.Down, u, v));
            _touches.Add(new TouchRecord(VideoTouchPhase.Down, u, v));
            _touches.Add(new TouchRecord(VideoTouchPhase.Up, u, v));
        }

        public void GoBack() => GoBackCount++;

        public void ReloadHtml() => ReloadHtmlCount++;

        public void SuppressPageScroll() => SuppressPageScrollCount++;
    }
}
