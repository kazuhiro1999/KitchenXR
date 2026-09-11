using System;
using System.Globalization;
using System.Reflection;
using Cysharp.Threading.Tasks;
using TLab.Android.WebView;
using UnityEngine;
using UnityEngine.UI;

namespace KitchenXR.Presentation.Video
{
    /// <summary>
    /// 主人の <c>YoutubePlayer.prefab</c>（`Assets/TLab/Youtube/`）を包む
    /// <see cref="IVideoPlayer"/>（設計 §6・ROADMAP P4）。
    ///
    /// **主人のコードは書き換えない。包む。**
    /// <see cref="KitchenXR"/> が <c>TLab.Android.WebView</c> に触れてよいのはこのフォルダだけで、
    /// それは EditMode 試験（<c>PlatformIsolationTests</c>）で検算している。
    ///
    /// 反射（リフレクション）を使っているのは意地悪ではなく、**そうするしかない**から:
    /// 主人の <c>YoutubePlayer.cs</c> は asmdef の無いフォルダに置かれているので
    /// <c>Assembly-CSharp</c> に入る。asmdef を持つ側（<c>KitchenXR.Runtime</c>）から
    /// <c>Assembly-CSharp</c> は参照できない（Unity の決まり）。
    /// 主人の側に asmdef を足せば型で呼べるようになるが、それは「書き換えない」の約束に反する。
    /// そこで **型の名前で引き当てて呼ぶ**。呼ぶのは4つだけ
    /// （<c>Load</c> / <c>Play</c> / <c>Pause</c> / <c>SetVolume</c>）で、
    /// 見つからなければ一度だけ警告を出して黙る——板の一覧とボタンは動き続ける。
    /// <c>TLabWebView</c> は asmdef（<c>com.tlabaltoh.webview.runtime</c>）を持つので、
    /// こちらは型でそのまま呼んでいる。
    ///
    /// 解像度について: 主人のプレハブは 1024x1024（正方形）で、<c>youtube.html</c> の器は
    /// 16:9 の比で上に寄るため、そのままだと板の下半分が空白になる。
    /// だから板を作るときに <see cref="Initialize"/> で 16:9 の解像度に直してから
    /// WebView を起こし、9:16 へ切り替えるときは <c>Resize</c> と
    /// （html の器を直す）<c>EvaluateJS</c> の両方を送る。
    /// </summary>
    public sealed class YoutubePlayerBridge : IVideoPlayer
    {
        /// <summary>主人のプレハブの置き場。ここ以外からは触らない。</summary>
        public const string PlayerPrefabPath = "Assets/TLab/Youtube/Prefab/YoutubePlayer.prefab";

        /// <summary>
        /// プレハブの根の名前。<c>youtube.html</c> が
        /// <c>unitySendMessage('YoutubePlayer', …)</c> でここへ返してくるので、
        /// シーンに置いた実体の名前を変えてはいけない（"(Clone)" も付けない）。
        /// </summary>
        public const string PlayerObjectName = "YoutubePlayer";

        // WebView の解像度。web が HTML の論理ピクセル、tex が板に貼るテクスチャ。
        private const int LandscapeWebWidth = 1280;
        private const int LandscapeWebHeight = 720;
        private const int LandscapeTexWidth = 640;
        private const int LandscapeTexHeight = 360;

        private const int PortraitWebWidth = 720;
        private const int PortraitWebHeight = 1280;
        private const int PortraitTexWidth = 360;
        private const int PortraitTexHeight = 640;

        private readonly GameObject _root;
        private readonly TLabWebView _webView;
        private readonly MonoBehaviour _player;

        private readonly MethodInfo _load;
        private readonly MethodInfo _play;
        private readonly MethodInfo _pause;
        private readonly MethodInfo _setVolume;

        private bool _warned;

        public YoutubePlayerBridge(GameObject root)
        {
            _root = root;
            if (_root == null)
            {
                return;
            }

            _webView = _root.GetComponentInChildren<TLabWebView>(true);
            _player = FindPlayerComponent(_root);

            var type = _player != null ? _player.GetType() : null;
            if (type != null)
            {
                _load = type.GetMethod("Load", new[] { typeof(string), typeof(bool) });
                _play = type.GetMethod("Play", Type.EmptyTypes);
                _pause = type.GetMethod("Pause", Type.EmptyTypes);
                _setVolume = type.GetMethod("SetVolume", new[] { typeof(float) });
            }
        }

        /// <summary>主人の <c>YoutubePlayer</c>（global namespace・Assembly-CSharp）を名前で引き当てる。</summary>
        private static MonoBehaviour FindPlayerComponent(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Name == PlayerObjectName)
                {
                    return behaviour;
                }
            }

            return null;
        }

        /// <summary>
        /// WebView は Android のネイティブプラグイン。Editor では絵が出ないので、
        /// 板は「動画は実機で」の札を出す（設計 P4）。
        /// </summary>
        public bool IsAvailable =>
            Application.platform == RuntimePlatform.Android && _player != null && _webView != null;

        /// <summary>
        /// 板が組み上がったところで一度だけ呼ぶ。
        /// 解像度を 16:9 に直し、要らない仕掛け（文字入力・uGUI のレイキャスト）を止めてから、
        /// 主人の <c>YoutubePlayer.Start()</c> が走るように実体を起こす。
        ///
        /// Android の実機でなければ**起こさない**——起こしても白い板が出るだけで、
        /// 主人の <c>Update()</c> が毎フレーム空振りする。
        /// </summary>
        public void Initialize()
        {
            if (_root == null)
            {
                return;
            }

            DisableTextInputAndRaycast();

            if (!IsAvailable)
            {
                _root.SetActive(false);
                return;
            }

            // Init() の前に解像度を決める（Init の後では resize を送らないと効かない）。
            _webView.InitResolution(
                LandscapeWebWidth, LandscapeWebHeight, LandscapeTexWidth, LandscapeTexHeight);

            _root.SetActive(true);
        }

        /// <summary>
        /// 設計 §6「文字入力はパネルに置かない」。
        /// プレハブには WebView を押すと仮想キーボードが出る <c>Button</c> ＋ 入力欄が付いているので、
        /// **シーンに置いた実体の側で**止める（プレハブ資産には触らない）。
        /// あわせて uGUI のレイキャストも切る——調理モードのポークが拾ってしまわないように。
        /// </summary>
        private void DisableTextInputAndRaycast()
        {
            foreach (var raycaster in _root.GetComponentsInChildren<GraphicRaycaster>(true))
            {
                raycaster.enabled = false;
            }

            foreach (var button in _root.GetComponentsInChildren<Button>(true))
            {
                button.enabled = false;
            }

            foreach (var graphic in _root.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            foreach (var behaviour in _root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Name == "TLabWebViewInputField")
                {
                    behaviour.enabled = false;
                }
            }
        }

        /// <summary>WebView のテクスチャを貼っている板（主人のプレハブの <c>RawImage</c>）。</summary>
        public RectTransform Surface
        {
            get
            {
                var rawImage = _root != null ? _root.GetComponentInChildren<RawImage>(true) : null;
                return rawImage != null ? rawImage.rectTransform : null;
            }
        }

        /// <summary>プレハブの中の Canvas（板に合わせて位置と縮尺を直すため）。</summary>
        public Transform Canvas
        {
            get
            {
                var canvas = _root != null ? _root.GetComponentInChildren<Canvas>(true) : null;
                return canvas != null ? canvas.transform : null;
            }
        }

        public void Load(string videoId)
        {
            if (string.IsNullOrEmpty(videoId) || !Invokable(_load))
            {
                return;
            }

            try
            {
                // UniTask<bool>。待たないので Forget して例外を握り潰す（取れなければ絵が出ないだけ）。
                var task = _load.Invoke(_player, new object[] { videoId, true });
                if (task is UniTask<bool> typed)
                {
                    typed.Forget();
                }
            }
            catch (Exception e)
            {
                Warn($"動画を読み込めませんでした（{videoId}）: {e.Message}");
            }
        }

        public void Play() => InvokeVoid(_play, "再生");

        public void Pause() => InvokeVoid(_pause, "一時停止");

        public void SetVolume(int volume)
        {
            if (!Invokable(_setVolume))
            {
                return;
            }

            try
            {
                _setVolume.Invoke(_player, new object[] { (float)Mathf.Clamp(volume, 0, 100) });
            }
            catch (Exception e)
            {
                Warn($"音量を変えられませんでした: {e.Message}");
            }
        }

        /// <summary>
        /// 16:9 ⇄ 9:16。WebView のテクスチャと HTML の器の両方を合わせる。
        ///
        /// 主人の <c>youtube.html</c> は <c>.video-container</c> の
        /// <c>padding-bottom: 56.25%</c>（＝16:9）で iframe の高さを決めている。
        /// **html は書き換えない**ので、切り替えは JS で style を上書きして頼む。
        /// </summary>
        public void SetAspect(VideoAspect aspect)
        {
            if (_webView == null)
            {
                return;
            }

            var portrait = aspect == VideoAspect.Portrait;

            _webView.Resize(
                portrait ? PortraitTexWidth : LandscapeTexWidth,
                portrait ? PortraitTexHeight : LandscapeTexHeight,
                portrait ? PortraitWebWidth : LandscapeWebWidth,
                portrait ? PortraitWebHeight : LandscapeWebHeight);

            // 16:9 → 56.25%、9:16 → 177.78%（高さ ÷ 幅）。
            var padding = (portrait ? 16f / 9f : 9f / 16f) * 100f;
            _webView.EvaluateJS(
                "(function(){var c=document.getElementsByClassName('video-container')[0];" +
                "if(c){c.style.paddingBottom='" +
                padding.ToString("0.##", CultureInfo.InvariantCulture) +
                "%';}})();");
        }

        private bool Invokable(MethodInfo method)
        {
            if (_player == null || method == null)
            {
                Warn("主人の YoutubePlayer が見つからないか、呼び口が変わっています。");
                return false;
            }

            return true;
        }

        private void InvokeVoid(MethodInfo method, string what)
        {
            if (!Invokable(method))
            {
                return;
            }

            try
            {
                method.Invoke(_player, null);
            }
            catch (Exception e)
            {
                Warn($"{what}に失敗しました: {e.Message}");
            }
        }

        /// <summary>警告は一度だけ（毎フレーム出しても直せるのは主人だけなので）。</summary>
        private void Warn(string message)
        {
            if (_warned)
            {
                return;
            }

            _warned = true;
            Debug.LogWarning($"[KitchenXR] 動画の板: {message}");
        }
    }
}
