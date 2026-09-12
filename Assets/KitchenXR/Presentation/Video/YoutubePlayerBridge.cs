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
    /// そこで **型の名前で引き当てて呼ぶ**。呼ぶのは4つ
    /// （<c>Load</c> / <c>Play</c> / <c>Pause</c> / <c>SetVolume</c>）と、
    /// 様子を読む3つ（<c>Initialized</c> / <c>State</c> / <c>videoId</c>）で、
    /// 見つからなければ札に出して黙る——板の一覧とボタンは動き続ける。
    /// <c>TLabWebView</c> は asmdef（<c>com.tlabaltoh.webview.runtime</c>）を持つので、
    /// こちらは型でそのまま呼んでいる。
    ///
    /// 解像度について: 主人のプレハブは 1024x1024（正方形）で、<c>youtube.html</c> の器は
    /// 16:9 の比で上に寄るため、そのままだと板の下半分が空白になる。
    /// だから板を作るときに <see cref="Initialize"/> で 16:9 の解像度に直してから
    /// WebView を起こし、9:16 へ切り替えるときは <c>Resize</c> と
    /// （html の器を直す）<c>EvaluateJS</c> の両方を送る。
    ///
    /// 2026-09-13 主人の実機確認（v1.0.6）「再生を押しても反応がない」への備え:
    /// Android の WebView は既定で**人の操作を伴わない再生**を拒む
    /// （<c>mediaPlaybackRequiresUserGesture</c>）。主人の SDK は WebView そのものを触って
    /// 使う前提なので困らないが、この板は WebView に触らせず JS だけで頼んでいる。
    /// そこで <see cref="TapCenter"/>——ページの中央に触ったことにする
    /// （<c>TLabWebView.TouchEvent</c>。主人の <c>WebViewInputListener</c> と同じ経路）。
    /// 動画が cue されているときの中央は YouTube の大きな再生ボタンなので、
    /// これで再生が始まり、以後この WebView は「操作済み」になって JS の再生も通る。
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

        // TLabWebView.TouchEvent の eventNum（主人の WebViewInputListener.WebTouchEvent と同じ）。
        private const int TouchDown = 0;
        private const int TouchUp = 1;

        /// <summary>タップの押し下げから押し上げまで。短すぎると WebView がクリックと見なさない。</summary>
        private const int TapHoldMs = 80;

        // 主人の YoutubePlayer.PlayerState の値（youtube.html が返す YT.PlayerState と同じ）。
        private const int StatePlaying = 1;
        private const int StateBuffering = 3;

        private readonly GameObject _root;
        private readonly TLabWebView _webView;
        private readonly MonoBehaviour _player;

        private readonly MethodInfo _load;
        private readonly MethodInfo _play;
        private readonly MethodInfo _pause;
        private readonly MethodInfo _setVolume;

        private readonly PropertyInfo _initialized;
        private readonly PropertyInfo _state;
        private readonly PropertyInfo _videoId;

        private bool _warned;
        private int _tapCount;

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

                _initialized = type.GetProperty("Initialized");
                _state = type.GetProperty("State");
                _videoId = type.GetProperty("videoId");
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

        public string LastError { get; private set; }

        /// <summary>
        /// YouTube 側が PLAYING か BUFFERING と返しているか。
        /// 主人の <c>isPlaying</c> は Play() を送った時点で true にする楽観値なので使わない
        /// ——再生を拒まれたときこそ知りたい。
        /// </summary>
        public bool IsReportedPlaying
        {
            get
            {
                var state = ReadState();
                return state == StatePlaying || state == StateBuffering;
            }
        }

        public string StatusLine
        {
            get
            {
                if (_root == null)
                {
                    return "プレハブ未設定";
                }

                if (_player == null || _webView == null)
                {
                    return "YoutubePlayer が見つかりません";
                }

                var web = _webView.state.ToString();
                var html = ReadBool(_initialized) ? "読込済" : "待ち";
                var state = StateName(ReadState());
                var id = ReadString(_videoId);
                var tapped = _tapCount > 0 ? $" タップ{_tapCount}" : string.Empty;
                var idPart = string.IsNullOrEmpty(id) ? string.Empty : " " + id;
                return $"WebView {web} / HTML {html} / 動画 {state}{idPart}{tapped}";
            }
        }

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
                var task = _load.Invoke(_player, new object[] { videoId, true });
                if (task is UniTask<bool> typed)
                {
                    // 結果は待たないが捨てない——false（10 秒待って loaded にならない）や
                    // TimeoutException（主人の Load は 5 秒で切る）は札に出す。
                    ObserveLoadAsync(typed, videoId).Forget();
                }
            }
            catch (Exception e)
            {
                Warn($"動画を読み込めませんでした（{videoId}）: {e.Message}");
            }
        }

        private async UniTaskVoid ObserveLoadAsync(UniTask<bool> task, string videoId)
        {
            try
            {
                var ok = await task;
                if (!ok)
                {
                    Warn($"読み込みの返事がありません（{videoId}）");
                }
            }
            catch (TimeoutException)
            {
                Warn($"読み込みが 5 秒で応答なし（{videoId}）。再生を押すと窓の中央をタップします");
            }
            catch (Exception e)
            {
                Warn($"読み込みで例外（{videoId}）: {e.Message}");
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

        /// <summary>
        /// 窓を触った場所を WebView へ渡す（方針3。板は比だけを知り、解像度はここで当てる）。
        ///
        /// 主人の <c>WebViewInputListener</c> がやっていることと同じ——
        /// 要素の中の比（u は左→右、v は**上→下**）に <c>webWidth</c>／<c>webHeight</c> を掛けて
        /// <c>TouchEvent</c> へ渡す。v が上からなのは HTML の座標系に合わせるため。
        ///
        /// 解像度は向きで変わる（<see cref="SetAspect"/> が <c>Resize</c> を送る）ので、
        /// **そのつど <c>webWidth</c>／<c>webHeight</c> を読み直す**——定数を写すと
        /// 9:16 に切り替えたあと関連動画を触った場所がずれる。
        /// </summary>
        public void Touch(VideoTouchPhase phase, float u, float v)
        {
            if (_webView == null)
            {
                return;
            }

            // 端（0 や 1）ちょうどだと WebView の外の座標になり得るので、内側へ丸める。
            var x = Mathf.Clamp(Mathf.RoundToInt(u * _webView.webWidth), 0, Mathf.Max(0, _webView.webWidth - 1));
            var y = Mathf.Clamp(Mathf.RoundToInt(v * _webView.webHeight), 0, Mathf.Max(0, _webView.webHeight - 1));

            try
            {
                _webView.TouchEvent(x, y, (int)phase);
            }
            catch (Exception e)
            {
                Warn($"窓への触りを送れませんでした: {e.Message}");
            }
        }

        /// <summary>
        /// WebView の履歴を1つ戻る。関連動画を触った先が youtube.com 本体へ飛ぶことがあるので、
        /// 板の操作部から埋め込みプレイヤーへ帰れるようにしておく。
        /// </summary>
        public void GoBack()
        {
            if (_webView == null)
            {
                return;
            }

            try
            {
                _webView.GoBack();
            }
            catch (Exception e)
            {
                Warn($"戻れませんでした: {e.Message}");
            }
        }

        /// <summary>
        /// ページの中央を一度タップする。押し下げと押し上げの間を <see cref="TapHoldMs"/> 空ける
        /// （同じフレームで送ると WebView がクリックと見なさない）。
        /// </summary>
        public void TapCenter()
        {
            if (_webView == null)
            {
                return;
            }

            var x = Mathf.Max(1, _webView.webWidth / 2);
            var y = Mathf.Max(1, _webView.webHeight / 2);
            _tapCount++;

            try
            {
                _webView.TouchEvent(x, y, TouchDown);
                ReleaseLaterAsync(x, y).Forget();
            }
            catch (Exception e)
            {
                Warn($"タップを送れませんでした: {e.Message}");
            }
        }

        private async UniTaskVoid ReleaseLaterAsync(int x, int y)
        {
            await UniTask.Delay(TapHoldMs);
            try
            {
                if (_webView != null)
                {
                    _webView.TouchEvent(x, y, TouchUp);
                }
            }
            catch (Exception e)
            {
                Warn($"タップの押し上げを送れませんでした: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ 読む側

        private int ReadState()
        {
            if (_player == null || _state == null)
            {
                return int.MinValue;
            }

            try
            {
                return Convert.ToInt32(_state.GetValue(_player));
            }
            catch
            {
                return int.MinValue;
            }
        }

        private bool ReadBool(PropertyInfo property)
        {
            if (_player == null || property == null)
            {
                return false;
            }

            try
            {
                return property.GetValue(_player) is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private string ReadString(PropertyInfo property)
        {
            if (_player == null || property == null)
            {
                return null;
            }

            try
            {
                return property.GetValue(_player) as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>YT.PlayerState の番号を人の言葉に（札に出す）。</summary>
        private static string StateName(int state)
        {
            switch (state)
            {
                case -1: return "未開始";
                case 0: return "終了";
                case 1: return "再生中";
                case 2: return "一時停止";
                case 3: return "読み込み中";
                case 5: return "待機（cue）";
                case int.MinValue: return "不明";
                default: return state.ToString(CultureInfo.InvariantCulture);
            }
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

        /// <summary>ログは一度だけ（毎フレーム出しても直せるのは主人だけなので）。札には毎回出す。</summary>
        private void Warn(string message)
        {
            LastError = message;
            if (_warned)
            {
                return;
            }

            _warned = true;
            Debug.LogWarning($"[KitchenXR] 動画の板: {message}");
        }
    }
}
