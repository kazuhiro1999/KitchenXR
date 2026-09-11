using System.Collections.Generic;
using KitchenXR.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation.Video
{
    /// <summary>
    /// 動画の板（設計 §6・§9・ROADMAP P4。4枚目の板）。
    ///
    /// レシピとは**無関係**の娯楽の板（設計 §0 の柱 C「ショーツとかドラマを見ながら料理したい。
    /// レシピを YouTube で見たいわけではない」）。だから <c>CookSession</c> には一切触れない。
    ///
    /// 中身は2つに分かれている:
    ///   - **操作部**: UI Toolkit の板（この <see cref="UIDocument"/>）。一覧・再生／一時停止・
    ///     音量 ±・16:9 ⇄ 9:16。ボタンは他の板と同じ <see cref="PokePress"/>（押し下げで発火）。
    ///     **文字入力は置かない**（設計 §6）。
    ///   - **絵**: 主人の <c>YoutubePlayer.prefab</c> が持つワールド空間の Canvas ＋ RawImage。
    ///     板の「動画の窓」の**すぐ手前**（0.6mm）に重ねる。
    ///
    /// なぜ UI Toolkit の <c>backgroundImage</c> に貼らず、主人の RawImage をそのまま使うか:
    ///   1. <c>TLabWebView</c> は毎フレーム <c>m_rawImage.texture</c> を自分で差し替える
    ///      （GLES は外部テクスチャの更新、Vulkan は毎フレーム新しい <c>Texture2D</c>）。
    ///      SDK が試されているのはこの経路で、ここに割り込む理由が無い。
    ///   2. UI Toolkit は背景画像を**動的アトラス**へ焼くことがある。焼かれると外部テクスチャの
    ///      更新が届かず、最初の1枚で止まる（実機でしか出ない止まり方で、直すのが難しい）。
    ///   3. 主人の板は元から「Canvas ＋ RawImage」で出来ている。包むだけで済む。
    /// 重ねる代わりに、**ポークの当たり判定は UI 側の板だけ**が持つ
    /// （プレハブ側の <c>GraphicRaycaster</c> と <c>Button</c> は
    /// <see cref="YoutubePlayerBridge.Initialize"/> で止めてある）。
    /// 絵は板の面より手前にあるので、窓を触っても「入り」の判定には届かない。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class VideoPanel : MonoBehaviour
    {
        // 板の大きさ（UI px。theme.uss の換算で 1px = 2mm）。
        // 16:9 は設計どおり**動画の窓の幅が 50cm**（= 250px）になるように決めた。
        // 9:16 は幅 50cm だと高さ 89cm になって台所に置けないので、
        // 窓の**高さ**を 36cm（180px）に取り、幅はその 9:16（20.25cm）とした。
        // ——どちらの向きでも板の寸法が変わる（縦は高く細く、横は低く広く）。
        public const float LandscapeWidthUnits = 266f;  // ≒ 53.2cm（窓 50cm ＋ 余白）
        public const float LandscapeHeightUnits = 266f; // ≒ 53.2cm
        public const float PortraitWidthUnits = 156f;   // ≒ 31.2cm
        public const float PortraitHeightUnits = 296f;  // ≒ 59.2cm

        /// <summary>絵を板の面より手前へ出す量（板のローカル単位。負が手前）。実寸 0.6mm。</summary>
        private const float SurfaceOffset = -0.003f;

        /// <summary>Canvas の 1px を実寸 1mm にするための縮尺（板の localScale 0.2 と合わせて 0.001m/px）。</summary>
        private const float SurfaceCanvasScale = 0.005f;

        /// <summary>音量の刻み（設計 §7「押せるのは大きく疎なボタン」なので細かくしない）。</summary>
        public const int VolumeStep = 10;

        public const int DefaultVolume = 70;

        [SerializeField] private GameObject _playerRoot;

        private readonly ClickDebounce _debounce = new ClickDebounce();
        private readonly List<VisualElement> _rows = new List<VisualElement>();

        private UIDocument _document;
        private VisualElement _root;
        private ScrollView _list;
        private VisualElement _videoArea;
        private Label _notice;
        private Label _nowPlaying;
        private Label _volumeLabel;
        private Button _playPauseButton;
        private Button _aspectButton;

        private IVideoPlayer _player;
        private YoutubePlayerBridge _bridge;

        private IReadOnlyList<MediaItem> _items = new List<MediaItem>();
        private int _selected = -1;
        private int _volume = DefaultVolume;
        private bool _playing;

        // 向きを変えても動かさない点（板の下辺の中央）。上へ伸ばすと天井に向かうので、
        // 下辺を固定して**上へ**伸ばす……のではなく、下辺を固定して高さだけ変える。
        private Vector3 _bottomCenter;
        private Quaternion _rotation = Quaternion.identity;
        private bool _anchored;

        public VideoAspect Aspect { get; private set; } = VideoAspect.Landscape;

        public IReadOnlyList<MediaItem> Items => _items;

        public int SelectedIndex => _selected;

        public int Volume => _volume;

        public bool IsPlaying => _playing;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _root = _document.rootVisualElement;

            _list = _root.Q<ScrollView>("mediaList");
            _videoArea = _root.Q<VisualElement>("videoArea");
            _notice = _root.Q<Label>("editorNotice");
            _nowPlaying = _root.Q<Label>("nowPlayingLabel");
            _volumeLabel = _root.Q<Label>("volumeLabel");
            _playPauseButton = _root.Q<Button>("playPauseButton");
            _aspectButton = _root.Q<Button>("aspectButton");

            PokePress.BindButton(_playPauseButton, _debounce, "video-playpause", TogglePlayPause);
            PokePress.BindButton(_root.Q<Button>("volumeDownButton"), _debounce, "video-volume-down",
                () => ChangeVolume(-VolumeStep));
            PokePress.BindButton(_root.Q<Button>("volumeUpButton"), _debounce, "video-volume-up",
                () => ChangeVolume(+VolumeStep));
            PokePress.BindButton(_aspectButton, _debounce, "video-aspect", ToggleAspect);

            // 主人のプレハブを包む。実機でなければここで眠らせる（板は札を出す）。
            _bridge = new YoutubePlayerBridge(_playerRoot);
            _bridge.Initialize();
            _player = _bridge.IsAvailable ? (IVideoPlayer)_bridge : new NullVideoPlayer();

            // 板の大きさが決まってから絵を重ねる（レイアウト待ち）。
            _videoArea?.RegisterCallback<GeometryChangedEvent>(_ => LayoutSurface());

            ApplyAspect(Aspect);
            RefreshControls();
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
            RefreshControls();
        }

        /// <summary>一覧を貼り替える（<c>media.json</c> から。最大 8 件は <see cref="MediaJson"/> が守る）。</summary>
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

            _list.Clear();
            _rows.Clear();

            if (_items.Count == 0)
            {
                var empty = new Label("一覧がありません（media.json）");
                empty.AddToClassList("caption");
                _list.Add(empty);
                return;
            }

            for (var i = 0; i < _items.Count; i++)
            {
                var index = i;
                var row = new VisualElement();
                row.AddToClassList("video-row");

                var title = new Label(_items[i].Title);
                title.AddToClassList("video-row__title");
                row.Add(title);

                // 行そのものが的（材料の板と同じ流儀。Toggle は押し上げで反転するので使わない）。
                PokePress.Bind(row, _debounce, $"video-item-{index}", () => Select(index));

                _list.Add(row);
                _rows.Add(row);
            }
        }

        // ------------------------------------------------------------------ 操作

        /// <summary>一覧から選ぶ＝その動画を読み込んで再生する（設計 §6）。</summary>
        public void Select(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                return;
            }

            _selected = index;
            _player.Load(_items[index].VideoId);
            _player.SetVolume(_volume);
            _playing = true; // YoutubePlayer.Load は autoplay 既定。
            RefreshControls();
        }

        public void TogglePlayPause()
        {
            if (_selected < 0)
            {
                return; // まだ何も選んでいない。
            }

            if (_playing)
            {
                _player.Pause();
                _playing = false;
            }
            else
            {
                _player.Play();
                _playing = true;
            }

            RefreshControls();
        }

        public void ChangeVolume(int delta)
        {
            _volume = Mathf.Clamp(_volume + delta, 0, 100);
            _player.SetVolume(_volume);
            RefreshControls();
        }

        public void ToggleAspect() =>
            ApplyAspect(Aspect == VideoAspect.Landscape ? VideoAspect.Portrait : VideoAspect.Landscape);

        /// <summary>
        /// 向きを変える。板の寸法・当たり判定・絵の大きさを全部合わせる。
        /// 板は**下辺の中央**を動かさない（上へ伸びる。レシピの板の上に置いているので下は空いていない）。
        /// </summary>
        public void ApplyAspect(VideoAspect aspect)
        {
            Aspect = aspect;

            var width = aspect == VideoAspect.Portrait ? PortraitWidthUnits : LandscapeWidthUnits;
            var height = aspect == VideoAspect.Portrait ? PortraitHeightUnits : LandscapeHeightUnits;

            WorldSpacePanelFactory.Resize(gameObject, _document, width, height);
            ApplyAnchor();

            _player?.SetAspect(aspect);

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

        private static float ToMeters(float units) =>
            units / WorldSpacePanelFactory.PanelPixelsPerUnit * WorldSpacePanelFactory.PanelLocalScale;

        // ------------------------------------------------------------------ 絵（WebView の板）

        /// <summary>
        /// 主人の RawImage を「動画の窓」に合わせる。
        /// 窓の実測（<c>worldBound</c>＝板のローカル単位）に、目当ての比の**一番大きい矩形**を
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

            var rect = _videoArea.worldBound;
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

            // 板のローカル単位 → Canvas の px（1px = 1mm 実寸）。
            var pxPerUnit = WorldSpacePanelFactory.PanelLocalScale / SurfaceCanvasScale;
            surface.localScale = Vector3.one;
            surface.localRotation = Quaternion.identity;
            surface.anchorMin = surface.anchorMax = new Vector2(0.5f, 0.5f);
            surface.pivot = new Vector2(0.5f, 0.5f);
            surface.anchoredPosition3D = Vector3.zero;
            surface.sizeDelta = new Vector2(width * pxPerUnit, height * pxPerUnit);
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
                _playPauseButton.SetEnabled(_selected >= 0);
            }

            if (_volumeLabel != null)
            {
                _volumeLabel.text = $"音量 {_volume}";
            }

            if (_nowPlaying != null)
            {
                _nowPlaying.text = _selected >= 0 ? _items[_selected].Title : "—";
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
