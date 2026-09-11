#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KitchenXR.Domain;
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
    /// 動画の板（設計 §6・ROADMAP P4）の検算。
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

            // 設計 §6「文字入力はパネルに置かない」。
            Assert.IsEmpty(Root.Query<TextField>().ToList(), "動画の板に文字入力を置いてはいけません（設計 §6）。");
        }

        [UnityTest]
        public IEnumerator 一覧が題名の数だけ描かれる()
        {
            yield return BuildAll(5);

            var rows = Root.Query<VisualElement>(className: "video-row").ToList();
            Assert.AreEqual(5, rows.Count, "題名の行が一覧の件数と合いません。");
            Assert.AreEqual("動画 0", rows[0].Q<Label>().text);
        }

        [UnityTest]
        public IEnumerator 一覧が空なら断りを出す()
        {
            yield return BuildAll(0);

            Assert.IsEmpty(Root.Query<VisualElement>(className: "video-row").ToList());
            Assert.AreEqual(0, _player.LoadedVideoIds.Count);
        }

        /// <summary>
        /// 一覧を**指で突いて**選ぶと、その動画 id で <c>Load</c> が呼ばれる（設計 §6）。
        /// 実機では主人の <c>YoutubePlayer.Load</c> がこの先に居る。
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
        /// 16:9 ⇄ 9:16 で板の寸法が変わり、**当たり判定も一緒に**変わる
        /// （片方だけ変えると 2026-09-12 の「触っているのに押せない」が戻る）。
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

        /// <summary>
        /// 向きを変えても**板の下辺の中央**は動かない（上のレシピ／タイマーへ食い込まない）。
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
