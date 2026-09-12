using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation.Video
{
    /// <summary>
    /// 動画の板（4枚目）。レシピとは無関係の娯楽の板なので <c>CookSession</c> には触れない。
    /// 板は「左に窓・右に一覧・下に操作部」で、文字入力は置かない。
    ///
    /// 絵は UI Toolkit では描かず、TLab の <c>YoutubePlayer.prefab</c> の Canvas ＋ RawImage を
    /// 窓の 1cm 手前に重ねる——<c>TLabWebView</c> が毎フレーム <c>m_rawImage.texture</c> を
    /// 差し替える経路にそのまま乗るため。<c>backgroundImage</c> に貼ると UI Toolkit が動的
    /// アトラスへ焼くことがあり、焼かれると外部テクスチャの更新が届かず最初の1枚で止まる。
    /// ポークの当たり判定は UI 側の板だけが持つ（プレハブ側の <c>GraphicRaycaster</c> と
    /// <c>Button</c> は <see cref="YoutubePlayerBridge.Initialize"/> で止めてある）。
    ///
    /// 窓への触りは <see cref="TapMoveThreshold"/> を越えないまま離れたらタップと判定して
    /// <see cref="IVideoPlayer.Tap"/> だけを送り、越えて初めて DOWN → DRAG を流す
    /// （揺れが DRAG になると WebView がスクロールと見なしてクリックを出さない）。
    ///
    /// 関連動画が <c>youtube.com/watch</c> へ top frame を運ぶことがあるので、
    /// <see cref="IVideoPlayer.CurrentUrl"/> を 0.5 秒ごとに見張り、同梱の html
    /// （<c>http://localhost</c>）の外へ出ていたら id を抜いて埋め込みへ連れ戻す。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class VideoPanel : MonoBehaviour
    {
        // ------------------------------------------------------------------ 板の寸法
        //
        // 板の大きさ（UI px。theme.uss の換算で 1px = 2mm）。
        // 「左に窓・右に一覧・下に操作部」なので、板の幅は窓と一覧の和で決まる。

        /// <summary>16:9 の窓の幅。250px × 2mm = 50cm。</summary>
        public const float LandscapeWindowWidthUnits = 250f;

        /// <summary>
        /// 9:16（ショーツ）の窓の高さ。幅 50cm だと高さ 89cm になって台所に置けないので、
        /// 高さを 36cm に取り、幅はその 9:16 とする。
        /// </summary>
        public const float PortraitWindowHeightUnits = 180f;

        /// <summary>9:16 の窓の幅（180 × 9/16 = 101.25 → 端数を切り上げて 102px ≒ 20.4cm）。</summary>
        public const float PortraitWindowWidthUnits = 102f;

        /// <summary>
        /// 右の一覧の幅（≒ 20cm）。中身は「サムネイル 8.8cm ＋ 題名 2行」の横並びを一番細く
        /// 収めた値——これより細いと題名が1行 5 文字を切って読めなくなる（内訳は USS に書いた）。
        /// 向きが変わっても幅は変えない（並んでいるものの位置が動くと探し直すことになる）。
        /// </summary>
        public const float ListWidthUnits = 100f;

        /// <summary>窓と一覧の間（≒ 8mm）。</summary>
        public const float ListGapUnits = 4f;

        public const float LandscapeWidthUnits = LandscapeWindowWidthUnits + ListGapUnits + ListWidthUnits;

        /// <summary>
        /// 16:9 の板の高さ（≒ 44.8cm）。
        ///
        /// 内訳: 操作部（札3行＋釦の行）が 60px、間が 3px、残り 161px が窓。
        /// 絵は 250px 幅の 16:9 で 140.6px（28.1cm）なので 20px の取り置きが残る——
        /// これは札（`.video-status-line`）が3行とも2行に折り返したときのぶんで、
        /// 削ると実機で札が伸びた瞬間に絵が 50cm を割る。
        /// </summary>
        public const float LandscapeHeightUnits = 224f;

        public const float PortraitWidthUnits = PortraitWindowWidthUnits + ListGapUnits + ListWidthUnits;

        /// <summary>
        /// 9:16 の板の高さ（≒ 52.8cm）。操作部 60px ＋ 間 3px ＋ 窓 201px。
        /// 縦向きの絵は幅で決まる（窓 102px × 16/9 = 181px）ので、
        /// 窓の残り 20px が札の折り返しの取り置き（16:9 と同じ考え）。
        /// </summary>
        public const float PortraitHeightUnits = 264f;

        /// <summary>
        /// 絵を板の面より手前へ出す量（板のローカル単位。負が手前）。実寸 1cm。
        /// 板（UI Toolkit）と絵（uGUI）はどちらも深度を書かない半透明なので、描く順はカメラから
        /// の距離で決まる——0.6mm しか離さないと板の方が後に描かれ、白 85% が絵に乗って
        /// 「薄白いカバーがかかって鮮明でない」に見える。1cm 離せば絵が確実に手前になる。
        /// 当たり判定は板の面のままなので、押し心地は変わらない。
        /// </summary>
        private const float SurfaceOffset = -0.05f;

        /// <summary>
        /// プレハブの根の縮尺（板のローカル単位／Canvas px）。
        /// 板の localScale 0.2 と合わせて Canvas の 1px = 実寸 1mm。
        /// </summary>
        private const float SurfaceCanvasScale = 0.005f;

        /// <summary>音量の刻み（釦は大きく疎にするので細かくしない）。</summary>
        public const int VolumeStep = 10;

        public const int DefaultVolume = 70;

        /// <summary>再生を頼んでから「返事が無い」と見なして中央をタップするまで（ミリ秒）。</summary>
        public const int GestureFallbackDelayMs = 900;

        // ------------------------------------------------------------------ 窓への触りの判定

        /// <summary>
        /// 「動かなかった」と見なす幅（絵の幅に対する比）。3% ＝ 50cm の絵で 1.5cm。
        ///
        /// レイの着地点は手の震え（1〜3°）が 1m 先で 2〜5cm に開くので、板の上では必ず揺れる
        /// （ポークの指先も同じ）。この幅までは「同じ場所を押している」と見なし、DOWN を送らない
        /// ——送ってしまうと続く揺れが DRAG になって、WebView がスクロールと解釈する。
        /// 縦の揺れは絵の縦横比を掛けて同じ物差しで測る（9:16 では縦の 1% が横の 1.8% に当たる）。
        /// </summary>
        public const float TapMoveThreshold = 0.03f;

        /// <summary>
        /// 押し下げから離すまでがこれ以内ならタップ（秒）。1.2 秒と長いのには理由が2つある。
        ///   1. ホログラムを指で突くと指は面を通り抜けてから戻るので、XRI の PointerDown から
        ///      PointerUp までに数十フレーム掛かる。400ms 程度では実機の普通の突きが「長押し」に
        ///      落ちて何も起きない。
        ///   2. 広げても WebView 側は常に 80ms のクリックしか見ない
        ///      （<see cref="IVideoPlayer.Tap"/> が押し下げと押し上げを自分で作るので、
        ///      こちらが何秒待っても Android 側の長押し＝文脈メニューにはならない）。
        /// この時間が守っているのは「板に手を置きっぱなしにして離した」だけを弾くこと。
        /// </summary>
        public const float TapMaxSeconds = 1.2f;

        /// <summary>
        /// html を読み直してから <c>loadVideo</c> を頼むまで（ミリ秒）。
        /// html は <c>https://www.youtube.com/iframe_api</c> を取りに行くので、立ち上がるのを
        /// 待つしかない（<c>Initialized</c> は一度立つと下りないため合図に使えない）。
        /// </summary>
        public const int RelatedReloadDelayMs = 1800;

        /// <summary>札を書き直す間隔（秒）。窓の URL を見張る間隔も兼ねる。</summary>
        private const float StatusRefreshSeconds = 0.5f;

        /// <summary>同梱の html の置き場（<c>YoutubePlayer.LoadHtml</c> の baseUrl）。ここに居る間は何もしない。</summary>
        private const string EmbeddedPlayerHost = "localhost";

        [SerializeField] private GameObject _playerRoot;

        private readonly ClickDebounce _debounce = new ClickDebounce();
        private readonly List<VisualElement> _rows = new List<VisualElement>();

        /// <summary>行に貼った絵。板が消えるとき（と並べ直すとき）に自分で捨てる。</summary>
        private readonly List<Texture2D> _thumbnails = new List<Texture2D>();

        /// <summary>今 窓を押している指（レイ）ごとの様子。タップかドラッグかをここで決める。</summary>
        private readonly Dictionary<int, WindowTouch> _touching = new Dictionary<int, WindowTouch>();

        private UIDocument _document;
        private VisualElement _root;
        private ScrollView _list;
        private VisualElement _videoArea;
        private Label _notice;
        private Label _nowPlaying;
        private Label _volumeLabel;
        private Label _statusLine;
        private Label _actionLine;
        private Label _errorLine;
        private Button _playPauseButton;
        private Button _aspectButton;

        // カメラの下見（v1-d）。一覧の上の小さな窓と、その下の釦2つ。
        private VisualElement _cameraWindow;
        private Label _cameraStatus;
        private Button _cameraButton;
        private Button _cameraBurstButton;
        private CameraProbe _cameraProbe;

        private IVideoPlayer _player;
        private YoutubePlayerBridge _bridge;
        private MediaThumbnailCache _thumbnailCache;
        private CancellationTokenSource _thumbnailCts;

        private IReadOnlyList<MediaItem> _items = new List<MediaItem>();
        private int _selected = -1;
        private int _volume = DefaultVolume;
        private bool _playing;

        private string _lastAction = string.Empty;
        private string _lastLog;
        private string _surfaceInfo = string.Empty;
        private float _statusClock;
        private int _fallbackSerial;

        /// <summary>一覧に無い動画（関連動画）を開いたときの見出し。null なら一覧の選択を出す。</summary>
        private string _offListTitle;

        /// <summary>html が読み込み終わったか（立った瞬間に縦スクロールを塞ぐ）。</summary>
        private bool _htmlLoaded;

        /// <summary>最後に連れ戻した関連動画の id（同じ URL で何度も反応しないため）。</summary>
        private string _handledRelatedId;

        private int _relatedSerial;

        // 向きを変えても動かさない点（板の下辺の中央）。
        private Vector3 _bottomCenter;
        private Quaternion _rotation = Quaternion.identity;
        private bool _anchored;

        public VideoAspect Aspect { get; private set; } = VideoAspect.Landscape;

        public IReadOnlyList<MediaItem> Items => _items;

        public int SelectedIndex => _selected;

        public int Volume => _volume;

        public bool IsPlaying => _playing;

        /// <summary>札の「最後にしたこと」（試験用）。</summary>
        public string LastAction => _lastAction;

        /// <summary>窓（動画の絵が入る枠）。PlayMode 試験が実測する。</summary>
        public VisualElement VideoArea => _videoArea;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _root = _document.rootVisualElement;

            _list = _root.Q<ScrollView>("mediaList");
            _videoArea = _root.Q<VisualElement>("videoArea");
            _notice = _root.Q<Label>("editorNotice");
            _nowPlaying = _root.Q<Label>("nowPlayingLabel");
            _volumeLabel = _root.Q<Label>("volumeLabel");
            _statusLine = _root.Q<Label>("statusLine");
            _actionLine = _root.Q<Label>("actionLine");
            _errorLine = _root.Q<Label>("errorLine");
            _playPauseButton = _root.Q<Button>("playPauseButton");
            _aspectButton = _root.Q<Button>("aspectButton");

            _cameraWindow = _root.Q<VisualElement>("cameraWindow");
            _cameraStatus = _root.Q<Label>("cameraStatus");
            _cameraButton = _root.Q<Button>("cameraButton");
            _cameraBurstButton = _root.Q<Button>("cameraBurstButton");

            PokePress.BindButton(_playPauseButton, _debounce, "video-playpause", TogglePlayPause);
            PokePress.BindButton(_root.Q<Button>("volumeDownButton"), _debounce, "video-volume-down",
                () => ChangeVolume(-VolumeStep));
            PokePress.BindButton(_root.Q<Button>("volumeUpButton"), _debounce, "video-volume-up",
                () => ChangeVolume(+VolumeStep));
            PokePress.BindButton(_aspectButton, _debounce, "video-aspect", ToggleAspect);
            PokePress.BindButton(_root.Q<Button>("backButton"), _debounce, "video-back", GoBack);

            // カメラの下見。調理中も押せる（この板だけはレイも指も通る層に居る）。
            PokePress.BindButton(_cameraButton, _debounce, "video-camera", ToggleCamera);
            PokePress.BindButton(_cameraBurstButton, _debounce, "video-camera-burst", ToggleCameraBurst);

            // 窓そのものを触れるようにする。釦ではないので PokePress は通さない。
            BindVideoAreaTouch();

            // TLab のプレハブを包む。実機でなければここで眠らせる（板は札を出す）。
            _bridge = new YoutubePlayerBridge(_playerRoot);
            _bridge.Initialize();
            _player = _bridge.IsAvailable ? (IVideoPlayer)_bridge : new NullVideoPlayer();

            // 板の大きさが決まってから絵を重ねる（レイアウト待ち）。
            _videoArea?.RegisterCallback<GeometryChangedEvent>(_ => LayoutSurface());

            ApplyAspect(Aspect);
            RefreshControls();
            RefreshStatus();
        }

        private void OnEnable() => Application.logMessageReceived += HandleLog;

        private void OnDisable() => Application.logMessageReceived -= HandleLog;

        private void OnDestroy()
        {
            CancelThumbnailLoads();
            ReleaseThumbnails();

            if (_cameraProbe != null)
            {
                _cameraProbe.Changed -= RefreshCamera;
                _cameraProbe.Dispose();
                _cameraProbe = null;
            }
        }

        private void Update()
        {
            _statusClock += Time.unscaledDeltaTime;
            if (_statusClock < StatusRefreshSeconds)
            {
                return;
            }

            _statusClock = 0f;

            // 実機では YouTube が返す状態を正とする（押した／押していないの覚えより確か）。
            if (_player != null && _player.IsAvailable)
            {
                var reported = _player.IsReportedPlaying;
                if (reported != _playing)
                {
                    _playing = reported;
                    RefreshControls();
                }
            }

            WatchHtmlLoaded();
            WatchWindowUrl();
            RefreshStatus();
        }

        // ------------------------------------------------------------------ 配線

        /// <summary>
        /// 動画の出し手を差し替える（Editor・試験用）。実機では <see cref="YoutubePlayerBridge"/> のまま。
        /// </summary>
        public void BindPlayer(IVideoPlayer player)
        {
            _player = player ?? new NullVideoPlayer();
            _player.SetVolume(_volume);
            _player.SetAspect(Aspect);
            _htmlLoaded = false;
            _handledRelatedId = null;
            RefreshControls();
            RefreshStatus();
        }

        /// <summary>
        /// カメラの下見を挿す（<c>Bootstrap</c> から。挿さっていなければ釦は何もしない）。
        /// 板が壊れても調理は止まらないので、口は1つだけにしてある。
        /// </summary>
        public void BindCamera(CameraProbe probe)
        {
            if (_cameraProbe != null)
            {
                _cameraProbe.Changed -= RefreshCamera;
            }

            _cameraProbe = probe;
            if (_cameraProbe != null)
            {
                _cameraProbe.Changed += RefreshCamera;
            }

            RefreshCamera();
        }

        /// <summary>「カメラ」——1枚取って小さな窓に出す。もう一度押すと消える。</summary>
        public void ToggleCamera()
        {
            if (_cameraProbe == null)
            {
                SetCameraStatus("カメラ: 口が挿さっていません");
                return;
            }

            _cameraProbe.Toggle();
        }

        /// <summary>「連写2fps」——2枚/秒で JPEG にして、大きさと所要を札に流す（段 (c) の下見）。</summary>
        public void ToggleCameraBurst()
        {
            if (_cameraProbe == null)
            {
                SetCameraStatus("カメラ: 口が挿さっていません");
                return;
            }

            _cameraProbe.ToggleBurst();
        }

        /// <summary>札の文言（試験用）。</summary>
        public string CameraStatus => _cameraStatus?.text ?? string.Empty;

        private void RefreshCamera()
        {
            if (_cameraWindow != null)
            {
                var texture = _cameraProbe?.Texture;
                _cameraWindow.style.backgroundImage = texture != null
                    ? new StyleBackground(texture)
                    : new StyleBackground(StyleKeyword.None);
                _cameraWindow.EnableInClassList("is-hidden", texture == null);
            }

            if (_cameraBurstButton != null)
            {
                _cameraBurstButton.text = _cameraProbe != null && _cameraProbe.IsBursting ? "連写停止" : "連写2fps";
            }

            SetCameraStatus(_cameraProbe?.StatusText ?? string.Empty);
        }

        private void SetCameraStatus(string text)
        {
            if (_cameraStatus == null)
            {
                return;
            }

            _cameraStatus.text = text ?? string.Empty;
            _cameraStatus.EnableInClassList("is-hidden", string.IsNullOrEmpty(text));
        }

        /// <summary>絵の保管庫を差し替える（試験用。既定は <see cref="MediaThumbnailCache.CreateDefault"/>）。</summary>
        public void BindThumbnailCache(MediaThumbnailCache cache)
        {
            _thumbnailCache = cache;
            RebuildList();
        }

        /// <summary>一覧を貼り替える（manor が正。上限は <see cref="MediaJson.MaxItems"/>）。</summary>
        public void BindMedia(IReadOnlyList<MediaItem> items)
        {
            _items = items ?? new List<MediaItem>();
            _selected = -1;
            RebuildList();
            RefreshControls();
        }

        private void RebuildList()
        {
            if (_list == null)
            {
                return;
            }

            // 前の絵は捨てる（並べ直しのたびに Texture2D が増えていくのを防ぐ）。
            CancelThumbnailLoads();
            ReleaseThumbnails();

            _list.Clear();
            _rows.Clear();

            if (_items.Count == 0)
            {
                var empty = new Label("一覧がありません");
                empty.AddToClassList("caption");
                _list.Add(empty);
                return;
            }

            // 実機では既定の保管庫を作る（Editor の試験は BindThumbnailCache で差し替える）。
            _thumbnailCache ??= MediaThumbnailCache.CreateDefault();

            _thumbnailCts = new CancellationTokenSource();
            var token = _thumbnailCts.Token;

            for (var i = 0; i < _items.Count; i++)
            {
                var index = i;
                var row = new VisualElement();
                row.AddToClassList("video-row");

                // 左にサムネイル（16:9）。取れるまでは下地のまま（失敗は黙る）。
                var thumb = new VisualElement();
                thumb.AddToClassList("video-row__thumb");
                thumb.pickingMode = PickingMode.Ignore; // 行が的。絵が指を吸わないように。
                row.Add(thumb);

                var title = new Label(_items[i].Title);
                title.AddToClassList("video-row__title");
                title.pickingMode = PickingMode.Ignore;
                row.Add(title);

                // 行そのものが的（材料の板と同じ流儀。Toggle は押し上げで反転するので使わない）。
                // 押し込みの見た目も付ける——実機で「押せたか」が分かるように。
                PokePress.BindButton(row, _debounce, $"video-item-{index}", () => Select(index));

                _list.Add(row);
                _rows.Add(row);

                LoadThumbnailAsync(_items[index], thumb, token).Forget();
            }
        }

        /// <summary>
        /// 行の絵を1枚。手元にあれば通信しない（<see cref="MediaThumbnailCache"/> が面倒を見る）ので、
        /// 一度出た一覧はオフラインでも絵つきで並ぶ。取れなければ黙って下地のまま。
        /// </summary>
        private async UniTaskVoid LoadThumbnailAsync(
            MediaItem item, VisualElement target, CancellationToken token)
        {
            if (_thumbnailCache == null || item == null || string.IsNullOrEmpty(item.ThumbnailUrl))
            {
                return;
            }

            Texture2D texture;
            try
            {
                texture = await _thumbnailCache.LoadAsync(item.VideoId, item.ThumbnailUrl, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (texture == null)
            {
                return;
            }

            // 待っている間に並べ直された／板が消えた。作ったものは自分で捨てる。
            if (token.IsCancellationRequested || this == null || target.panel == null)
            {
                DestroyTexture(texture);
                return;
            }

            _thumbnails.Add(texture);
            target.style.backgroundImage = new StyleBackground(texture);
        }

        private void CancelThumbnailLoads()
        {
            _thumbnailCts?.Cancel();
            _thumbnailCts?.Dispose();
            _thumbnailCts = null;
        }

        private void ReleaseThumbnails()
        {
            foreach (var texture in _thumbnails)
            {
                DestroyTexture(texture);
            }

            _thumbnails.Clear();
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }

        // ------------------------------------------------------------------ 操作

        /// <summary>一覧から選ぶ＝その動画を読み込んで再生する。</summary>
        public void Select(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                return;
            }

            _selected = index;
            _offListTitle = null;
            _handledRelatedId = null;
            SetAction($"一覧: 「{_items[index].Title}」→ 読み込み（{_items[index].VideoId}）");
            _player.Load(_items[index].VideoId);
            _player.SetVolume(_volume);
            _playing = true; // YoutubePlayer.Load は autoplay 既定。
            RefreshControls();
            EnsurePlayingLaterAsync().Forget();
        }

        public void TogglePlayPause()
        {
            if (_playing)
            {
                SetAction("一時停止を頼みました");
                _player.Pause();
                _playing = false;
            }
            else
            {
                // 選ぶ前でも押せる。youtube.html は既定の動画を cue して立つので、それが始まる。
                SetAction(_selected >= 0 ? "再生を頼みました" : "再生を頼みました（cue 済みの動画）");
                _player.Play();
                _playing = true;
                EnsurePlayingLaterAsync().Forget();
            }

            RefreshControls();
        }

        /// <summary>
        /// 再生を頼んだあと、YouTube が「再生中」と返さなければ WebView の中央をタップする。
        /// Editor（<see cref="NullVideoPlayer"/>）では何もしない——実機の WebView の癖への対策なので。
        /// 続けて何度も頼まれたら最後の1回だけが効く（<see cref="_fallbackSerial"/>）。
        /// </summary>
        private async UniTaskVoid EnsurePlayingLaterAsync()
        {
            if (_player == null || !_player.IsAvailable)
            {
                return;
            }

            var serial = ++_fallbackSerial;
            var token = this.GetCancellationTokenOnDestroy();
            await UniTask.Delay(GestureFallbackDelayMs, cancellationToken: token).SuppressCancellationThrow();
            if (token.IsCancellationRequested || serial != _fallbackSerial || _player == null)
            {
                return;
            }

            if (!_playing || _player.IsReportedPlaying)
            {
                return;
            }

            SetAction("再生の返事が無いので窓の中央をタップしました（操作なしの再生を WebView が拒む対策）");
            _player.TapCenter();
        }

        public void ChangeVolume(int delta)
        {
            _volume = Mathf.Clamp(_volume + delta, 0, 100);
            _player.SetVolume(_volume);
            SetAction($"音量 {_volume}");
            RefreshControls();
        }

        public void ToggleAspect() =>
            ApplyAspect(Aspect == VideoAspect.Landscape ? VideoAspect.Portrait : VideoAspect.Landscape);

        /// <summary>
        /// WebView の履歴を1つ戻る。関連動画を触った先で youtube.com 本体へ
        /// 遷移することがあり、そうなると埋め込みプレイヤーの操作（<c>youtube.html</c> の JS）が
        /// 効かなくなる。板から帰れる道を1つ置いておく。
        /// </summary>
        public void GoBack()
        {
            SetAction("戻る");
            _player?.GoBack();
        }

        // ------------------------------------------------------------------ 窓の見張り

        /// <summary>
        /// html が読み込み終わった瞬間に、ページ全体の縦スクロールを塞ぐ。
        /// 読み込みの前に JS を送っても消えるので、立ち上がりを待ってから1度だけ送る。
        /// </summary>
        private void WatchHtmlLoaded()
        {
            var loaded = _player != null && _player.IsHtmlLoaded;
            if (loaded == _htmlLoaded)
            {
                return;
            }

            _htmlLoaded = loaded;
            if (!loaded)
            {
                return; // 読み直し中。次に立ったらまた送る。
            }

            _player.SuppressPageScroll();
            SetAction("窓の縦スクロールを止めました（html 読込後）");
        }

        /// <summary>
        /// 窓が同梱の html の外（<c>youtube.com/watch</c> など）へ出ていないかを見張る。
        ///
        /// 埋め込みプレイヤーの関連動画は、同じ iframe の中で再生されるもの（何もしなくてよい）と、
        /// top frame を <c>youtube.com/watch</c> へ運ぶものがある。後者は html の JS が丸ごと
        /// 消えるので板からは何も操作できなくなる。id を抜いて埋め込みへ連れ戻す。
        /// </summary>
        private void WatchWindowUrl()
        {
            var url = _player?.CurrentUrl;
            if (string.IsNullOrEmpty(url))
            {
                return; // Editor、または初期化前。
            }

            if (url.IndexOf(EmbeddedPlayerHost, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 同梱の html の上に居る。次の遷移に備えて覚えを流す。
                _handledRelatedId = null;
                return;
            }

            if (url.IndexOf("watch?v=", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("youtu.be/", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("/shorts/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                SetAction($"窓が別の場所へ出ました（{Shorten(url)}）。「戻る」で帰れます");
                return;
            }

            var videoId = MediaJson.NormalizeVideoId(url);
            if (videoId == null || videoId == _handledRelatedId)
            {
                return;
            }

            _handledRelatedId = videoId;
            SetAction($"関連動画 {videoId} を開きました（埋め込みへ連れ戻します）");
            OpenRelatedAsync(videoId).Forget();
        }

        /// <summary>
        /// 関連動画を埋め込みプレイヤーで開き直す。
        /// html を読み直してから（＝舞台を作り直してから）<c>loadVideo</c> を頼む——
        /// youtube.com の上では html の JS が無いので、先に読み直さないと何も送れない。
        /// </summary>
        private async UniTaskVoid OpenRelatedAsync(string videoId)
        {
            var serial = ++_relatedSerial;
            var token = this.GetCancellationTokenOnDestroy();

            _player.ReloadHtml();
            _htmlLoaded = false;

            await UniTask.Delay(RelatedReloadDelayMs, cancellationToken: token).SuppressCancellationThrow();
            if (token.IsCancellationRequested || serial != _relatedSerial || _player == null)
            {
                return;
            }

            _selected = IndexOfVideoId(videoId);
            _offListTitle = _selected >= 0 ? null : $"関連動画 {videoId}";

            _player.Load(videoId);
            _player.SetVolume(_volume);
            _player.SetAspect(Aspect);
            _player.SuppressPageScroll();
            _playing = true;

            SetAction($"関連動画 {videoId} を読み込みました");
            RefreshControls();
            EnsurePlayingLaterAsync().Forget();
        }

        private int IndexOfVideoId(string videoId)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].VideoId == videoId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string Shorten(string url) =>
            url.Length <= 48 ? url : url.Substring(0, 47) + "…";

        // ------------------------------------------------------------------ 窓への触り

        /// <summary>押している指1本の様子。タップかドラッグかをここで決める。</summary>
        private sealed class WindowTouch
        {
            public Vector2 DownUv;
            public Vector2 LastUv;
            public float DownTime;

            /// <summary>閾値を越えて DOWN を送った＝以後 DRAG を流す。</summary>
            public bool Dragging;
        }

        /// <summary>
        /// 窓（<c>videoArea</c>）を触った場所を <see cref="IVideoPlayer"/> へ流す。
        /// 押し下げでは何も送らない——レイの着地点もポークの指先も揺れるので、そのまま DOWN を
        /// 送ると押し上げまでに DRAG が挟まり、WebView がスクロールと解釈してクリックを出さない。
        ///   - <see cref="TapMoveThreshold"/> を越えたら押し下げの座標で DOWN、以後 DRAG
        ///   - 越えないまま <see cref="TapMaxSeconds"/> 以内に離れたら <see cref="IVideoPlayer.Tap"/>
        ///   - 越えないまま長く留まって離れたら何も送らない（板に手を置いただけ）
        /// どちらに転んだかは札（<c>actionLine</c>）に出す。釦ではないので
        /// <see cref="PokePress"/> は通さない。
        /// </summary>
        private void BindVideoAreaTouch()
        {
            if (_videoArea == null)
            {
                return;
            }

            _videoArea.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!TryVideoUv(evt.position, out var uv))
                {
                    return;
                }

                // まだ WebView へは何も送らない（送ると揺れが DRAG になる）。
                _touching[evt.pointerId] = new WindowTouch
                {
                    DownUv = uv,
                    LastUv = uv,
                    DownTime = Time.unscaledTime,
                    Dragging = false,
                };
            });

            _videoArea.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_touching.TryGetValue(evt.pointerId, out var touch) ||
                    !TryVideoUv(evt.position, out var uv))
                {
                    return;
                }

                touch.LastUv = uv;

                if (touch.Dragging)
                {
                    _player?.Touch(VideoTouchPhase.Drag, uv.x, uv.y);
                    return;
                }

                if (MovedFarEnough(touch.DownUv, uv))
                {
                    // ここから先はスクロール。押し下げの座標で DOWN を送ってから DRAG を流す
                    // ——WebView は DOWN の無い DRAG を無視する。
                    touch.Dragging = true;
                    _player?.Touch(VideoTouchPhase.Down, touch.DownUv.x, touch.DownUv.y);
                    _player?.Touch(VideoTouchPhase.Drag, uv.x, uv.y);
                    SetAction($"窓: ドラッグ開始（{touch.DownUv.x:0.00}, {touch.DownUv.y:0.00}）");
                }
            });

            _videoArea.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_touching.TryGetValue(evt.pointerId, out var touch) &&
                    TryVideoUv(evt.position, out var uv))
                {
                    touch.LastUv = uv;
                }

                ReleaseTouch(evt.pointerId);
            });

            _videoArea.RegisterCallback<PointerLeaveEvent>(evt => ReleaseTouch(evt.pointerId));
            _videoArea.RegisterCallback<PointerOutEvent>(evt => ReleaseTouch(evt.pointerId));
            _videoArea.RegisterCallback<PointerCaptureOutEvent>(evt => ReleaseTouch(evt.pointerId));
        }

        /// <summary>
        /// 動いた量を絵の幅の比で測る。縦は絵の縦横比を掛けて同じ物差しに揃える
        /// （9:16 では縦 1% が横 1.8% ぶんの距離になる）。
        /// </summary>
        private bool MovedFarEnough(Vector2 from, Vector2 to)
        {
            var heightOverWidth = Aspect == VideoAspect.Portrait ? 16f / 9f : 9f / 16f;
            var du = to.x - from.x;
            var dv = (to.y - from.y) * heightOverWidth;
            return du * du + dv * dv > TapMoveThreshold * TapMoveThreshold;
        }

        private void ReleaseTouch(int pointerId)
        {
            if (!_touching.TryGetValue(pointerId, out var touch))
            {
                return;
            }

            _touching.Remove(pointerId);

            if (touch.Dragging)
            {
                // 送り損ねると WebView の中で指が押されたままになる。必ず離す。
                _player?.Touch(VideoTouchPhase.Up, touch.LastUv.x, touch.LastUv.y);
                SetAction($"窓: ドラッグ終了（{touch.LastUv.x:0.00}, {touch.LastUv.y:0.00}）");
                return;
            }

            var held = Time.unscaledTime - touch.DownTime;
            if (held > TapMaxSeconds)
            {
                SetAction($"窓: 長押し {held:0.0}秒（何も送りません）");
                return;
            }

            // ここだけが「クリック」。押し下げの座標で DOWN → 80ms → UP。
            _player?.Tap(touch.DownUv.x, touch.DownUv.y);
            SetAction($"窓: タップ（{touch.DownUv.x:0.00}, {touch.DownUv.y:0.00}）");
        }

        /// <summary>
        /// 板の座標（<see cref="PointerEventBase{T}.position"/>）を絵の中の比へ写す。
        ///
        /// 窓（<c>videoArea</c>）と絵は同じ大きさではない——絵は目当ての比（16:9 か 9:16）の
        /// 一番大きい矩形として窓の中に収まる（<see cref="LayoutSurface"/> と同じ算）ので、
        /// 上下（または左右）に余白が出る。そこを触っても WebView には何も無いので、
        /// 絵の外なら false を返して送らない。
        ///
        /// v（縦）は上から下へ 0→1。HTML の座標系（UI Toolkit と同じ向き）に合わせてある。
        /// </summary>
        private bool TryVideoUv(Vector2 panelPosition, out Vector2 uv)
        {
            uv = Vector2.zero;
            if (_videoArea == null)
            {
                return false;
            }

            var box = _videoArea.contentRect;
            if (box.width <= 0f || box.height <= 0f)
            {
                return false;
            }

            var targetRatio = Aspect == VideoAspect.Portrait ? 9f / 16f : 16f / 9f;
            var width = box.width;
            var height = width / targetRatio;
            if (height > box.height)
            {
                height = box.height;
                width = height * targetRatio;
            }

            var left = box.x + (box.width - width) / 2f;
            var top = box.y + (box.height - height) / 2f;

            var local = _videoArea.WorldToLocal(panelPosition);
            var u = (local.x - left) / width;
            var v = (local.y - top) / height;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                return false;
            }

            uv = new Vector2(u, v);
            return true;
        }

        /// <summary>
        /// 向きを変える。板の寸法・当たり判定・絵の大きさを全部合わせる。
        /// 板は下辺の中央を動かさない（上へ伸びる。レシピの板の上に置いているので下は空いていない）。
        /// </summary>
        public void ApplyAspect(VideoAspect aspect)
        {
            // 覚えていた下辺の中央（起動時に Bootstrap が置いた点）は、配置モードやアンカーの復元で
            // 板が動いたあとは古い。だから寸法を変える直前に今の Transform から取り直す
            // ——さもないと向きを変えた瞬間に板が壁の奥などへ飛ぶ。
            RememberBottomCenterFromTransform();

            Aspect = aspect;

            var width = aspect == VideoAspect.Portrait ? PortraitWidthUnits : LandscapeWidthUnits;
            var height = aspect == VideoAspect.Portrait ? PortraitHeightUnits : LandscapeHeightUnits;

            WorldSpacePanelFactory.Resize(gameObject, _document, width, height);
            ApplyAnchor();

            _player?.SetAspect(aspect);

            // 向きを変えると WebView が resize され、html の style も入れ替わる。
            // 縦スクロールを塞ぐ JS はそのたびに送り直す。
            _player?.SuppressPageScroll();

            if (_aspectButton != null)
            {
                // ボタンには「押したらこうなる」を書く。
                _aspectButton.text = aspect == VideoAspect.Portrait ? "16:9" : "9:16";
            }

            LayoutSurface();
        }

        // ------------------------------------------------------------------ 置き場

        /// <summary>
        /// 板の下辺の中央をここに置く（<c>Bootstrap</c> から）。
        /// 板の原点は左上（<see cref="WorldSpacePanelFactory.PanelPivot"/>）なので、
        /// 寸法が変わるたびにここから原点を計算し直す。
        /// </summary>
        public void PlaceAtBottomCenter(Vector3 bottomCenter, Quaternion rotation)
        {
            _bottomCenter = bottomCenter;
            _rotation = rotation;
            _anchored = true;
            ApplyAnchor();
            LayoutSurface();
        }

        /// <summary>
        /// 今の位置・向き・寸法から下辺の中央を取り直す。
        /// まだ一度も置かれていない（試験や Awake の最中）なら何もしない——
        /// 寸法が既定値のままで計算すると、板が原点からずれる。
        /// </summary>
        private void RememberBottomCenterFromTransform()
        {
            if (!_anchored || _document == null)
            {
                return;
            }

            var size = _document.worldSpaceSize;
            if (size.x <= 0f || size.y <= 0f)
            {
                return;
            }

            _rotation = transform.rotation;
            var right = _rotation * Vector3.right;
            var up = _rotation * Vector3.up;
            _bottomCenter = transform.position + right * (ToMeters(size.x) / 2f) - up * ToMeters(size.y);
        }

        private void ApplyAnchor()
        {
            if (!_anchored || _document == null)
            {
                return;
            }

            var size = _document.worldSpaceSize;
            var widthMeters = ToMeters(size.x);
            var heightMeters = ToMeters(size.y);

            var right = _rotation * Vector3.right;
            var up = _rotation * Vector3.up;

            transform.SetPositionAndRotation(
                _bottomCenter - right * (widthMeters / 2f) + up * heightMeters, _rotation);
        }

        /// <summary>UI px → 実寸（m）。板の縮尺は実際の Transform から読む（表示の設定で変わり得る）。</summary>
        private float ToMeters(float units) =>
            units / WorldSpacePanelFactory.PanelPixelsPerUnit * transform.localScale.x;

        // ------------------------------------------------------------------ 絵（WebView の板）

        /// <summary>
        /// TLab の RawImage を「動画の窓」に合わせる。
        /// 窓の実測（板のローカル単位。UI px ÷ PixelsPerUnit）に、目当ての比の一番大きい矩形を
        /// 収める。余白の計算違いがあっても比だけは崩れない。
        /// </summary>
        private void LayoutSurface()
        {
            if (_bridge == null || _videoArea == null || _playerRoot == null)
            {
                return;
            }

            var surface = _bridge.Surface;
            var canvas = _bridge.Canvas;
            if (surface == null || canvas == null)
            {
                return;
            }

            // worldBound は「どの座標系で返るか」が板の構成で変わって見える（絵が元の位置の
            // ずっと右上に 10 倍の大きさで出た）。だから板の根から見た px の矩形を自分で取り、
            // 既知の換算だけで置く: 根の (0,0) は板の Transform の原点（左上）、
            // UI の y は下向きなので局所座標では負、1px = 1/PixelsPerUnit 局所単位。
            var px = _videoArea.ChangeCoordinatesTo(_root, new Rect(Vector2.zero, _videoArea.layout.size));
            var rect = new Rect(
                px.x / WorldSpacePanelFactory.PanelPixelsPerUnit,
                -(px.y + px.height) / WorldSpacePanelFactory.PanelPixelsPerUnit,
                px.width / WorldSpacePanelFactory.PanelPixelsPerUnit,
                px.height / WorldSpacePanelFactory.PanelPixelsPerUnit);
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return; // レイアウトがまだ。GeometryChangedEvent でまた来る。
            }

            var targetRatio = Aspect == VideoAspect.Portrait ? 9f / 16f : 16f / 9f; // 幅 ÷ 高さ
            var width = rect.width;
            var height = width / targetRatio;
            if (height > rect.height)
            {
                height = rect.height;
                width = height * targetRatio;
            }

            // プレハブが持っている縮尺・位置を打ち消して、板の座標系に載せ直す。
            _playerRoot.transform.SetParent(transform, false);
            _playerRoot.transform.localRotation = Quaternion.identity;
            _playerRoot.transform.localScale = Vector3.one * SurfaceCanvasScale;
            _playerRoot.transform.localPosition = new Vector3(rect.center.x, rect.center.y, SurfaceOffset);

            canvas.localPosition = Vector3.zero;
            canvas.localRotation = Quaternion.identity;
            canvas.localScale = Vector3.one;

            // 板のローカル単位 → Canvas の px。根の縮尺が SurfaceCanvasScale なので、その逆数。
            // ここに板の縮尺 0.2 を掛けてはいけない（絵が 1/5 の大きさになる）。
            var canvasPxPerUnit = 1f / SurfaceCanvasScale;
            surface.localScale = Vector3.one;
            surface.localRotation = Quaternion.identity;
            surface.anchorMin = surface.anchorMax = new Vector2(0.5f, 0.5f);
            surface.pivot = new Vector2(0.5f, 0.5f);
            surface.anchoredPosition3D = Vector3.zero;
            surface.sizeDelta = new Vector2(width * canvasPxPerUnit, height * canvasPxPerUnit);

            // 札に出す（寸法の計算違いを実機で見分けるため）。実寸は板の縮尺込み。
            var scale = transform.localScale.x;
            // 位置の狂いを実機で見分けるため、窓の矩形（板のローカル単位）と絵の置き場も出す。
            _surfaceInfo =
                $"絵 {width * scale * 1000f:0}×{height * scale * 1000f:0}mm / 窓 x{rect.x:0.00} y{rect.y:0.00} w{rect.width:0.00} h{rect.height:0.00} 中心({rect.center.x:0.00},{rect.center.y:0.00})";
        }

        // ------------------------------------------------------------------ 札

        private void SetAction(string text)
        {
            _lastAction = text ?? string.Empty;
            RefreshStatus();
        }

        /// <summary>
        /// Unity のログのうち動画に関わるものを札へ。<c>YoutubePlayer</c> は状態の変化や失敗を
        /// Debug.Log／LogError で出すので、実機ではこれが一番早い手掛かりになる。
        /// </summary>
        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return;
            }

            if (!condition.Contains("YoutubePlayer") && !condition.Contains("Player ") &&
                !condition.Contains("TLab") && !condition.Contains("動画の板") &&
                !condition.Contains("html loaded") && !condition.Contains("send "))
            {
                return;
            }

            var mark = type == LogType.Error || type == LogType.Exception ? "！" : "・";
            _lastLog = mark + condition.Replace("\n", " ");
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (_statusLine != null)
            {
                var status = _player != null ? _player.StatusLine : "—";
                _statusLine.text = string.IsNullOrEmpty(_surfaceInfo) ? status : $"{status} / {_surfaceInfo}";
            }

            if (_actionLine != null)
            {
                var log = string.IsNullOrEmpty(_lastLog) ? string.Empty : $"  {_lastLog}";
                _actionLine.text = $"{_lastAction}{log}";
            }

            if (_errorLine != null)
            {
                var error = _player?.LastError;
                var has = !string.IsNullOrEmpty(error);
                _errorLine.text = has ? $"失敗: {error}" : string.Empty;
                _errorLine.EnableInClassList("is-hidden", !has);
            }
        }

        // ------------------------------------------------------------------ 見た目

        private void RefreshControls()
        {
            if (_root == null)
            {
                return;
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                if (i == _selected)
                {
                    _rows[i].AddToClassList("video-row--selected");
                }
                else
                {
                    _rows[i].RemoveFromClassList("video-row--selected");
                }
            }

            if (_playPauseButton != null)
            {
                _playPauseButton.text = _playing ? "一時停止" : "再生";
                // 常に押せる（無効の Button はポークも受けず「反応がない」に見える）。
                _playPauseButton.SetEnabled(true);
            }

            if (_volumeLabel != null)
            {
                _volumeLabel.text = $"音量 {_volume}";
            }

            if (_nowPlaying != null)
            {
                _nowPlaying.text = _offListTitle
                                   ?? (_selected >= 0 && _selected < _items.Count
                                       ? _items[_selected].Title
                                       : "—");
            }

            if (_notice != null)
            {
                // WebView は Android のプラグイン。Editor では絵が出ないので札で断る。
                var available = _player != null && _player.IsAvailable;
                _notice.text = available ? string.Empty : "動画は実機で（Quest 3）";
                _notice.style.display = available ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }
    }
}
