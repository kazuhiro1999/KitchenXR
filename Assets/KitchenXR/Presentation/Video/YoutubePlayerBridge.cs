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
    /// TLab の <c>YoutubePlayer.prefab</c>（`Assets/TLab/Youtube/`）を包む
    /// <see cref="IVideoPlayer"/>。TLab のコードは書き換えず包む（<c>TLab.Android.WebView</c> に
    /// 触れてよいのはこのフォルダだけ——<c>PlatformIsolationTests</c> が検算している）。
    ///
    /// 反射を使うのはそうするしかないから: <c>YoutubePlayer.cs</c> は asmdef の無いフォルダに
    /// あるので <c>Assembly-CSharp</c> に入り、asmdef を持つ <c>KitchenXR.Runtime</c> からは
    /// 参照できない。呼ぶのは <c>Load</c>／<c>Play</c>／<c>Pause</c>／<c>SetVolume</c> と、
    /// 様子を読む <c>Initialized</c>／<c>State</c>／<c>videoId</c>。見つからなければ札に出して
    /// 黙る（板の一覧とボタンは動き続ける）。<c>TLabWebView</c> は asmdef を持つので型で呼ぶ。
    ///
    /// プレハブは 1024x1024（正方形）だが <c>youtube.html</c> の器は 16:9 で上に寄るので、
    /// <see cref="Initialize"/> で 16:9 の解像度に直してから WebView を起こす。
    ///
    /// Android の WebView は人の操作を伴わない再生を拒むことがある
    /// （<c>mediaPlaybackRequiresUserGesture</c>）ので、<see cref="TapCenter"/> でページ中央に
    /// 触ったことにする——cue 中の中央は YouTube の再生ボタンなので、以後 JS の再生も通る。
    /// </summary>
    public sealed class YoutubePlayerBridge : IVideoPlayer
    {
        /// <summary>TLab のプレハブの置き場。ここ以外からは触らない。</summary>
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

        // TLabWebView.TouchEvent の eventNum（WebViewInputListener.WebTouchEvent と同じ）。
        private const int TouchDown = 0;
        private const int TouchUp = 1;

        /// <summary>タップの押し下げから押し上げまで。短すぎると WebView がクリックと見なさない。</summary>
        private const int TapHoldMs = 80;

        // YoutubePlayer.PlayerState の値（youtube.html が返す YT.PlayerState と同じ）。
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

        /// <summary><c>LoadHtml(string)</c> と、その材料の <c>TextAsset</c>（private な SerializeField）。</summary>
        private readonly MethodInfo _loadHtml;
        private readonly FieldInfo _htmlAsset;

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

                // 舞台を作り直す道（関連動画が youtube.com へ出てしまったとき）。
                // html の中身は private な SerializeField にしか無いので、そこから借りる
                // ——読むだけで、プレハブにも TextAsset にも書き戻さない。
                _loadHtml = type.GetMethod("LoadHtml", new[] { typeof(string) });
                _htmlAsset = type.GetField("Html", BindingFlags.Instance | BindingFlags.NonPublic);
            }
        }

        /// <summary><c>YoutubePlayer</c>（global namespace・Assembly-CSharp）を名前で引き当てる。</summary>
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
        /// 板は「動画は実機で」の札を出す。
        /// </summary>
        public bool IsAvailable =>
            Application.platform == RuntimePlatform.Android && _player != null && _webView != null;

        public string LastError { get; private set; }

        /// <summary>
        /// YouTube 側が PLAYING か BUFFERING と返しているか。
        /// TLab の <c>isPlaying</c> は Play() を送った時点で true にする楽観値なので使わない
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

        /// <summary><c>youtube.html</c> が読み込み終わったか（<c>YoutubePlayer.Initialized</c>）。</summary>
        public bool IsHtmlLoaded => ReadBool(_initialized);

        /// <summary>
        /// 今 WebView が開いている URL。
        ///
        /// <c>TLabWebView.GetUrl()</c> は <c>m_state</c> を見ないので、初期化の前に呼ぶと
        /// ネイティブの口（<c>m_NativePlugin</c>）が無くて例外になる。必ず状態を確かめてから呼ぶ。
        /// </summary>
        public string CurrentUrl
        {
            get
            {
                if (_webView == null || _webView.state != TLabWebView.State.INITIALIZED)
                {
                    return null;
                }

                try
                {
                    return _webView.GetUrl();
                }
                catch (Exception)
                {
                    // 取れないだけ。札に出すほどのことではない（0.5 秒ごとに来るので溢れる）。
                    return null;
                }
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
        /// <c>YoutubePlayer.Start()</c> が走るように実体を起こす。
        ///
        /// Android の実機でなければ起こさない——起こしても白い板が出るだけで、
        /// <c>Update()</c> が毎フレーム空振りする。
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
        /// 文字入力はパネルに置かない。プレハブには WebView を押すと仮想キーボードが出る
        /// <c>Button</c> ＋ 入力欄が付いているので、シーンに置いた実体の側で止める
        /// （プレハブ資産には触らない）。
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

        /// <summary>WebView のテクスチャを貼っている板（プレハブの <c>RawImage</c>）。</summary>
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
                    // TimeoutException（TLab の Load は 5 秒で切る）は札に出す。
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
        /// <c>youtube.html</c> は <c>.video-container</c> の <c>padding-bottom: 56.25%</c>
        /// （＝16:9）で iframe の高さを決めている。
        /// html は書き換えないので、切り替えは JS で style を上書きして頼む。
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
        /// 窓を触った場所を WebView へ渡す（板は比だけを知り、解像度はここで当てる）。
        ///
        /// <c>WebViewInputListener</c> と同じ——要素の中の比（u は左→右、v は上→下）に
        /// <c>webWidth</c>／<c>webHeight</c> を掛けて <c>TouchEvent</c> へ渡す。
        /// v が上からなのは HTML の座標系に合わせるため。
        ///
        /// 解像度は向きで変わる（<see cref="SetAspect"/> が <c>Resize</c> を送る）ので、
        /// そのつど <c>webWidth</c>／<c>webHeight</c> を読み直す——定数を写すと
        /// 9:16 に切り替えたあと関連動画を触った場所がずれる。
        /// </summary>
        public void Touch(VideoTouchPhase phase, float u, float v)
        {
            if (_webView == null)
            {
                return;
            }

            try
            {
                _webView.TouchEvent(ToWebX(u), ToWebY(v), (int)phase);
            }
            catch (Exception e)
            {
                Warn($"窓への触りを送れませんでした: {e.Message}");
            }
        }

        // 絵の中の比 → WebView の論理ピクセル。端（0 や 1）ちょうどだと WebView の外の座標に
        // なり得るので内側へ丸める。解像度は向きで変わるのでそのつど読む。
        private int ToWebX(float u) =>
            Mathf.Clamp(Mathf.RoundToInt(u * _webView.webWidth), 0, Mathf.Max(0, _webView.webWidth - 1));

        private int ToWebY(float v) =>
            Mathf.Clamp(Mathf.RoundToInt(v * _webView.webHeight), 0, Mathf.Max(0, _webView.webHeight - 1));

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
        /// <c>youtube.html</c> を読み直して埋め込みプレイヤーの舞台を作り直す。
        ///
        /// 反射で <c>YoutubePlayer.LoadHtml(Html.text)</c> を呼ぶ（<c>Init()</c> が起動時にするのと同じ）。
        /// あわせて <c>videoId</c> を空にする——<c>LoadVideo</c> は
        /// 「同じ id なら何もしない」で早々に帰るので、読み直した直後に同じ動画を頼めなくなる。
        /// 反射が効かなければ <see cref="GoBack"/> で代える（履歴が1つならそれで戻れる）。
        /// </summary>
        public void ReloadHtml()
        {
            var html = _htmlAsset != null && _player != null
                ? (_htmlAsset.GetValue(_player) as TextAsset)?.text
                : null;

            if (_loadHtml == null || string.IsNullOrEmpty(html))
            {
                Warn("html を読み直せないので「戻る」で代えます（YoutubePlayer の作りが変わった？）。");
                GoBack();
                return;
            }

            try
            {
                _loadHtml.Invoke(_player, new object[] { html });
                ForgetLoadedVideoId();
            }
            catch (Exception e)
            {
                Warn($"html を読み直せませんでした: {e.Message}");
                GoBack();
            }
        }

        /// <summary>
        /// TLab 側が覚えている「今の動画 id」を忘れさせる（private な setter を反射で叩く）。
        /// 効かなくても致命ではない——同じ動画をもう一度選んだときだけ空振りする。
        /// </summary>
        private void ForgetLoadedVideoId()
        {
            if (_player == null || _videoId == null)
            {
                return;
            }

            try
            {
                _videoId.SetValue(_player, null);
            }
            catch (Exception)
            {
                // TLab 側の作りが変わった。札に出すほどのことではない。
            }
        }

        /// <summary>
        /// ページ全体の縦スクロールを塞ぐ（16:9 のときページごと縦に動いてしまう）。
        ///
        /// html は書き換えない（TLab の資産）。だから読み込みが済んだあとに JS で被せる:
        ///   - <c>overflow: hidden</c> を html と body の両方へ（body だけでは足りない）
        ///   - <c>overscroll-behavior: none</c>——Android の WebView の「端で引っ張れる」を止める
        ///   - <c>touch-action: none</c>——ページ自身のパン／ズームを渡さない
        ///   - <c>touchmove</c> を body と html の上でだけ <c>preventDefault</c>。
        ///     iframe（＝関連動画の帯）の中は素通しにする——関連動画の帯の横スクロールを
        ///     殺してはいけない。
        /// 何度送っても害は無い（同じ style を上書きするだけ）ので、読み込み完了と向きの切替のたびに送る。
        /// </summary>
        public void SuppressPageScroll()
        {
            if (_webView == null)
            {
                return;
            }

            try
            {
                _webView.EvaluateJS(
                    "(function(){" +
                    "var h=document.documentElement,b=document.body;" +
                    "if(h){h.style.overflow='hidden';h.style.overscrollBehavior='none';}" +
                    "if(b){b.style.overflow='hidden';b.style.overscrollBehavior='none';b.style.touchAction='none';}" +
                    "if(!window.__kxNoScroll){window.__kxNoScroll=true;" +
                    "document.addEventListener('touchmove',function(e){" +
                    "if(e.target===document.body||e.target===document.documentElement){e.preventDefault();}" +
                    "},{passive:false});}" +
                    "})();");
            }
            catch (Exception e)
            {
                Warn($"縦スクロールを止められませんでした: {e.Message}");
            }
        }

        /// <summary>
        /// ページの中央を一度タップする。動画が cue されているときの中央は YouTube の大きな
        /// 再生ボタンなので、これで再生が始まり、以後この WebView は「操作済み」になる。
        /// </summary>
        public void TapCenter()
        {
            _tapCount++;
            Tap(0.5f, 0.5f);
        }

        /// <summary>
        /// 窓の1点を叩く。押し下げと押し上げの間を <see cref="TapHoldMs"/> 空け、座標は動かさない
        /// （同じフレームで送ると WebView がクリックと見なさず、座標が動くとスクロールと
        /// 見なされる——関連動画が押せなくなる）。
        /// </summary>
        public void Tap(float u, float v)
        {
            if (_webView == null)
            {
                return;
            }

            var x = ToWebX(u);
            var y = ToWebY(v);

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
                Warn("YoutubePlayer が見つからないか、呼び口が変わっています。");
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

        /// <summary>ログは一度だけ（毎フレーム出しても意味が無い）。札には毎回出す。</summary>
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
