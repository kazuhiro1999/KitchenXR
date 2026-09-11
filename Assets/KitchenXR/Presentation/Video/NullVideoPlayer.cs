using System.Collections.Generic;

namespace KitchenXR.Presentation.Video
{
    /// <summary>
    /// 動画を出さない <see cref="IVideoPlayer"/>。
    ///
    /// WebView は Android のプラグイン（`Assets/TLab/TLabWebView/Plugins/Android`）なので
    /// Editor では絵が出ない。Editor ではこれを挿しておき、板は「動画は実機で」の札を出す。
    /// 一覧もボタンも**そのまま動く**——頼まれたことを覚えるだけなので、PlayMode 試験の
    /// 「選ぶと Load が呼ばれる」の受け手も兼ねる。
    /// </summary>
    public sealed class NullVideoPlayer : IVideoPlayer
    {
        private readonly List<string> _loaded = new List<string>();

        public bool IsAvailable => false;

        /// <summary>頼まれた順の動画 id（試験が見る）。</summary>
        public IReadOnlyList<string> LoadedVideoIds => _loaded;

        public string LastVideoId { get; private set; }

        public int PlayCount { get; private set; }

        public int PauseCount { get; private set; }

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
    }
}
