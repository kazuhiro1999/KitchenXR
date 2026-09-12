#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KitchenXR.Domain;
using KitchenXR.Platform.Null;
using KitchenXR.Presentation;
using KitchenXR.Presentation.Video;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace KitchenXR.Tests.PlayMode
{
    /// <summary>
    /// 動画の板の検算。
    ///
    /// WebView は Android のプラグインなので Editor では絵が出ない——
    /// だから板は <see cref="IVideoPlayer"/> の口だけを見る作りになっていて、
    /// ここではそこに <see cref="NullVideoPlayer"/> を挿して
    /// 「一覧が描かれるか」「選ぶと Load が呼ばれるか」「向きを変えると板の寸法が変わるか」
    /// を実際のポークまで含めて示す。
    /// 板の組み立ては Kitchen.unity と同じ <see cref="WorldSpacePanelFactory"/> を通す。
    /// </summary>
    public class VideoPanelTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string VideoUxmlPath = "Assets/KitchenXR/Presentation/UI/VideoPanel.uxml";

        private const float StartDepthMeters = -0.06f;

        private GameObject _panelGo;
        private GameObject _pokeGo;
        private GameObject _rigGo;
        private GameObject _cameraGo;
        private GameObject _eventSystemGo;
        private GameObject _managerGo;

        private VideoPanel _panel;
        private NullVideoPlayer _player;
        private XRPokeInteractor _poke;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _panelGo, _pokeGo, _rigGo, _cameraGo, _eventSystemGo, _managerGo })
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }

            _panelGo = _pokeGo = _rigGo = _cameraGo = _eventSystemGo = _managerGo = null;
            _panel = null;
            _player = null;
            _poke = null;

            yield return null;
        }

        private static IReadOnlyList<MediaItem> SampleItems(int count) =>
            Enumerable.Range(0, count)
                .Select(i => new MediaItem($"動画 {i}", $"aaaaaaaaa{i:00}"))
                .ToList();

        private IEnumerator BuildAll(int itemCount = 3)
        {
            _managerGo = new GameObject("XR Interaction Manager", typeof(XRInteractionManager));

            _cameraGo = new GameObject("Main Camera", typeof(Camera));
            _cameraGo.tag = "MainCamera";
            _cameraGo.transform.position = new Vector3(0f, 0f, -1f);

            _eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
            var inputModule = _eventSystemGo.AddComponent<XRUIInputModule>();
            inputModule.bypassUIToolkitEvents = false;

            _rigGo = new GameObject("UI Toolkit Support");
            _rigGo.AddComponent<XRUIToolkitManager>();
            var panelInput = _rigGo.AddComponent<PanelInputConfiguration>();
            panelInput.panelInputRedirection = PanelInputConfiguration.PanelInputRedirection.Never;
            panelInput.processWorldSpaceInput = true;
            ForceEditorInputRedirect();

            yield return null;

            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(VideoUxmlPath);
            Assert.IsNotNull(panelSettings, "KitchenPanelSettings が見つかりません。");
            Assert.IsNotNull(uxml, "VideoPanel.uxml が見つかりません。");

            _panelGo = new GameObject("VideoPanel");
            _panelGo.SetActive(false);
            _panelGo.transform.position = Vector3.zero;
            _panelGo.transform.rotation = Quaternion.identity;

            WorldSpacePanelFactory.Configure(
                _panelGo, panelSettings, uxml,
                VideoPanel.LandscapeWidthUnits, VideoPanel.LandscapeHeightUnits);

            _panelGo.SetActive(true);
            yield return null;

            // _playerRoot は挿さない（Editor には WebView が無い）。板は札を出したまま動く。
            _panel = _panelGo.AddComponent<VideoPanel>();

            _player = new NullVideoPlayer();
            _panel.BindPlayer(_player);

            // 絵は取りに行かない（EditMode・PlayMode 試験はネットに出ない）。
            // 取得口を持たない保管庫を挿しておけば、行は下地のまま並ぶ。
            _panel.BindThumbnailCache(new MediaThumbnailCache(
                Path.Combine(Application.temporaryCachePath, "video-panel-tests"), null));

            _panel.BindMedia(SampleItems(itemCount));

            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }

            _pokeGo = new GameObject("Poke Interactor");
            _pokeGo.SetActive(false);
            _poke = _pokeGo.AddComponent<XRPokeInteractor>();
            _poke.enableUIInteraction = true;
            _poke.requirePokeFilter = false;
            _poke.pokeDepth = 0.1f;
            _pokeGo.transform.position = new Vector3(0f, 0f, StartDepthMeters);
            _pokeGo.SetActive(true);

            yield return null;
        }

        /// <summary>
        /// 試験のための細工（PokeButtonInteractionTests と同じ。実機では要らない分岐）。
        /// </summary>
        private static void ForceEditorInputRedirect()
        {
            var type = typeof(PanelSettings).Assembly.GetType("UnityEngine.UIElements.DefaultEventSystem");
            var field = type?.GetField("IsEditorRemoteConnected",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "DefaultEventSystem.IsEditorRemoteConnected が見つかりません。");

            Func<bool> alwaysTrue = () => true;
            field.SetValue(null, alwaysTrue);
        }

        private VisualElement Root => _panelGo.GetComponent<UIDocument>().rootVisualElement;

        private Vector3 WorldPositionOf(VisualElement element)
        {
            var rect = element.worldBound;
            Assert.Greater(rect.width, 0f, "UI 要素の幅が 0 です（レイアウトが未確定）。");
            return _panelGo.transform.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f));
        }

        private IEnumerator PokeAt(Vector3 worldTarget, float depthMeters)
        {
            var forward = _panelGo.transform.forward;

            for (var d = StartDepthMeters; d <= depthMeters; d += 0.002f)
            {
                _pokeGo.transform.position = worldTarget + forward * d;
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            for (var d = depthMeters; d >= StartDepthMeters; d -= 0.002f)
            {
                _pokeGo.transform.position = worldTarget + forward * d;
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// 突いたまま横へ引いてから抜く（＝スクロール）。
        /// <paramref name="worldOffset"/> は板の面に沿った動き。
        /// </summary>
        private IEnumerator PokeAndDrag(Vector3 worldTarget, float depthMeters, Vector3 worldOffset)
        {
            var forward = _panelGo.transform.forward;

            for (var d = StartDepthMeters; d <= depthMeters; d += 0.002f)
            {
                _pokeGo.transform.position = worldTarget + forward * d;
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            for (var i = 1; i <= 10; i++)
            {
                _pokeGo.transform.position =
                    worldTarget + worldOffset * (i / 10f) + forward * depthMeters;
                yield return null;
            }

            for (var d = depthMeters; d >= StartDepthMeters; d -= 0.002f)
            {
                _pokeGo.transform.position = worldTarget + worldOffset + forward * d;
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        // ---------------------------------------------------------------- 本題

        [UnityTest]
        public IEnumerator 板が立って操作部が揃っている()
        {
            yield return BuildAll();

            Assert.IsNotNull(Root.Q<ScrollView>("mediaList"), "題名の列がありません。");
            Assert.IsNotNull(Root.Q<VisualElement>("videoArea"), "動画の窓がありません。");
            Assert.IsNotNull(Root.Q<Button>("playPauseButton"), "再生／一時停止がありません。");
            Assert.IsNotNull(Root.Q<Button>("volumeDownButton"), "音量−がありません。");
            Assert.IsNotNull(Root.Q<Button>("volumeUpButton"), "音量＋がありません。");
            Assert.IsNotNull(Root.Q<Button>("aspectButton"), "16:9／9:16 の切り替えがありません。");
            Assert.IsNotNull(Root.Q<Button>("backButton"),
                "「戻る」がありません（関連動画の先で youtube.com 本体へ行くと帰れなくなります）。");

            // 文字入力はパネルに置かない。
            Assert.IsEmpty(Root.Query<TextField>().ToList(), "動画の板に文字入力を置いてはいけません。");
        }

        [UnityTest]
        public IEnumerator 一覧が題名の数だけ描かれる()
        {
            yield return BuildAll(5);

            var rows = Root.Query<VisualElement>(className: "video-row").ToList();
            Assert.AreEqual(5, rows.Count, "題名の行が一覧の件数と合いません。");
            Assert.AreEqual("動画 0", rows[0].Q<Label>().text);
        }

        /// <summary>
        /// 一覧の行が「左にサムネイル・右に題名」になっていること。
        /// 絵そのものは取りに行かないので、枠が在って左に置かれていることだけを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator 一覧の行にサムネイルの枠が左にある()
        {
            yield return BuildAll(3);

            var rows = Root.Query<VisualElement>(className: "video-row").ToList();
            Assert.IsNotEmpty(rows);

            foreach (var row in rows)
            {
                var thumb = row.Q<VisualElement>(className: "video-row__thumb");
                var title = row.Q<Label>(className: "video-row__title");
                Assert.IsNotNull(thumb, "行にサムネイルの枠がありません。");
                Assert.IsNotNull(title, "行に題名がありません。");

                Assert.Greater(thumb.worldBound.width, 0f, "サムネイルの枠の幅が 0 です。");
                Assert.Less(thumb.worldBound.xMin, title.worldBound.xMin,
                    "サムネイルが題名より右にあります（左がサムネイル、右が題名）。");

                // 16:9 の枠（YouTube の絵の比）。
                Assert.AreEqual(16f / 9f, thumb.worldBound.width / thumb.worldBound.height, 0.1f,
                    "サムネイルの枠が 16:9 ではありません。");
            }
        }

        /// <summary>
        /// 一覧が窓の右横にあり、窓の幅が 50cm 取れていること。
        /// 定数の足し算は EditMode 試験が縛っているので、ここは実測で見る。
        /// </summary>
        [UnityTest]
        public IEnumerator 一覧が窓の右横にあり窓が16対9で50cm取れる()
        {
            yield return BuildAll(4);

            var area = Root.Q<VisualElement>("videoArea");
            var list = Root.Q<VisualElement>("listBlock");
            Assert.IsNotNull(area);
            Assert.IsNotNull(list);

            Assert.Greater(list.worldBound.xMin, area.worldBound.xMax - 0.01f,
                "一覧が窓の右横にありません。");

            // worldBound は板のローカル単位（UI px ÷ 100）。板の縮尺 0.2 を掛けて m になる。
            var widthCm = area.worldBound.width * WorldSpacePanelFactory.PanelLocalScale * 100f;
            var heightCm = area.worldBound.height * WorldSpacePanelFactory.PanelLocalScale * 100f;

            Assert.AreEqual(50f, widthCm, 0.6f, $"窓の幅が 50cm ではありません（実測 {widthCm:0.0}cm）。");
            Assert.GreaterOrEqual(heightCm, 50f / (16f / 9f) - 0.2f,
                $"窓が低すぎて 50cm の 16:9 が入りません（実測 {heightCm:0.0}cm）。"
                + "VideoPanel.LandscapeHeightUnits を増やすこと。");

            // 逆に、要るより高すぎると板が無駄に大きくなる（台所の壁は有限）。
            // 余りは札の折り返しぶんの取り置き——札は3行あり、それぞれ最大2行に折り返す
            // （`.video-status-line` の max-height 12px）。空いているときは 32.8cm、
            // 全部が2行になっても 28.2cm で 50cm の 16:9 がちょうど収まる。
            // ここを詰めると、実機で札が伸びた瞬間に絵が 50cm を割る。
            Assert.LessOrEqual(heightCm, 50f / (16f / 9f) + 5.5f,
                $"窓が必要より高く、板が無駄に大きくなっています（実測 {heightCm:0.0}cm、"
                + $"要るのは {50f / (16f / 9f):0.0}cm）。VideoPanel.LandscapeHeightUnits を削ること。");

            var listCm = list.worldBound.width * WorldSpacePanelFactory.PanelLocalScale * 100f;
            Assert.AreEqual(VideoPanel.ListWidthUnits * 0.2f, listCm, 0.6f,
                $"一覧の幅が定数と合いません（実測 {listCm:0.0}cm）。");
        }

        /// <summary>9:16（ショーツ）でも窓の高さ 36cm が取れること。</summary>
        [UnityTest]
        public IEnumerator 縦向きでも窓の高さが36cm取れる()
        {
            yield return BuildAll(4);

            _panel.ToggleAspect();
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            var area = Root.Q<VisualElement>("videoArea");
            var heightCm = area.worldBound.height * WorldSpacePanelFactory.PanelLocalScale * 100f;
            var widthCm = area.worldBound.width * WorldSpacePanelFactory.PanelLocalScale * 100f;

            Assert.GreaterOrEqual(heightCm, 36f - 0.6f,
                $"9:16 の窓の高さが 36cm に足りません（実測 {heightCm:0.0}cm）。"
                + "VideoPanel.PortraitHeightUnits を増やすこと。");
            Assert.GreaterOrEqual(widthCm, 36f * 9f / 16f - 0.6f,
                $"9:16 の窓の幅が足りません（実測 {widthCm:0.0}cm）。");

            // 9:16 の絵は幅で決まる（窓の幅 × 16/9 が絵の高さ）。窓がそれより高いぶんは
            // 札の折り返しの取り置き——余り過ぎていたら板を削る。
            Assert.LessOrEqual(heightCm, widthCm * 16f / 9f + 5.5f,
                $"9:16 の窓が必要より高く、板が無駄に大きくなっています"
                + $"（実測 {heightCm:0.0}cm、絵は {widthCm * 16f / 9f:0.0}cm）。"
                + "VideoPanel.PortraitHeightUnits を削ること。");
        }

        [UnityTest]
        public IEnumerator 一覧が空なら断りを出す()
        {
            yield return BuildAll(0);

            Assert.IsEmpty(Root.Query<VisualElement>(className: "video-row").ToList());
            Assert.AreEqual(0, _player.LoadedVideoIds.Count);
        }

        /// <summary>
        /// 一覧を指で突いて選ぶと、その動画 id で <c>Load</c> が呼ばれる。
        /// 実機では TLab の <c>YoutubePlayer.Load</c> がこの先に居る。
        /// </summary>
        [UnityTest]
        public IEnumerator 一覧をポークで選ぶとLoadが呼ばれる()
        {
            yield return BuildAll(3);

            var rows = Root.Query<VisualElement>(className: "video-row").ToList();
            yield return PokeAt(WorldPositionOf(rows[1]), 0.05f);

            Assert.AreEqual(1, _player.LoadedVideoIds.Count, "選んだのに Load が1回ではありません。");
            Assert.AreEqual("aaaaaaaaa01", _player.LastVideoId);
            Assert.AreEqual(1, _panel.SelectedIndex);
            Assert.IsTrue(_panel.IsPlaying, "選んだら再生が始まります（autoplay）。");
        }

        [UnityTest]
        public IEnumerator 再生と一時停止が切り替わる()
        {
            yield return BuildAll(2);

            _panel.Select(0);
            Assert.IsTrue(_panel.IsPlaying);

            _panel.TogglePlayPause();
            Assert.IsFalse(_panel.IsPlaying);
            Assert.AreEqual(1, _player.PauseCount);

            _panel.TogglePlayPause();
            Assert.IsTrue(_panel.IsPlaying);
            Assert.AreEqual(1, _player.PlayCount);

            yield return null;
        }

        [UnityTest]
        public IEnumerator 音量が上下して0から100に収まる()
        {
            yield return BuildAll(2);

            var start = _panel.Volume;
            _panel.ChangeVolume(+VideoPanel.VolumeStep);
            Assert.AreEqual(start + VideoPanel.VolumeStep, _panel.Volume);
            Assert.AreEqual(_panel.Volume, _player.Volume);

            for (var i = 0; i < 20; i++)
            {
                _panel.ChangeVolume(-VideoPanel.VolumeStep);
            }

            Assert.AreEqual(0, _panel.Volume, "音量が 0 を下回りました。");

            for (var i = 0; i < 30; i++)
            {
                _panel.ChangeVolume(+VideoPanel.VolumeStep);
            }

            Assert.AreEqual(100, _panel.Volume, "音量が 100 を超えました。");

            yield return null;
        }

        /// <summary>
        /// 16:9 ⇄ 9:16 で板の寸法が変わり、当たり判定も一緒に変わる
        /// （片方だけ変えると「触っているのに押せない」が戻る）。
        /// </summary>
        [UnityTest]
        public IEnumerator 向きを変えると板の寸法と当たり判定が変わる()
        {
            yield return BuildAll(2);

            var doc = _panelGo.GetComponent<UIDocument>();
            var collider = _panelGo.GetComponent<BoxCollider>();

            Assert.AreEqual(VideoAspect.Landscape, _panel.Aspect);
            Assert.AreEqual(VideoPanel.LandscapeWidthUnits, doc.worldSpaceSize.x, 0.01f);
            Assert.AreEqual(VideoPanel.LandscapeHeightUnits, doc.worldSpaceSize.y, 0.01f);

            _panel.ToggleAspect();
            yield return null;

            Assert.AreEqual(VideoAspect.Portrait, _panel.Aspect);
            Assert.AreEqual(VideoPanel.PortraitWidthUnits, doc.worldSpaceSize.x, 0.01f);
            Assert.AreEqual(VideoPanel.PortraitHeightUnits, doc.worldSpaceSize.y, 0.01f);
            Assert.Less(doc.worldSpaceSize.x, VideoPanel.LandscapeWidthUnits, "9:16 は横に細くなります。");
            Assert.Greater(doc.worldSpaceSize.y, VideoPanel.LandscapeHeightUnits, "9:16 は縦に高くなります。");

            var expected = WorldSpacePanelFactory.ColliderSizeFor(doc.worldSpaceSize.x, doc.worldSpaceSize.y);
            Assert.AreEqual(expected.x, collider.size.x, 0.001f, "当たり判定の幅が板と合いません。");
            Assert.AreEqual(expected.y, collider.size.y, 0.001f, "当たり判定の高さが板と合いません。");
            Assert.AreEqual(VideoAspect.Portrait, _player.Aspect, "WebView 側にも向きが伝わっていません。");

            _panel.ToggleAspect();
            yield return null;

            Assert.AreEqual(VideoAspect.Landscape, _panel.Aspect);
            Assert.AreEqual(VideoPanel.LandscapeWidthUnits, doc.worldSpaceSize.x, 0.01f);
        }

        // ---------------------------------------------------------------- 窓への触り

        /// <summary>
        /// 窓を触ると WebView へそのまま渡る（次の動画は埋め込みプレイヤー自身の関連動画で選ぶ）。
        ///
        /// 指で窓の中央を突くと、Down → …（Drag）… → Up の順で
        /// <see cref="IVideoPlayer.Touch"/> が呼ばれ、比は (0.5, 0.5) のあたりになる。
        /// 実機ではこの先に <c>TLabWebView.TouchEvent</c> が居て、YouTube の関連動画が押される。
        /// </summary>
        [UnityTest]
        public IEnumerator 窓をポークするとWebViewへ触りが渡る()
        {
            yield return BuildAll(2);

            var area = Root.Q<VisualElement>("videoArea");
            Assert.IsNotNull(area, "動画の窓がありません。");

            yield return PokeAt(WorldPositionOf(area), 0.05f);

            Assert.IsNotEmpty(_player.Touches, "窓を触っても WebView へ何も渡っていません。");

            var first = _player.Touches[0];
            Assert.AreEqual(VideoTouchPhase.Down, first.Phase, "最初の触りが押し下げではありません。");
            Assert.AreEqual(0.5f, first.U, 0.06f, "窓の中央を触ったのに横の比が中央になりません。");
            Assert.AreEqual(0.5f, first.V, 0.06f, "窓の中央を触ったのに縦の比が中央になりません。");

            var last = _player.Touches[_player.Touches.Count - 1];
            Assert.AreEqual(VideoTouchPhase.Up, last.Phase,
                "離したのに押し上げが届いていません（WebView の中で指が押されたままになります）。");
        }

        /// <summary>
        /// 窓を突いて（横へ動かさずに）離すとタップ1回として渡り、DRAG は1つも混ざらない。
        /// 押し下げをそのまま DOWN で流すと、指の揺れが DRAG になって
        /// WebView がスクロールと解釈し、クリックを出さない。
        /// </summary>
        [UnityTest]
        public IEnumerator 窓を突いて離すとタップとして渡りドラッグが混ざらない()
        {
            yield return BuildAll(2);

            var area = Root.Q<VisualElement>("videoArea");
            yield return PokeAt(WorldPositionOf(area), 0.05f);

            Assert.AreEqual(1, _player.Taps.Count,
                $"タップが1回ではありません（{_player.Taps.Count} 回。札: {_panel.LastAction}）。");
            Assert.AreEqual(0.5f, _player.Taps[0].U, 0.06f);
            Assert.AreEqual(0.5f, _player.Taps[0].V, 0.06f);

            CollectionAssert.DoesNotContain(
                _player.Touches.Select(t => t.Phase).ToList(), VideoTouchPhase.Drag,
                "タップに DRAG が混ざりました（WebView がスクロールと見なしてクリックを出しません）。");

            StringAssert.Contains("タップ", _panel.LastAction, "札に「タップ」と出ていません。");
        }

        /// <summary>
        /// 窓を突いたまま横へ引くとドラッグになり、タップは送られない
        /// （関連動画の帯の横スクロールを殺さないこと）。
        /// </summary>
        [UnityTest]
        public IEnumerator 窓を横へ引くとドラッグになりタップは送らない()
        {
            yield return BuildAll(2);

            var area = Root.Q<VisualElement>("videoArea");

            // 絵の幅は 50cm なので、6cm 引けば閾値（3% = 1.5cm）を確実に越える。
            var offset = _panelGo.transform.right * 0.06f;
            yield return PokeAndDrag(WorldPositionOf(area), 0.05f, offset);

            Assert.IsEmpty(_player.Taps,
                $"横へ引いたのにタップを送りました（札: {_panel.LastAction}）。");

            var phases = _player.Touches.Select(t => t.Phase).ToList();
            CollectionAssert.Contains(phases, VideoTouchPhase.Down, "ドラッグの押し下げが届いていません。");
            CollectionAssert.Contains(phases, VideoTouchPhase.Drag, "ドラッグが届いていません。");
            Assert.AreEqual(VideoTouchPhase.Down, phases[0], "最初の触りが押し下げではありません。");
            Assert.AreEqual(VideoTouchPhase.Up, phases[phases.Count - 1],
                "離したのに押し上げが届いていません（WebView の中で指が押されたままになります）。");

            StringAssert.Contains("ドラッグ", _panel.LastAction, "札に「ドラッグ」と出ていません。");
        }

        // ---------------------------------------------------------------- 窓の見張り

        /// <summary>
        /// 窓が <c>youtube.com/watch</c> へ出たら、id を抜いて埋め込みプレイヤーへ連れ戻す。
        /// </summary>
        [UnityTest]
        public IEnumerator 関連動画へ遷移したら連れ戻して読み込む()
        {
            yield return BuildAll(2);

            _player.CurrentUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&feature=emb_rel_end";

            // 見張りは 0.5 秒ごと。読み直してから Load までさらに RelatedReloadDelayMs 待つ。
            var deadline = Time.unscaledTime + 1f + VideoPanel.RelatedReloadDelayMs / 1000f + 1f;
            while (Time.unscaledTime < deadline && _player.LoadedVideoIds.Count == 0)
            {
                yield return null;
            }

            Assert.AreEqual(1, _player.ReloadHtmlCount,
                "html を読み直していません（youtube.com の上では埋め込みの JS が無いので何も送れません）。");
            CollectionAssert.Contains(_player.LoadedVideoIds.ToList(), "dQw4w9WgXcQ",
                $"関連動画の id を読み込んでいません（札: {_panel.LastAction}）。");
            StringAssert.Contains("dQw4w9WgXcQ", _panel.LastAction, "札に関連動画の id が出ていません。");
        }

        /// <summary>
        /// 同梱の html の上に居る間は何もしない（毎 0.5 秒 読み直したら動画が止まってしまう）。
        /// </summary>
        [UnityTest]
        public IEnumerator 埋め込みの上に居る間は連れ戻さない()
        {
            yield return BuildAll(2);

            _player.CurrentUrl = "http://localhost/";

            var deadline = Time.unscaledTime + 1.2f;
            while (Time.unscaledTime < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(0, _player.ReloadHtmlCount, "埋め込みの上なのに読み直しました。");
            Assert.IsEmpty(_player.LoadedVideoIds, "埋め込みの上なのに読み込み直しました。");
        }

        /// <summary>
        /// html の読み込みが済んだら、ページ全体の縦スクロールを塞ぐ JS を送る。
        /// </summary>
        [UnityTest]
        public IEnumerator html読込後に縦スクロールを止める()
        {
            yield return BuildAll(2);

            var before = _player.SuppressPageScrollCount;
            _player.IsHtmlLoaded = true;

            var deadline = Time.unscaledTime + 1.2f;
            while (Time.unscaledTime < deadline && _player.SuppressPageScrollCount == before)
            {
                yield return null;
            }

            Assert.Greater(_player.SuppressPageScrollCount, before,
                "html を読み込んだのに縦スクロールを止めていません。");
        }

        /// <summary>向きを変えたら縦スクロールの止めを送り直す（WebView の resize で style が入れ替わる）。</summary>
        [UnityTest]
        public IEnumerator 向きを変えると縦スクロールの止めを送り直す()
        {
            yield return BuildAll(2);

            var before = _player.SuppressPageScrollCount;
            _panel.ToggleAspect();
            yield return null;

            Assert.Greater(_player.SuppressPageScrollCount, before,
                "向きを変えたのに縦スクロールの止めを送り直していません。");
        }

        /// <summary>
        /// 窓の外（操作部の札のあたり）を触っても WebView には何も渡らない。
        /// 渡すのは絵の中だけ——余白の座標を送ると WebView の端が押されてしまう。
        /// </summary>
        [UnityTest]
        public IEnumerator 窓の外を触ってもWebViewへは渡らない()
        {
            yield return BuildAll(2);

            var status = Root.Q<Label>("statusLine");
            Assert.IsNotNull(status);

            yield return PokeAt(WorldPositionOf(status), 0.05f);

            Assert.IsEmpty(_player.Touches, "窓の外を触ったのに WebView へ渡りました。");
        }

        /// <summary>
        /// 「戻る」で WebView の履歴が1つ戻る（関連動画の先から埋め込みプレイヤーへ帰る道）。
        /// </summary>
        [UnityTest]
        public IEnumerator 戻るを押すとWebViewの履歴を戻る()
        {
            yield return BuildAll(2);

            _panel.GoBack();

            Assert.AreEqual(1, _player.GoBackCount, "「戻る」が WebView に届いていません。");
            StringAssert.Contains("戻る", _panel.LastAction, "札に「戻る」と出ていません。");

            yield return null;
        }

        // ---------------------------------------------------------------- カメラの下見（v1-d）

        /// <summary>
        /// 「カメラ」の釦が一覧側に在って、指で突ける。
        /// 板は <see cref="KitchenXR.Platform.IPassthroughCamera"/> の口しか見ないので、
        /// ここでは受け皿（<see cref="NullPassthroughCamera"/>）を挿す——Editor の実機と同じ道筋。
        /// </summary>
        [UnityTest]
        public IEnumerator カメラの釦を押すと非対応の札が出る()
        {
            yield return BuildAll(2);

            var probe = new CameraProbe(new NullPassthroughCamera());
            _panel.BindCamera(probe);

            var button = Root.Q<Button>("cameraButton");
            Assert.IsNotNull(button, "「カメラ」の釦がありません。");

            var list = Root.Q<VisualElement>("listBlock");
            Assert.Greater(button.worldBound.xMin, list.worldBound.xMin - 0.01f,
                "「カメラ」の釦が一覧側にありません。");

            // 釦は4cm角以上（押す的の最小寸法）。
            var sizeCm = button.worldBound.height * WorldSpacePanelFactory.PanelLocalScale * 100f;
            Assert.GreaterOrEqual(sizeCm, 4f - 0.2f, $"「カメラ」の釦が {sizeCm:0.0}cm しかありません。");

            yield return PokeAt(WorldPositionOf(button), 0.05f);

            // 受け皿は権限も取れないので、押した瞬間に理由が札へ出る。
            var deadline = Time.unscaledTime + 2f;
            while (Time.unscaledTime < deadline && string.IsNullOrEmpty(_panel.CameraStatus))
            {
                yield return null;
            }

            StringAssert.Contains("カメラ", _panel.CameraStatus, "札にカメラの理由が出ていません。");
            StringAssert.Contains("非対応", _panel.CameraStatus,
                $"Editor では「非対応」が出るはずです（実際の札: {_panel.CameraStatus}）。");

            // 絵は出ない。窓は畳まれたまま。
            Assert.IsNull(probe.Texture, "Editor なのに絵が出ました。");
            Assert.IsTrue(Root.Q<VisualElement>("cameraWindow").ClassListContains("is-hidden"),
                "絵が無いのに小さな窓が畳まれていません。");

            probe.Dispose();
        }

        /// <summary>
        /// 向きを変えても板の下辺の中央は動かない（上のレシピ／タイマーへ食い込まない）。
        /// </summary>
        [UnityTest]
        public IEnumerator 向きを変えても下辺の位置は動かない()
        {
            yield return BuildAll(2);

            var bottomCenter = new Vector3(0.5f, 1.6f, 1.0f);
            _panel.PlaceAtBottomCenter(bottomCenter, Quaternion.identity);
            yield return null;

            var doc = _panelGo.GetComponent<UIDocument>();

            Vector3 BottomCenterOf()
            {
                var size = doc.worldSpaceSize;
                var w = size.x / WorldSpacePanelFactory.PanelPixelsPerUnit * WorldSpacePanelFactory.PanelLocalScale;
                var h = size.y / WorldSpacePanelFactory.PanelPixelsPerUnit * WorldSpacePanelFactory.PanelLocalScale;
                return _panelGo.transform.position + _panelGo.transform.right * (w / 2f)
                       - _panelGo.transform.up * h;
            }

            Assert.Less(Vector3.Distance(bottomCenter, BottomCenterOf()), 0.001f);

            _panel.ToggleAspect();
            yield return null;

            Assert.Less(Vector3.Distance(bottomCenter, BottomCenterOf()), 0.001f,
                "向きを変えたら板の下辺が動きました（下のタイマーへ食い込みます）。");
        }
    }
}
#endif
