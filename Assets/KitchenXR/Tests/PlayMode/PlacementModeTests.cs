#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using KitchenXR.Domain;
using KitchenXR.Platform;
using KitchenXR.Platform.Null;
using KitchenXR.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace KitchenXR.Tests.PlayMode
{
    /// <summary>
    /// 配置モードの検算（ROADMAP P2・設計 §4.4）。
    ///
    /// 見るのは3つ:
    ///   1. **配置モードでは調理の板が指で押せない**（指を突っ込んでも工程が進まない）。
    ///      同時に Ray は生きる（調理モードでは止めている）
    ///   2. **「保存」で調理モードへ戻り**、控え（panels.json）に全ての鍵が書かれる
    ///   3. **起動時の復元順**——アンカー → 控え → 既定
    ///
    /// 板の組み立ては Kitchen.unity と同じ <see cref="WorldSpacePanelFactory"/> を通す
    /// （シーンと試験で別の組み方をしない、という P1 からの約束）。
    /// </summary>
    public class PlacementModeTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string RecipeUxmlPath = "Assets/KitchenXR/Presentation/UI/RecipePanel.uxml";
        private const string RecipeResourcePath = "Recipes/chahan";

        private const float PanelWidthUnits = 260f;
        private const float PanelHeightUnits = 190f;

        private const float StartDepthMeters = -0.06f;

        private GameObject _panelGo;
        private GameObject _pokeGo;
        private GameObject _rigGo;
        private GameObject _cameraGo;
        private GameObject _eventSystemGo;
        private GameObject _managerGo;
        private GameObject _placementGo;
        private GameObject _rayGo;
        private GameObject _originGo;

        private RecipePanel _recipePanel;
        private CookSession _session;
        private XRPokeInteractor _poke;
        private PanelPlacement _placement;
        private CookingModeInputGate _gate;
        private DefaultHandInputPolicy _policy;
        private PanelPoseFile _poseFile;
        private InMemoryAnchorStore _anchors;
        private Behaviour _rayLike;

        private string _directory;
        private int _nextCount;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "KitchenXR-P2Play-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[]
                     {
                         _panelGo, _pokeGo, _rigGo, _cameraGo, _eventSystemGo, _managerGo,
                         _placementGo, _rayGo, _originGo,
                     })
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }

            _panelGo = _pokeGo = _rigGo = _cameraGo = _eventSystemGo = _managerGo = null;
            _placementGo = _rayGo = _originGo = null;
            _recipePanel = null;
            _session = null;
            _poke = null;
            _placement = null;
            _gate = null;
            _policy = null;
            _poseFile = null;
            _anchors = null;
            _rayLike = null;
            _nextCount = 0;

            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, true);
                }
            }
            catch (IOException)
            {
                // 掃除に失敗しても結果は変わらない。
            }

            yield return null;
        }

        // ---------------------------------------------------------------- 土台

        private IEnumerator BuildRig()
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

            // 控えの基準（Kitchen.unity では XR Origin）。原点に置いておく。
            _originGo = new GameObject("XR Origin (test)");

            yield return null;
        }

        /// <summary>
        /// 試験のための細工（<c>PokeButtonInteractionTests</c> と同じ。アプリ側の不具合ではない）。
        /// エディタでは Game View に focus が無いとワールド空間の入力が処理されない。
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

        private IEnumerator BuildPanel()
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RecipeUxmlPath);
            Assert.IsNotNull(panelSettings, "KitchenPanelSettings が見つかりません。");
            Assert.IsNotNull(uxml, "RecipePanel.uxml が見つかりません。");

            _panelGo = new GameObject("RecipePanel");
            _panelGo.SetActive(false);
            _panelGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            WorldSpacePanelFactory.Configure(_panelGo, panelSettings, uxml, PanelWidthUnits, PanelHeightUnits);
            _panelGo.SetActive(true);

            yield return null;

            _recipePanel = _panelGo.AddComponent<RecipePanel>();

            var text = Resources.Load<TextAsset>(RecipeResourcePath);
            Assert.IsNotNull(text, $"見本レシピが読めません: Resources/{RecipeResourcePath}.json");
            _session = new CookSession(RecipeJson.Parse(text.text));

            _recipePanel.NextRequested += () =>
            {
                _nextCount++;
                _session.Apply(SessionEvent.NextRequested.Instance);
                _recipePanel.Refresh(_session);
            };
            _recipePanel.Refresh(_session);

            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }
        }

        private IEnumerator BuildPokeInteractor()
        {
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

        /// <summary>Bootstrap と同じ組み立て（アンカーは InMemory・控えは一時の場所）。</summary>
        private IEnumerator BuildPlacement()
        {
            // 「調理モードで止める Ray っぽい Interactor」の身代わり。
            // 実物（NearFarInteractor）でなくてよい——ゲートが見るのは Behaviour.enabled だけ。
            _rayGo = new GameObject("Ray Interactor (stand-in)");
            _rayLike = _rayGo.AddComponent<Light>();

            _placementGo = new GameObject("Kitchen Panels");
            _gate = _placementGo.AddComponent<CookingModeInputGate>();
            _placement = _placementGo.AddComponent<PanelPlacement>();
            _placement.RevealSeconds = 0.1f;

            _policy = new DefaultHandInputPolicy();
            _gate.Bind(_policy);
            _gate.SetRayLikeInteractors(_rayLike);

            _anchors = new InMemoryAnchorStore();
            _poseFile = new PanelPoseFile(Path.Combine(_directory, PanelPoseFile.FileName));

            _placement.Bind(_anchors, _poseFile, _policy, _gate, _originGo.transform);
            _placement.Register(PanelPlacement.RecipeKey, _recipePanel);

            yield return null;
        }

        private IEnumerator BuildAll()
        {
            yield return BuildRig();
            yield return BuildPanel();
            yield return BuildPokeInteractor();
            yield return BuildPlacement();

            Assert.AreEqual(1, _session.Current, "初期状態は1工程目のはずです。");
        }

        // ---------------------------------------------------------------- 指を動かす

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

        private Button NextButton
        {
            get
            {
                var button = _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<Button>("nextButton");
                Assert.IsNotNull(button, "『次へ』(nextButton) が UXML にありません。");
                return button;
            }
        }

        // ---------------------------------------------------------------- 1. 入力の切り替え

        [UnityTest]
        public IEnumerator 調理モードではRayが止まりPokeが効く()
        {
            yield return BuildAll();

            Assert.AreEqual(HandInputMode.CookingMode, _policy.CurrentMode, "既定は調理モードのはずです。");
            Assert.IsFalse(_rayLike.enabled, "調理モードで Ray が生きています（設計 §4.4・§7）。");
            Assert.IsTrue(CookingModeInputGate.IsUiEnabled(_panelGo), "調理モードで板の UI が止まっています。");

            yield return PokeAt(WorldPositionOf(NextButton), 0.05f);

            Assert.AreEqual(1, _nextCount, "調理モードで『次へ』が効きません。");
        }

        [UnityTest]
        public IEnumerator 配置モードに入るとPokeが効かずRayが効く()
        {
            yield return BuildAll();

            var target = WorldPositionOf(NextButton);

            _placement.Enter();
            yield return null;

            Assert.IsTrue(_placement.IsPlacing);
            Assert.AreEqual(HandInputMode.PlacementMode, _policy.CurrentMode);
            Assert.IsTrue(_rayLike.enabled, "配置モードで Ray が止まったままです（掴めません）。");
            Assert.IsFalse(CookingModeInputGate.IsUiEnabled(_panelGo),
                "配置モードなのに調理の板の UI が生きています（誤って工程が進みます）。");

            yield return PokeAt(target, 0.05f);

            Assert.AreEqual(0, _nextCount, "配置モードで『次へ』が発火しました（設計 §4.4）。");
            Assert.AreEqual(1, _session.Current, "配置モードで工程が進みました。");
        }

        [UnityTest]
        public IEnumerator 配置モードでは板に枠が出て掴めるようになる()
        {
            yield return BuildAll();

            Assert.IsFalse(PanelPlacement.HasFrame(_panelGo), "調理モードで枠が出ています。");

            _placement.Enter();
            yield return null;

            Assert.IsTrue(PanelPlacement.HasFrame(_panelGo), "配置モードで枠が出ていません（設計 §4.4）。");

            var grab = _panelGo.GetComponent<XRGrabInteractable>();
            var simple = _panelGo.GetComponent<XRSimpleInteractable>();
            Assert.IsNotNull(grab, "掴む仕掛けが足されていません。");
            Assert.IsTrue(grab.enabled, "配置モードで掴めません。");
            Assert.IsFalse(simple.enabled,
                "ポークの受け口と掴む仕掛けが同時に有効です（同じコライダーを2つが名乗ると引き当てが壊れます）。");

            _placement.Cancel();
            yield return null;

            Assert.IsFalse(PanelPlacement.HasFrame(_panelGo), "配置モードを出ても枠が残っています。");
            Assert.IsFalse(grab.enabled);
            Assert.IsTrue(simple.enabled, "調理モードに戻ったのにポークの受け口が戻っていません。");
        }

        // ---------------------------------------------------------------- 2. 保存

        [UnityTest]
        public IEnumerator 保存すると調理モードへ戻り控えに書かれる()
        {
            yield return BuildAll();

            _placement.Enter();
            yield return null;

            // 主人が掴んで動かした、のかわりに板を直接動かす。
            var moved = new Pose(new Vector3(0.4f, 1.5f, 1.1f), Quaternion.Euler(0f, -20f, 0f));
            _panelGo.transform.SetPositionAndRotation(moved.position, moved.rotation);

            var saving = _placement.SaveAsync();
            while (saving.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending)
            {
                yield return null;
            }

            Assert.IsFalse(_placement.IsPlacing, "保存しても配置モードのままです。");
            Assert.AreEqual(HandInputMode.CookingMode, _policy.CurrentMode, "保存で調理モードへ戻りません（設計 §4.4）。");
            Assert.IsTrue(CookingModeInputGate.IsUiEnabled(_panelGo), "調理モードに戻ったのに板の UI が止まっています。");

            // 控えは**必ず**書く（アンカーが使えるかどうかに関わらず。設計 §4.3 の退避路）。
            var read = new PanelPoseFile(Path.Combine(_directory, PanelPoseFile.FileName));
            read.Load();
            Assert.IsTrue(read.TryGet(PanelPlacement.RecipeKey, out var stored), "控えに鍵がありません。");
            Assert.AreEqual(moved.position.x, stored.position.x, 1e-3f);
            Assert.AreEqual(moved.position.y, stored.position.y, 1e-3f);

            // アンカー側にも入っている（ここでは InMemory）。
            var fromAnchor = _anchors.LoadAsync(PanelPlacement.RecipeKey).GetAwaiter().GetResult();
            Assert.IsNotNull(fromAnchor, "アンカーへ保存されていません。");
        }

        [UnityTest]
        public IEnumerator 元に戻すと入る前の位置へ戻る()
        {
            yield return BuildAll();

            var before = _panelGo.transform.position;

            _placement.Enter();
            yield return null;

            _panelGo.transform.position = before + new Vector3(0.5f, 0.3f, 0f);
            _placement.Undo();

            Assert.AreEqual(before.x, _panelGo.transform.position.x, 1e-4f, "『元に戻す』で戻りません。");
            Assert.AreEqual(before.y, _panelGo.transform.position.y, 1e-4f);
            Assert.IsTrue(_placement.IsPlacing, "『元に戻す』で配置モードを出てしまいました。");
        }

        // ---------------------------------------------------------------- 3. 復元順

        [UnityTest]
        public IEnumerator 復元はアンカーを先に見る()
        {
            yield return BuildAll();

            var anchorPose = new Pose(new Vector3(1f, 1.4f, 0.5f), Quaternion.identity);
            _anchors.SaveAsync(PanelPlacement.RecipeKey, anchorPose).GetAwaiter().GetResult();

            // 控えには**別の**場所を入れておく。アンカーが勝つことを見るため。
            _poseFile.Set(PanelPlacement.RecipeKey, new Pose(new Vector3(-3f, 0f, 0f), Quaternion.identity));
            _poseFile.Save();
            _poseFile.Load();

            yield return RestoreAndSettle();

            Assert.AreEqual(PanelPlacement.SourceAnchor, _placement.RestoreSources[PanelPlacement.RecipeKey],
                "アンカーがあるのに控えから戻しています（復元順が違います）。");
            Assert.AreEqual(anchorPose.position.x, _panelGo.transform.position.x, 1e-3f);
        }

        [UnityTest]
        public IEnumerator アンカーが無ければ控えから戻す()
        {
            yield return BuildAll();

            // 控えは **XR Origin 基準の相対**。基準を動かして、相対で戻ることも一緒に見る。
            _originGo.transform.position = new Vector3(10f, 0f, 0f);

            _poseFile.Set(PanelPlacement.RecipeKey, new Pose(new Vector3(0.2f, 1.3f, 1.2f), Quaternion.identity));
            _poseFile.Save();
            _poseFile.Load();

            yield return RestoreAndSettle();

            Assert.AreEqual(PanelPlacement.SourceFile, _placement.RestoreSources[PanelPlacement.RecipeKey],
                "控えから戻していません。");
            Assert.AreEqual(10.2f, _panelGo.transform.position.x, 1e-2f,
                "控えが XR Origin 基準の相対で戻っていません（設計 §4.3 の退避路）。");
        }

        [UnityTest]
        public IEnumerator どちらも無ければ既定のまま()
        {
            yield return BuildAll();

            var before = _panelGo.transform.position;

            yield return RestoreAndSettle();

            Assert.AreEqual(PanelPlacement.SourceDefault, _placement.RestoreSources[PanelPlacement.RecipeKey],
                "覚えていないのに何かから戻しています。");
            Assert.AreEqual(before, _panelGo.transform.position, "既定の位置が動きました（Bootstrap の配置のままのはず）。");
        }

        /// <summary>復元を回して、ゆっくり出す補間が終わるまで待つ。</summary>
        private IEnumerator RestoreAndSettle()
        {
            var restoring = _placement.RestoreAsync();
            while (restoring.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending)
            {
                yield return null;
            }

            var until = Time.unscaledTime + _placement.RevealSeconds + 0.3f;
            while (Time.unscaledTime < until)
            {
                yield return null;
            }
        }
    }
}
#endif
