#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using KitchenXR.Platform.Null;
using KitchenXR.Presentation;
using KitchenXR.Presentation.Hazard;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Tests.PlayMode
{
    /// <summary>
    /// 火気の注意（v1-c）の検算。見るのは5つ:
    ///   1. 手元のメニューの「注意の板」の頁にプリセットが縦に並び、板の高さが頁で変わる
    ///   2. プリセットを選ぶと板が1枚でき、<see cref="PanelPlacement"/> に鍵で登録される
    ///      （＝その場で掴めて、保存で位置が覚えられる）
    ///   3. 調理中の注意の板は触れない（コライダーが降り、レイの層から外れる）
    ///   4. 「消す」の2度押しで板と鍵が消える
    ///   5. 領域は始点と終点から矩形になり、閾値で床の線の段が切り替わる
    ///
    /// 板の組み立てはシーンと同じ <see cref="WorldSpacePanelFactory"/> を通す。
    /// </summary>
    public class HazardTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string HazardUxmlPath = "Assets/KitchenXR/Presentation/UI/HazardPanel.uxml";
        private const string DeleteUxmlPath = "Assets/KitchenXR/Presentation/UI/HazardDelete.uxml";
        private const string MenuUxmlPath = "Assets/KitchenXR/Presentation/UI/PlacementMenu.uxml";

        private GameObject _managerGo;
        private GameObject _cameraGo;
        private GameObject _eventSystemGo;
        private GameObject _originGo;
        private GameObject _placementGo;
        private GameObject _hazardGo;
        private GameObject _menuGo;
        private GameObject _pokeGo;
        private GameObject _rayGo;

        private PanelPlacement _placement;
        private CookingModeInputGate _gate;
        private DefaultHandInputPolicy _policy;
        private KitchenXR.Platform.PanelPoseFile _poseFile;

        private HazardBoards _boards;
        private HazardZones _zones;
        private HazardZoneDrawing _drawing;
        private HazardProximity _proximity;
        private HazardSound _sound;
        private XRPokeInteractor _poke;

        private HazardPanelFile _panelFile;
        private HazardZoneFile _zoneFile;
        private HazardPresetCatalog _catalog;

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "KitchenXR-Hazard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[]
                     {
                         _menuGo, _hazardGo, _placementGo, _pokeGo, _rayGo, _originGo, _cameraGo,
                         _eventSystemGo, _managerGo,
                     })
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }

            _menuGo = _hazardGo = _placementGo = _pokeGo = _rayGo = _originGo = _cameraGo = null;
            _eventSystemGo = _managerGo = null;
            _placement = null;
            _gate = null;
            _policy = null;
            _poseFile = null;
            _boards = null;
            _zones = null;
            _drawing = null;
            _proximity = null;
            _sound = null;
            _poke = null;
            _panelFile = null;
            _zoneFile = null;
            _catalog = null;

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

        /// <summary>Bootstrap と同じ組み立て（アンカーは InMemory・控えは一時の場所）。</summary>
        private IEnumerator BuildAll()
        {
            _managerGo = new GameObject("XR Interaction Manager", typeof(XRInteractionManager));

            _cameraGo = new GameObject("Main Camera", typeof(Camera));
            _cameraGo.tag = "MainCamera";
            _cameraGo.transform.SetPositionAndRotation(
                new Vector3(0f, 1.5f, 0f), Quaternion.identity);

            // EventSystem を自分で置くのが肝心。置かないと UI Toolkit が板を立てた時点で
            // 自前の EventSystem を作り、それが後の試験まで残って「2 つある」という警告が
            // 毎フレーム出る（＝以降の試験のフレームが数倍遅くなり、2度押しの猶予が切れる）。
            _eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));

            // 控えの基準（Kitchen.unity では XR Origin）。原点＝床の高さ 0。
            _originGo = new GameObject("XR Origin (test)");

            _placementGo = new GameObject("Kitchen Panels");
            _gate = _placementGo.AddComponent<CookingModeInputGate>();
            _placement = _placementGo.AddComponent<PanelPlacement>();
            _placement.RevealSeconds = 0f;

            _policy = new DefaultHandInputPolicy();
            _gate.Bind(_policy);

            _poseFile = new KitchenXR.Platform.PanelPoseFile(
                Path.Combine(_directory, KitchenXR.Platform.PanelPoseFile.FileName));
            _placement.Bind(new InMemoryAnchorStore(), _poseFile, _policy, _gate, _originGo.transform);

            _pokeGo = new GameObject("Poke Interactor");
            _pokeGo.SetActive(false);
            _poke = _pokeGo.AddComponent<XRPokeInteractor>();
            _poke.requirePokeFilter = false;
            _pokeGo.transform.position = new Vector3(0f, 1.5f, 0f);
            _pokeGo.SetActive(true);

            _panelFile = new HazardPanelFile(Path.Combine(_directory, HazardPanelFile.FileName));
            _zoneFile = new HazardZoneFile(Path.Combine(_directory, HazardZoneFile.FileName));
            _catalog = HazardPresetCatalog.LoadFromResources();
            Assert.Greater(_catalog.Count, 0, "プリセットが読めていません。");

            _hazardGo = new GameObject("Kitchen Hazards");
            _boards = _hazardGo.AddComponent<HazardBoards>();
            _drawing = _hazardGo.AddComponent<HazardZoneDrawing>();
            _zones = _hazardGo.AddComponent<HazardZones>();
            _sound = _hazardGo.AddComponent<HazardSound>();
            _proximity = _hazardGo.AddComponent<HazardProximity>();

            _boards.Bind(
                _placement, _panelFile, _catalog, _cameraGo.transform,
                LoadPanelSettings(), LoadUxml(HazardUxmlPath), LoadUxml(DeleteUxmlPath));

            _drawing.Bind(Array.Empty<XRBaseInputInteractor>(), _cameraGo.transform);
            _zones.Bind(_zoneFile, new InMemoryAnchorStore(), _originGo.transform, _drawing);
            _proximity.Bind(_zones, _sound, new[] { _poke }, _cameraGo.transform, _policy);

            yield return null;
        }

        private static PanelSettings LoadPanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(settings, "KitchenPanelSettings が見つかりません。");
            return settings;
        }

        private static VisualTreeAsset LoadUxml(string path)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.IsNotNull(uxml, $"{path} が見つかりません。");
            return uxml;
        }

        // ---------------------------------------------------------------- 1. 手元のメニュー

        [UnityTest]
        public IEnumerator 手元のメニューの注意の板の頁にプリセットが縦に並ぶ()
        {
            yield return BuildAll();

            _menuGo = new GameObject("PlacementMenu");
            _menuGo.SetActive(false);
            WorldSpacePanelFactory.Configure(
                _menuGo, LoadPanelSettings(), LoadUxml(MenuUxmlPath),
                PlacementMenuPanel.WidthUnits, PlacementMenuPanel.IdleHeightUnits);
            var menu = _menuGo.AddComponent<PlacementMenuPanel>();
            _menuGo.SetActive(true);

            yield return null;
            yield return null;

            menu.SetPresets(_catalog.Presets);
            menu.SetPlacing(true);

            yield return null;

            var document = _menuGo.GetComponent<UIDocument>();
            Assert.AreEqual(PlacementMenuPanel.Page.Placing, menu.CurrentPage);
            Assert.AreEqual(PlacementMenuPanel.PlacingHeightUnits, document.worldSpaceSize.y, 0.01f,
                "配置の操作の頁で板の高さが合っていません（3行×2 が入りません）。");

            var root = document.rootVisualElement;
            Assert.IsNotNull(root.Q<Button>("hazardMenuButton"), "「注意の板」の入口がありません。");
            Assert.IsNotNull(root.Q<Button>("zoneMenuButton"), "「コンロの領域」の入口がありません。");

            // プリセットの一覧の頁へ。
            menu.SetPage(PlacementMenuPanel.Page.Hazard);
            yield return null;

            Assert.AreEqual(PlacementMenuPanel.HazardHeightUnits, document.worldSpaceSize.y, 0.01f,
                "プリセットの頁で板が高くなっていません（縦に5行入りません）。");

            var list = root.Q<VisualElement>(PlacementMenuPanel.HazardListName);
            Assert.IsNotNull(list, "プリセットを並べる場所がありません。");
            Assert.AreEqual(_catalog.Count, list.childCount,
                "プリセットの数だけ釦が縦に並ぶはずです。");

            foreach (var preset in _catalog.Presets)
            {
                var button = root.Q<Button>("hazardPreset_" + preset.Id);
                Assert.IsNotNull(button, $"{preset.Id} の釦がありません。");
                Assert.AreEqual(preset.Title, button.text);

                // 押す釦の下限（20px = 4cm 角）を満たしていること。
                Assert.GreaterOrEqual(button.resolvedStyle.minHeight.value, 20f,
                    $"{preset.Id} の釦が 4cm 角より低いです。");
            }

            // 「戻る」で配置の操作へ帰れる（行き止まりを作らない）。
            menu.SetPage(PlacementMenuPanel.Page.Placing);
            yield return null;
            Assert.AreEqual(PlacementMenuPanel.PlacingHeightUnits, document.worldSpaceSize.y, 0.01f);

            // 領域の頁も同じく。
            menu.SetPage(PlacementMenuPanel.Page.Zone);
            yield return null;
            Assert.AreEqual(PlacementMenuPanel.ZoneHeightUnits, document.worldSpaceSize.y, 0.01f);
            Assert.IsNotNull(root.Q<Button>("zoneDrawButton"), "「囲む」がありません。");
            Assert.IsNotNull(root.Q<Button>("zoneRaiseButton"), "上面の高さ ±5cm がありません。");

            // 調理モードへ戻ると「配置」1つの高さに戻る。
            menu.SetPlacing(false);
            yield return null;
            Assert.AreEqual(PlacementMenuPanel.Page.Idle, menu.CurrentPage);
            Assert.AreEqual(PlacementMenuPanel.IdleHeightUnits, document.worldSpaceSize.y, 0.01f);
        }

        // ---------------------------------------------------------------- 2. 板を作る

        [UnityTest]
        public IEnumerator プリセットを選ぶと板ができてPanelPlacementに登録される()
        {
            yield return BuildAll();

            _boards.SetPlacing(true);
            _placement.Enter();

            var panel = _boards.Add("fire");
            yield return null;
            yield return null;

            Assert.IsNotNull(panel, "注意の板ができていません。");
            Assert.AreEqual(1, _boards.Panels.Count);
            Assert.AreEqual("fire", panel.PresetId);
            StringAssert.StartsWith(HazardBoards.KeyPrefix, panel.Key, "鍵の頭が panel.hazard. ではありません。");

            // 頭の前 50cm（他の板より手前）に出ていること。
            var distance = Vector3.Distance(
                panel.transform.position, _cameraGo.transform.position);
            Assert.Less(distance, 1.0f, "頭の前 50cm あたりに出るはずです。");

            // 配置モードの最中に足しても、その場で掴めること（枠と掴む仕掛け）。
            Assert.IsTrue(PanelPlacement.HasFrame(panel.gameObject),
                "配置モード中に作った板に枠が出ていません（掴んで置けません）。");
            var grab = panel.GetComponent<XRGrabInteractable>();
            Assert.IsNotNull(grab, "掴む仕掛けが足されていません。");
            Assert.IsTrue(grab.enabled, "作った直後に掴めません。");

            // 種類は即座に控えへ（位置は「保存」で板の仕組みが覚える）。
            var read = new HazardPanelFile(Path.Combine(_directory, HazardPanelFile.FileName)).Load();
            Assert.AreEqual(1, read.Count, "hazards.json に残っていません。");
            Assert.AreEqual(panel.Key, read[0].Key);
            Assert.AreEqual("fire", read[0].PresetId);

            // 「保存」で位置が控え（panels.json）へ。
            var moved = new Vector3(0.4f, 1.2f, 1.4f);
            panel.transform.position = moved;

            var saving = _placement.SaveAsync();
            while (saving.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending)
            {
                yield return null;
            }

            var poses = new KitchenXR.Platform.PanelPoseFile(
                Path.Combine(_directory, KitchenXR.Platform.PanelPoseFile.FileName));
            poses.Load();
            Assert.IsTrue(poses.TryGet(panel.Key, out var stored),
                "注意の板の位置が控えに入っていません。");
            Assert.AreEqual(moved.x, stored.position.x, 1e-3f);
        }

        [UnityTest]
        public IEnumerator 知らないプリセットでは板を作らない()
        {
            yield return BuildAll();

            Assert.IsNull(_boards.Add("しらない"), "知らない種類で板ができてしまいました。");
            Assert.IsEmpty(_boards.Panels);
        }

        // ---------------------------------------------------------------- 3. 調理中は触れない

        [UnityTest]
        public IEnumerator 調理中の注意の板は触れずレイの層にも載らない()
        {
            yield return BuildAll();

            var panel = _boards.Add("hot");
            yield return null;

            _boards.SetPlacing(false);
            yield return null;

            var collider = panel.GetComponent<BoxCollider>();
            Assert.IsNotNull(collider);
            Assert.IsFalse(collider.enabled, "調理中の注意の板は指でも触れない（表示だけ）。");
            Assert.IsFalse(panel.GetComponent<XRSimpleInteractable>().enabled,
                "ポークの受け口が生きています。");
            Assert.IsFalse(CookingModeInputGate.IsRayReachable(panel.gameObject),
                "調理中の注意の板にレイが届きます。");

            // 配置モードへ入れば掴める（コライダーが戻る）。
            _boards.SetPlacing(true);
            _placement.Enter();
            yield return null;

            Assert.IsTrue(collider.enabled, "配置モードで掴めません（コライダーが降りたまま）。");
            Assert.IsTrue(CookingModeInputGate.IsRayReachable(panel.gameObject),
                "配置モードで注意の板にレイが届きません。");
        }

        // ---------------------------------------------------------------- 4. 消す

        [UnityTest]
        public IEnumerator 消すの2度押しで板と鍵が消える()
        {
            yield return BuildAll();

            _boards.SetPlacing(true);
            _placement.Enter();

            var panel = _boards.Add("blade");
            yield return null;
            yield return null;

            var key = panel.Key;
            var chip = panel.Chip;
            Assert.IsNotNull(chip, "「消す」の板が付いていません。");
            Assert.IsTrue(chip.gameObject.activeSelf, "配置モードなのに「消す」が出ていません。");

            // 板の子ではなく兄弟で、板の右横に付いて回ること
            // （子にすると UIDocument が自分の板を作らず親の中の要素になる）。
            Assert.AreNotSame(panel.transform, chip.transform.parent,
                "「消す」が注意の板の子になっています。");
            Assert.Greater(
                Vector3.Dot(chip.transform.position - panel.transform.position, panel.transform.right),
                0f, "「消す」が板の右横にありません。");

            // 1度目は身構えるだけ。
            chip.PressForTest();
            yield return null;
            Assert.IsTrue(chip.IsArmed, "1度目で消えてはいけません（2度押し）。");
            Assert.AreEqual(1, _boards.Panels.Count);

            chip.PressForTest();
            yield return null;
            yield return null;

            Assert.IsEmpty(_boards.Panels, "2度目で板が消えていません。");
            Assert.IsFalse(_placement.Unregister(key), "鍵が PanelPlacement に残っています。");
            Assert.IsEmpty(
                new HazardPanelFile(Path.Combine(_directory, HazardPanelFile.FileName)).Load(),
                "hazards.json に残っています。");
        }

        // ---------------------------------------------------------------- 5. 領域

        [UnityTest]
        public IEnumerator 領域は始点と終点から矩形になる()
        {
            yield return BuildAll();

            _zones.BeginDraw();
            Assert.IsTrue(_drawing.IsArmed, "「囲む」で待ちに入っていません。");

            // コンロの上面（0.9m）の高さでピンチして、水平に 60×50cm ドラッグ。
            _drawing.BeginAt(new Vector3(-0.3f, 0.9f, 1.0f));
            Assert.IsTrue(_drawing.IsDragging);
            Assert.AreEqual(0.9f, _drawing.PlaneY, 1e-4f, "仮の作業面が始点の高さに張られていません。");

            // 高さの違う点を渡しても作業面に貼り付く（＝水平にドラッグ）。
            _drawing.DragTo(new Vector3(0.3f, 1.4f, 1.5f));
            Assert.AreEqual(0.9f, _drawing.Current.y, 1e-4f);

            _drawing.Commit();
            yield return null;

            Assert.AreEqual(1, _zones.Count, "領域ができていません。");
            var zone = _zones.Last;
            Assert.AreEqual(0.6f, zone.SizeX, 1e-3f);
            Assert.AreEqual(0.5f, zone.SizeZ, 1e-3f);
            Assert.AreEqual(0.9f, zone.Center.y, 1e-3f);
            Assert.AreEqual(0.9f, zone.Height, 1e-3f, "床（XR Origin の y = 0）からの高さ。");

            // 描いた直後に控えへ入る（電源が落ちても残る）。
            Assert.AreEqual(1,
                new HazardZoneFile(Path.Combine(_directory, HazardZoneFile.FileName)).Load().Count,
                "zones.json に残っていません。");

            // ±5cm で上面だけ動く。
            Assert.IsTrue(_zones.AdjustHeight(1));
            Assert.AreEqual(0.95f, _zones.Last.Center.y, 1e-3f);
            Assert.AreEqual(0f, _zones.Last.FloorY, 1e-3f, "床の線は動かないはずです。");

            // 「消す」で無くなる。
            Assert.IsTrue(_zones.RemoveLast());
            Assert.AreEqual(0, _zones.Count);
        }

        [UnityTest]
        public IEnumerator 小さすぎる囲みは領域にしない()
        {
            yield return BuildAll();

            _zones.BeginDraw();
            _drawing.BeginAt(new Vector3(0f, 0.9f, 1f));
            _drawing.DragTo(new Vector3(0.04f, 0.9f, 1.04f));
            _drawing.Commit();

            yield return null;

            Assert.AreEqual(0, _zones.Count, "指の震えほどの囲みで領域ができました。");
        }

        /// <summary>
        /// 囲んでいる間はレイの掴みを止めること。配置モードのレイは全ての層に届くので、
        /// コンロを指したレイが途中の板を横切っていると、囲もうとしたピンチで板が飛ぶ。
        /// </summary>
        [UnityTest]
        public IEnumerator 囲んでいる間はレイで板を掴めない()
        {
            yield return BuildAll();

            _rayGo = new GameObject("Ray Interactor");
            _rayGo.SetActive(false);
            var ray = _rayGo.AddComponent<XRRayInteractor>();
            _rayGo.SetActive(true);

            _drawing.Bind(new XRBaseInputInteractor[] { ray }, _cameraGo.transform);

            yield return null;

            _zones.BeginDraw();
            Assert.AreEqual(0, (int)ray.interactionLayers,
                "囲んでいる間もレイが板を掴めます（ピンチで板が飛びます）。");

            _drawing.BeginAt(new Vector3(-0.3f, 0.9f, 1.0f));
            _drawing.DragTo(new Vector3(0.3f, 0.9f, 1.5f));
            _drawing.Commit();

            yield return null;

            Assert.AreEqual(CookingModeInputGate.PlacementRayInteractionLayers, (int)ray.interactionLayers,
                "囲み終わってもレイが戻っていません（板を掴み直せません）。");

            // 「戻る」でやめたときも戻ること。
            _zones.BeginDraw();
            Assert.AreEqual(0, (int)ray.interactionLayers);
            _zones.CancelDraw();
            Assert.AreEqual(CookingModeInputGate.PlacementRayInteractionLayers, (int)ray.interactionLayers,
                "描くのをやめてもレイが戻っていません。");
        }

        [UnityTest]
        public IEnumerator 閾値で床の線の段が切り替わる()
        {
            yield return BuildAll();

            _zones.BeginDraw();
            _drawing.BeginAt(new Vector3(-0.3f, 0.9f, 1.0f));
            _drawing.DragTo(new Vector3(0.3f, 0.9f, 1.5f));
            _drawing.Commit();
            yield return null;

            var visual = _zones.Entries[0].Visual;
            Assert.IsNotNull(visual);

            // 頭も手も遠い（調理モード）→ 何も出ない。
            _cameraGo.transform.position = new Vector3(0f, 1.5f, -3f);
            MoveHand(new Vector3(0f, 1.5f, -3f));
            _proximity.Evaluate();
            Assert.AreEqual(HazardAlertLevel.Off, visual.Level);
            Assert.IsFalse(visual.IsVisible, "離れているのに線が出ています。");

            // 手を 30cm 上へ（＝ 40cm 以内）→ 琥珀。
            MoveHand(new Vector3(0f, 1.2f, 1.25f));
            _proximity.Evaluate();
            Assert.AreEqual(HazardAlertLevel.Watch, visual.Level, "手 40cm で琥珀になりません。");
            Assert.IsTrue(visual.IsVisible);

            // 手を 15cm 上へ（＝ 20cm 以内）→ 赤 ＋ 音1つ。
            var soundBefore = _sound.PlayedCount;
            MoveHand(new Vector3(0f, 1.05f, 1.25f));
            _proximity.Evaluate();
            Assert.AreEqual(HazardAlertLevel.Near, visual.Level, "手 20cm で赤になりません。");
            Assert.AreEqual(soundBefore + 1, _sound.PlayedCount, "赤へ上がったときに音が鳴りません。");

            // 赤のまま近づいても鳴り続けない。
            MoveHand(new Vector3(0f, 1.0f, 1.25f));
            _proximity.Evaluate();
            Assert.AreEqual(soundBefore + 1, _sound.PlayedCount, "近づいている間ずっと鳴っています。");

            // 手を遠ざけ、頭だけ 50cm（＝ 60cm 以内）→ 琥珀。
            MoveHand(new Vector3(0f, 1.5f, -3f));
            _cameraGo.transform.position = new Vector3(0f, 1.4f, 0.8f);
            _proximity.Evaluate();
            Assert.AreEqual(HazardAlertLevel.Watch, visual.Level, "頭 60cm で琥珀になりません。");

            // 配置モードでは離れていても薄く出す（領域が見えないと置き直せない）。
            _cameraGo.transform.position = new Vector3(0f, 1.5f, -3f);
            _policy.SetMode(KitchenXR.Platform.HandInputMode.PlacementMode);
            _proximity.Evaluate();
            Assert.AreEqual(HazardAlertLevel.Off, visual.Level);
            Assert.IsTrue(visual.IsVisible, "配置モードで領域の線が出ていません。");
        }

        private void MoveHand(Vector3 position) => _pokeGo.transform.position = position;
    }
}
#endif
