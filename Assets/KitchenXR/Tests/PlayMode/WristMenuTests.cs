#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
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
    /// 手首の釦とメニュー、そしてレイの線の出し入れの検算
    /// （2026-09-13 主人の実機確認 v1.0.8 の③と④。設計 §4.4・§7・§11 追補）。
    ///
    /// 見るのは4つ:
    ///   1. **手首の釦をポークするとメニューが出入りする**（トグル）
    ///   2. **配置を終えるとメニューが閉じる**（<c>Bootstrap</c> と同じ結び方で確かめる）
    ///   3. **手首の釦はレイの相手にならない**（誤って遠くから押されない）
    ///   4. **レイの線は相手にホバーしている間だけ出る**（主人「操作できない場合は表示を消して」）
    ///
    /// 板の組み立ては Kitchen.unity と同じ <see cref="WorldSpacePanelFactory"/> を通す。
    /// </summary>
    public class WristMenuTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string WristToggleUxmlPath = "Assets/KitchenXR/Presentation/UI/WristToggle.uxml";
        private const string PlacementMenuUxmlPath = "Assets/KitchenXR/Presentation/UI/PlacementMenu.uxml";

        private const float WristToggleUnits = 22f;
        private const float MenuWidthUnits = 130f;
        private const float MenuHeightUnits = 92f;

        private const float StartDepthMeters = -0.06f;

        private GameObject _managerGo;
        private GameObject _cameraGo;
        private GameObject _eventSystemGo;
        private GameObject _rigGo;
        private GameObject _pokeGo;

        private GameObject _wristRootGo;
        private GameObject _palmGo;
        private GameObject _toggleGo;
        private GameObject _menuGo;

        private WristMenu _wristMenu;
        private WristMenuButton _toggleButton;
        private PlacementMenuPanel _menuPanel;
        private XRPokeInteractor _poke;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[]
                     {
                         _managerGo, _cameraGo, _eventSystemGo, _rigGo, _pokeGo,
                         _wristRootGo, _palmGo,
                     })
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }

            _managerGo = _cameraGo = _eventSystemGo = _rigGo = _pokeGo = null;
            _wristRootGo = _palmGo = _toggleGo = _menuGo = null;
            _wristMenu = null;
            _toggleButton = null;
            _menuPanel = null;
            _poke = null;

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

            yield return null;
        }

        /// <summary>試験のための細工（PokeButtonInteractionTests と同じ。実機では要らない分岐）。</summary>
        private static void ForceEditorInputRedirect()
        {
            var type = typeof(PanelSettings).Assembly.GetType("UnityEngine.UIElements.DefaultEventSystem");
            var field = type?.GetField("IsEditorRemoteConnected",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "DefaultEventSystem.IsEditorRemoteConnected が見つかりません。");

            Func<bool> alwaysTrue = () => true;
            field.SetValue(null, alwaysTrue);
        }

        /// <summary>
        /// Kitchen.unity の <c>Wrist Menu</c> と同じ組み立て。
        ///
        /// 手のひらの身代わり（<c>Palm</c>）は原点に置き、**手のひらの面をカメラ（-Z）へ向ける**
        /// ——実機で主人が手を返してメニューを見る姿勢がこれ。
        /// 手のひらの向く先は <c>-palm.up</c> なので、x を +90° 回すと
        /// <c>palm.up = (0,0,1)</c>・<c>-palm.up = (0,0,-1)</c> でカメラ側を向き、
        /// <c>palm.forward = (0,-1,0)</c>（指先は下。腕が上から来ている姿勢）になる。
        /// </summary>
        private IEnumerator BuildWristMenu()
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var toggleUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WristToggleUxmlPath);
            var menuUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PlacementMenuUxmlPath);
            Assert.IsNotNull(panelSettings, "KitchenPanelSettings が見つかりません。");
            Assert.IsNotNull(toggleUxml, "WristToggle.uxml が見つかりません。");
            Assert.IsNotNull(menuUxml, "PlacementMenu.uxml が見つかりません。");

            _palmGo = new GameObject("Palm");
            _palmGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));

            _wristRootGo = new GameObject("Wrist Menu");
            _wristMenu = _wristRootGo.AddComponent<WristMenu>();

            _menuGo = new GameObject("PlacementMenu");
            _menuGo.SetActive(false);
            _menuGo.transform.SetParent(_wristRootGo.transform, false);
            WorldSpacePanelFactory.Configure(
                _menuGo, panelSettings, menuUxml, MenuWidthUnits, MenuHeightUnits);
            _menuPanel = _menuGo.AddComponent<PlacementMenuPanel>();

            _toggleGo = new GameObject("WristToggle (Right)");
            _toggleGo.SetActive(false);
            _toggleGo.transform.SetParent(_wristRootGo.transform, false);
            WorldSpacePanelFactory.Configure(
                _toggleGo, panelSettings, toggleUxml, WristToggleUnits, WristToggleUnits);
            _toggleGo.layer = CookingModeInputGate.OffRayPhysicsLayer;
            _toggleButton = _toggleGo.AddComponent<WristMenuButton>();
            _toggleGo.SetActive(true);

            _wristMenu.Bind(null, null, _palmGo.transform, _toggleButton, _menuGo, _cameraGo.transform);

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
            _pokeGo.transform.position = new Vector3(0f, 0f, -5f);
            _pokeGo.SetActive(true);

            yield return null;
        }

        private IEnumerator BuildAll()
        {
            yield return BuildRig();
            yield return BuildWristMenu();
            yield return BuildPokeInteractor();
        }

        /// <summary>板の中の要素の場所を世界座標で。</summary>
        private static Vector3 WorldPositionOf(GameObject panel, VisualElement element)
        {
            var rect = element.worldBound;
            Assert.Greater(rect.width, 0f, "UI 要素の幅が 0 です（レイアウトが未確定）。");
            return panel.transform.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f));
        }

        private IEnumerator PokeAt(GameObject panel, Vector3 worldTarget, float depthMeters)
        {
            var forward = panel.transform.forward;

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

        private IEnumerator PokeWristToggle()
        {
            var root = _toggleGo.GetComponent<UIDocument>().rootVisualElement;
            var button = root.Q<Button>(WristMenuButton.ButtonName);
            Assert.IsNotNull(button, $"WristToggle.uxml に {WristMenuButton.ButtonName} がありません。");

            yield return PokeAt(_toggleGo, WorldPositionOf(_toggleGo, button), 0.05f);
        }

        // ---------------------------------------------------------------- 本題

        /// <summary>
        /// 起動直後は**何も出ていない**（釦の板だけが手首に付いている）。
        /// 主人「手を洗ってるときなどに出ると邪魔」——手の向きで出る仕掛けをやめた核心。
        /// </summary>
        [UnityTest]
        public IEnumerator 起動直後はメニューが出ていない()
        {
            yield return BuildAll();

            Assert.IsFalse(_wristMenu.IsMenuOpen, "起動直後にメニューが出ています。");
            Assert.IsFalse(_menuGo.activeSelf, "起動直後にメニューの板が立っています。");
            Assert.IsTrue(_toggleGo.activeSelf, "手首の釦が出ていません（これは常に付いている板です）。");
        }

        [UnityTest]
        public IEnumerator 手首の釦をポークするとメニューがトグルで出入りする()
        {
            yield return BuildAll();

            yield return PokeWristToggle();
            Assert.IsTrue(_wristMenu.IsMenuOpen, "手首の釦を押してもメニューが出ませんでした。");
            Assert.IsTrue(_menuGo.activeSelf, "メニューの板が立っていません。");

            // 連打の抑止（ClickDebounce の 600ms。設計 §7）を跨ぐ。
            // 試験のフレームは実時間より速く進むので、ここだけは実時間で待つ。
            yield return new WaitForSecondsRealtime(0.7f);

            yield return PokeWristToggle();
            Assert.IsFalse(_wristMenu.IsMenuOpen, "もう一度押してもメニューが閉じませんでした。");
            Assert.IsFalse(_menuGo.activeSelf, "メニューの板が残っています。");
        }

        /// <summary>
        /// 出したメニューの釦が**押せる**こと。
        /// <see cref="UIDocument"/> は無効化のたびに <c>rootVisualElement</c> を作り直すので、
        /// <c>SetActive</c> で出し入れする板は**出るたびに配線し直さないと死ぬ**
        /// （<see cref="PlacementMenuPanel"/> が <c>OnEnable</c> で配線し直している理由）。
        /// ここはその罠を踏んでいないことの検算でもある。
        /// </summary>
        [UnityTest]
        public IEnumerator 配置を終えるとメニューが閉じる()
        {
            yield return BuildAll();

            // Bootstrap と同じ結び方——「保存」を押したら配置が終わり、メニューは閉じる。
            var saved = 0;
            _menuPanel.SaveRequested += () =>
            {
                saved++;
                _menuPanel.SetPlacing(false);
                _wristMenu.Close();
            };

            yield return PokeWristToggle();
            Assert.IsTrue(_wristMenu.IsMenuOpen);

            _menuPanel.SetPlacing(true);
            yield return null;

            var root = _menuGo.GetComponent<UIDocument>().rootVisualElement;
            var saveButton = root.Q<Button>("saveButton");
            Assert.IsNotNull(saveButton, "PlacementMenu.uxml に saveButton がありません。");

            yield return PokeAt(_menuGo, WorldPositionOf(_menuGo, saveButton), 0.05f);

            Assert.AreEqual(1, saved, "出し直したメニューの「保存」が押せませんでした（配線が死んでいます）。");
            Assert.IsFalse(_wristMenu.IsMenuOpen, "保存したのにメニューが閉じていません。");
            Assert.IsFalse(_menuGo.activeSelf, "保存したのにメニューの板が残っています。");
        }

        /// <summary>
        /// 手首の釦は**レイの相手にならない**（誤って遠くから押されない）。
        /// 物理層 8 番 "Kitchen Panel Off Ray" は Ray の <c>raycastMask</c>（0/5/31）から外れていて、
        /// <c>Physics.DefaultRaycastLayers</c> には入ったままなので**指では押せる**
        /// ——上の「ポークで出入りする」試験がそれを示している。
        /// </summary>
        [UnityTest]
        public IEnumerator 手首の釦はレイの相手にならない()
        {
            yield return BuildAll();

            Assert.AreEqual(CookingModeInputGate.OffRayPhysicsLayer, _toggleGo.layer,
                "手首の釦がレイの届く層に載っています（遠くから誤って押せてしまいます）。");
            Assert.IsFalse(CookingModeInputGate.IsRayReachable(_toggleGo));
        }

        /// <summary>
        /// 手を見失ったら釦もメニューも引っ込める（宙に取り残さない）。
        /// <c>XRInputModalityManager</c> は手を追えていないとき <c>Left/Right Hand</c> ごと眠らせる。
        /// </summary>
        [UnityTest]
        public IEnumerator 手を見失うと釦もメニューも引っ込む()
        {
            yield return BuildAll();

            yield return PokeWristToggle();
            Assert.IsTrue(_wristMenu.IsMenuOpen);

            _palmGo.SetActive(false);
            yield return null;
            yield return null;

            Assert.IsFalse(_toggleGo.activeSelf, "手を見失ったのに手首の釦が浮いたままです。");
            Assert.IsFalse(_wristMenu.IsMenuOpen, "手を見失ったのにメニューが浮いたままです。");
        }

        // ---------------------------------------------------------------- ④ レイの線

        /// <summary>
        /// レイの線は**指す先があるときだけ**出る（主人「レイが操作できない場合は表示を消して」）。
        ///
        /// 相手（<see cref="XRSimpleInteractable"/>）を向いていない間は線を落とし、
        /// 向けた瞬間に戻す。ここは XRI の層（<c>interactionLayers</c>）を通った後の話なので、
        /// 調理モードでは動画の板だけ・配置モードでは全部の板が「相手」になる。
        /// </summary>
        [UnityTest]
        public IEnumerator レイの線は相手にホバーしている間だけ出る()
        {
            _managerGo = new GameObject("XR Interaction Manager", typeof(XRInteractionManager));

            // 相手の板（原点。厚み 0.1m の箱）。
            var target = new GameObject("Target");
            var collider = target.AddComponent<BoxCollider>();
            collider.size = new Vector3(1f, 1f, 0.1f);
            var interactable = target.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(collider);

            // レイ（z = -1 から +Z を向く）。
            // 線は **子の `LineVisual`** に置く——XRI の Near-Far Interactor がまさにこの形で、
            // 同じ GameObject だけを見る作りだと黙って線を消せない（2026-09-13 に踏んだ）。
            // ここでは挿し込みをせず、<see cref="RayLineVisibility"/> 自身に拾わせて確かめる。
            var rayGo = new GameObject("Ray Interactor");
            rayGo.SetActive(false);
            rayGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -1f), Quaternion.identity);

            var lineVisualGo = new GameObject("LineVisual");
            lineVisualGo.transform.SetParent(rayGo.transform, false);
            var line = lineVisualGo.AddComponent<LineRenderer>();

            var ray = rayGo.AddComponent<XRRayInteractor>();
            var visibility = rayGo.AddComponent<RayLineVisibility>();
            rayGo.SetActive(true);

            try
            {
                // まずは相手を向いていない（真上を向ける）。
                rayGo.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                for (var i = 0; i < 5; i++)
                {
                    yield return null;
                }

                Assert.IsFalse(ray.hasHover, "何も無い方を向いているのにホバーしています。");
                Assert.IsFalse(visibility.IsLineShown, "指す先が無いのにレイの線が出ています。");
                Assert.IsFalse(line.enabled, "指す先が無いのに LineRenderer が生きています。");

                // 相手を向ける。
                rayGo.transform.rotation = Quaternion.identity;
                for (var i = 0; i < 5; i++)
                {
                    yield return null;
                }

                Assert.IsTrue(ray.hasHover, "板を向いているのにホバーしていません（試験の組み立ての問題）。");
                Assert.IsTrue(visibility.IsLineShown, "相手を指しているのにレイの線が出ません。");
                Assert.IsTrue(line.enabled, "相手を指しているのに LineRenderer が落ちています。");

                // また外す。
                rayGo.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                for (var i = 0; i < 5; i++)
                {
                    yield return null;
                }

                Assert.IsFalse(visibility.IsLineShown, "相手から外れたのにレイの線が残っています。");
            }
            finally
            {
                UnityEngine.Object.Destroy(rayGo);
                UnityEngine.Object.Destroy(target);
            }
        }
    }
}
#endif
