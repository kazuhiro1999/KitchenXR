using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.UI;
using KitchenXR.Presentation;
using KitchenXR.Presentation.Hazard;
using KitchenXR.Presentation.Video;

namespace KitchenXR.App.Editor
{
    /// <summary>
    /// Kitchen.unity を組み立てるバッチ用ツール（-executeMethod から叩く。何度でも作り直せる）。
    /// MR テンプレートの SampleScene を複製し、チュートリアル UI・サンプル専用オブジェクト・
    /// テンプレートの仮想環境（Environment）を外して、レシピ／材料／タイマーの3枚の
    /// ワールド空間 UI Toolkit パネルと Bootstrap を足す。
    ///
    /// 実機で踏んだ落とし穴を3つ、ここで塞いでいる（詳細は各所のコメント）:
    ///   (1) 日本語フォントは PanelSettings 側にも据える（USS の指定が外れても字が出るように）
    ///   (2) `Environment` ルートを消さないとパススルーが見えない
    ///   (3) XRI 3.5 のワールド空間 UI Toolkit には受け口
    ///       （XRUIToolkitManager・PanelInputConfiguration・bypassUIToolkitEvents=false）が要り、
    ///       パネルのコライダーは UI px ではなく実寸で・isTrigger を立てずに置く
    /// </summary>
    public static class KitchenSceneBuilder
    {
        private const string SourceScenePath = "Assets/Scenes/SampleScene.unity";
        public const string KitchenScenePath = "Assets/KitchenXR/Scenes/Kitchen.unity";
        public const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";

        public const string UiToolkitManagerObjectName = "XR UI Toolkit Manager";
        public const string PanelInputConfigurationObjectName = "Panel Input Configuration";

        // 実寸換算（詳細は theme.uss の先頭コメント）。
        // PanelSettings の Pixels Per Unit = 100、板の localScale = 0.2 なので 1 UI px ≒ 2mm。
        // 実体は WorldSpacePanelFactory（PlayMode 試験と同じ組み立てを通すため）。
        public const float PanelLocalScale = WorldSpacePanelFactory.PanelLocalScale;
        public const float PanelPixelsPerUnit = WorldSpacePanelFactory.PanelPixelsPerUnit;

        // レシピ（＝一覧と同じ寸法。重ねて出す2枚なので必ず揃える）。
        // 本文を左右2列にしたので 260×190 で収まる（PanelLayoutTests で検算）。
        // 広げるなら 280 までだが、Bootstrap の初期配置（左右 0.5m 間隔）だと
        // 260（0.52m）の時点で既にタイマーの板と 2cm 重なっており、280 にすると 6cm になる。
        private const float RecipeWidthUnits = 260f; // 実測 ≒ 52cm
        private const float RecipeHeightUnits = 190f; // ≒ 38cm

        // 材料（一目で全部見えるように大きめ）。170×240（34cm×48cm）。
        // 行を 8px に詰めた（theme.uss）ので、20 点程度までスクロール無しで並ぶ。
        // 左隣（基準点から -0.5m）に置いても、右へ 0.34m なのでレシピの板（0m から）に届かない。
        private const float IngredientsWidthUnits = 170f; // ≒ 34cm
        private const float IngredientsHeightUnits = 240f; // ≒ 48cm

        // タイマーは作り口（1/3/5/10分・±30秒）と3つ積む場所が要るので、材料の板より一回り大きい
        // （4cm角のボタンを6つ並べるのに 44cm 要る）。
        private const float TimerWidthUnits = 220f; // ≒ 44cm
        private const float TimerHeightUnits = 220f; // ≒ 44cm

        // 動画の板は向きで寸法が変わる（VideoPanel が持つ。ここは 16:9 の初期値だけ）。
        private const float VideoWidthUnits = VideoPanel.LandscapeWidthUnits;
        private const float VideoHeightUnits = VideoPanel.LandscapeHeightUnits;

        // 手元のメニュー（配置の操作を全部ここへ集めてある）。ここは調理中（「配置」1つ）の
        // 寸法で、頁を変えると PlacementMenuPanel が高さを変える（プリセットの一覧は縦に5行）。
        // 中身の寸法の根拠は PlacementMenu.uss に書いた。
        private const float PlacementMenuWidthUnits = PlacementMenuPanel.WidthUnits;
        private const float PlacementMenuHeightUnits = PlacementMenuPanel.IdleHeightUnits;

        // 手首の釦。22×22 ≒ 4.4cm 角——「押す釦は最小 4cm 角」をちょうど満たす
        // 一番小さい板（WristToggle.uss）。
        private const float WristToggleWidthUnits = 22f;
        private const float WristToggleHeightUnits = 22f;

        /// <summary>手首の釦とメニューを載せる根。</summary>
        public const string WristMenuObjectName = "Wrist Menu";

        public const string WristToggleObjectName = "WristToggle";
        public const string PlacementMenuObjectName = "PlacementMenu";

        /// <summary>注意の板と領域を載せる根（板は実行時に作るので、ここには空の根だけ）。</summary>
        public const string HazardRootObjectName = "Kitchen Hazards";

        /// <summary>指先の光る点を載せる子の名前（左右の Poke Interactor の下に1つずつ）。</summary>
        public const string FingertipCursorObjectName = "Fingertip Cursor";

        // XRI の World Space UI サンプル（WorldSpacePanel.asset）と同じ値。
        // 「既存のコライダーを使う」＝ UI Document は自前でコライダーを作らない。
        private const int ColliderUpdateModeKeepExisting = 1;

        public static void Build()
        {
            var scene = OpenSourceSceneAsCopy();

            RemoveTutorialAndSampleContent(scene);
            ConfigureCameraForPassthrough(scene);

            var panelSettings = CreateOrLoadPanelSettings();

            var panelsRoot = new GameObject("Kitchen Panels");

            // パネルは手の高さより上（胸〜目線）。正面 1.2m・高さ 1.35m を既定に、
            // 材料を左、タイマーを右へ内向きに振る（アンカーに保存するまでの初期位置）。
            var recipeGo = CreatePanelObject(
                "RecipePanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/RecipePanel.uxml"),
                RecipeWidthUnits, RecipeHeightUnits,
                new Vector3(0f, 1.35f, 1.2f), Quaternion.identity);
            var recipePanel = recipeGo.AddComponent<RecipePanel>();

            // レシピを選ぶ板はレシピの板と同じ場所・同じ寸法に重ねて置く
            // （起動時はこちらが出て、選ぶと入れ替わる）。
            // 出し入れは GameObject.SetActive ではなく Presentation/PanelVisibility が行う
            // ——UIDocument は無効化のたびに rootVisualElement を作り直すので、
            // 各パネルが Awake で掴んだ要素の参照が死んでしまう。
            var recipeListGo = CreatePanelObject(
                "RecipeListPanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/RecipeListPanel.uxml"),
                RecipeWidthUnits, RecipeHeightUnits,
                new Vector3(0f, 1.35f, 1.2f), Quaternion.identity);
            var recipeListPanel = recipeListGo.AddComponent<RecipeListPanel>();

            var ingredientsGo = CreatePanelObject(
                "IngredientsPanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/IngredientsPanel.uxml"),
                IngredientsWidthUnits, IngredientsHeightUnits,
                new Vector3(-0.72f, 1.35f, 1.05f), Quaternion.Euler(0f, -25f, 0f));
            var ingredientsPanel = ingredientsGo.AddComponent<IngredientsPanel>();

            var timerGo = CreatePanelObject(
                "TimerPanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/TimerPanel.uxml"),
                TimerWidthUnits, TimerHeightUnits,
                new Vector3(0.72f, 1.35f, 1.05f), Quaternion.Euler(0f, 25f, 0f));
            var timerPanel = timerGo.AddComponent<TimerPanel>();

            // 4枚目（動画）。レシピの右上＝タイマーの上。実行時の位置は
            // Bootstrap.PlaceVideoPanel が頭の向きから決め直すので、ここは Editor で見たときの目安。
            // 動画の板だけ Video の Interaction Layer を名乗る（調理中もレイで操作させるため）。
            // Default も残すので、指で押す（ポーク）のと配置モードで掴むのは今までどおり。
            // 調理モードでは Ray の interactionLayers が Video だけになるので、
            // レイが触れるのはこの板だけになる（CookingModeInputGate を見よ）。
            var videoGo = CreatePanelObject(
                "VideoPanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/VideoPanel.uxml"),
                VideoWidthUnits, VideoHeightUnits,
                new Vector3(0.72f, 1.35f + 0.04f + VideoHeightUnits * 0.002f, 1.05f), Quaternion.Euler(0f, 25f, 0f),
                VideoInteractionLayers);
            var videoPanel = videoGo.AddComponent<VideoPanel>();
            AttachYoutubePlayer(videoPanel);

            var inputGate = panelsRoot.AddComponent<CookingModeInputGate>();
            WireCookingModeInputGate(scene, inputGate);

            var panelPlacement = panelsRoot.AddComponent<PanelPlacement>();
            WirePanelPlacementOrigin(scene, panelPlacement);

            // 手首の釦で出し入れするメニュー。
            // 揃わなければ黙って作らない——レシピ／一覧の板の頭の「配置」（2度押し）が確実な入り口。
            var placementMenuPanel = AttachWristMenu(scene, panelSettings, out var wristMenu);

            // 火気の注意（注意の板とコンロの領域）。板も線も実行時に作るので、根と部品だけ置く。
            var hazards = AttachHazards(scene, panelSettings);

            EnsureUiToolkitInput(scene);

            CreateBootstrap(recipeListPanel, recipePanel, ingredientsPanel, timerPanel, videoPanel, inputGate,
                panelPlacement, placementMenuPanel, wristMenu, hazards);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            RegisterInBuildSettings();

            Debug.Log("[KitchenXR] Kitchen.unity を作成しました。");
        }

        private static Scene OpenSourceSceneAsCopy()
        {
            var scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(Path.GetDirectoryName(KitchenScenePath)!);
            EditorSceneManager.SaveScene(scene, KitchenScenePath); // Save As。元の SampleScene.unity は変更しない。
            return scene;
        }

        private static void RemoveTutorialAndSampleContent(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                // MR テンプレートのチュートリアル UI（Coaching UI・Tutorial Player・手のひらメニュー等）一式。
                if (root.name == "UI")
                {
                    Object.DestroyImmediate(root);
                    continue;
                }

                // (2) テンプレートの仮想の部屋（グリッドの床と空）。本来はチュートリアル UI の
                // トグルが FadeMaterial でこれを消すが、その UI を外すと残り続け、
                // カメラの透明な背景（パススルー）を完全に覆ってしまう。
                if (root.name == "Environment")
                {
                    Object.DestroyImmediate(root);
                }
            }

            // チュートリアルの目標演出とサンプルのオブジェクト出現機（MR Interaction Setup の子）。
            DestroyDeepChildByName(scene, "Goal Manager");
            DestroyDeepChildByName(scene, "Object Spawner");

            DisableManagersThatLostTheirUi(scene);
        }

        /// <summary>
        /// チュートリアル UI（"UI" ルート）を消すと、テンプレートの <c>OcclusionManager</c> が
        /// 握っていたトグルの参照（m_QuestSettings・m_UIToggleObject・m_AndroidXRSettings）が
        /// 全て null になる。Quest では <c>Start()</c> が
        /// <c>m_QuestSettings.SetActive(true)</c> で UnassignedReferenceException を投げ、
        /// そこで初期化が丸ごと止まる（＝この manager は最初から何もしていない。
        /// 手の遮蔽が効いて見えるのは ARShaderOcclusion 側の働き）。
        /// 起動のたびに例外を出すだけなので、参照を失っていれば止めておく。
        /// 遮蔽の切り替えを自前の UI から操作したくなったら、ここで参照を挿し直すこと。
        /// </summary>
        private static void DisableManagersThatLostTheirUi(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
                {
                    if (behaviour == null || behaviour.GetType().Name != "OcclusionManager")
                    {
                        continue;
                    }

                    behaviour.enabled = false;
                    EditorUtility.SetDirty(behaviour);
                    Debug.Log("[KitchenXR] OcclusionManager はチュートリアル UI の参照を失うため無効にしました。");
                }
            }
        }

        /// <summary>
        /// (2) Meta のパススルーは「カメラの背景が透明」であることが条件
        /// （com.unity.xr.meta-openxr の Camera (Passthrough) の項）。テンプレートの既定のままだが、
        /// 作り直しても必ずこの状態になるよう明示的に設定する。
        /// </summary>
        private static void ConfigureCameraForPassthrough(Scene scene)
        {
            var cameraTransform = FindDeepChild(scene, "Main Camera");
            var camera = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
            if (camera == null)
            {
                Debug.LogWarning("[KitchenXR] Main Camera が見つからず、パススルーの背景を設定できませんでした。");
                return;
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            EditorUtility.SetDirty(camera);
        }

        private static PanelSettings CreateOrLoadPanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                Directory.CreateDirectory(Path.GetDirectoryName(PanelSettingsPath)!);
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }

            settings.renderMode = PanelRenderMode.WorldSpace;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.referenceSpritePixelsPerUnit = 100f;

            // Unity 既定の runtime テーマ（ui-uitk skill の注意どおり独自 USS からは参照しない）。
            if (settings.themeStyleSheet == null)
            {
                var themeGuid = AssetDatabase.FindAssets("UnityDefaultRuntimeTheme t:ThemeStyleSheet").FirstOrDefault();
                if (themeGuid != null)
                {
                    var themePath = AssetDatabase.GUIDToAssetPath(themeGuid);
                    settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
                }
            }

            // (1) 日本語のフォント。USS の -unity-font-definition が外れても字が出るように、
            // PanelSettings 側にも既定／代替フォントを持つ TextSettings を結ぶ。
            var fontAsset = KitchenFontAssetBuilder.CreateOrLoadFontAsset();
            var textSettings = KitchenFontAssetBuilder.CreateOrLoadTextSettings(fontAsset);
            KitchenFontAssetBuilder.ApplyToPanelSettings(settings, textSettings);

            // (3) コライダーは自分たちで作る（XRI サンプルの WorldSpacePanel と同じ）。
            var so = new SerializedObject(settings);
            var colliderMode = so.FindProperty("m_ColliderUpdateMode");
            if (colliderMode != null)
            {
                colliderMode.intValue = ColliderUpdateModeKeepExisting;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static VisualTreeAsset LoadUxml(string path) => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);

        /// <summary>
        /// 動画の板が名乗る Interaction Layer（Default ＋ Video）。
        /// Default を残すのが肝心——外すとポークと配置モードの掴みまで効かなくなる
        /// （どちらの Interactor も Default で引き当てている）。
        /// </summary>
        private static int VideoInteractionLayers =>
            1 | (1 << CookingModeInputGate.VideoInteractionLayer);

        private static GameObject CreatePanelObject(
            string name, Transform parent, PanelSettings panelSettings, VisualTreeAsset uxml,
            float widthUnits, float heightUnits, Vector3 localPosition, Quaternion localRotation,
            int? interactionLayers = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;

            // (3) 板の組み立ては PlayMode 試験と同じ WorldSpacePanelFactory を通す。
            // UIDocument.pivot を左上に据える（既定の中央のままだとコライダーと板の矩形が
            // 半分ずれて、当たってはいるのにボタンを掴めない）ところまで含めてここが面倒を見る。
            WorldSpacePanelFactory.Configure(go, panelSettings, uxml, widthUnits, heightUnits, interactionLayers);

            EditorUtility.SetDirty(go.GetComponent<XRSimpleInteractable>());

            return go;
        }

        /// <summary>
        /// (3) XRI 3.5 でワールド空間の UI Toolkit を指で押せるようにするための受け口。
        /// XRI の manual `ui-world-space-ui-toolkit-support` と World Space UI サンプルの DemoScene に合わせる:
        ///   - `XRUIToolkitManager`（これが無いと XRI の UI Toolkit 対応が丸ごと無効）
        ///   - `PanelInputConfiguration`（Panel Input Redirection = Never。EventSystem が UI Toolkit の入力を奪うのを止める）
        ///   - `XRUIInputModule.bypassUIToolkitEvents = false`（テンプレートの既定は true）
        /// </summary>
        private static void EnsureUiToolkitInput(Scene scene)
        {
            if (Object.FindFirstObjectByType<XRUIToolkitManager>(FindObjectsInactive.Include) == null)
            {
                var go = new GameObject(UiToolkitManagerObjectName);
                go.AddComponent<XRUIToolkitManager>();
            }

            if (Object.FindFirstObjectByType<PanelInputConfiguration>(FindObjectsInactive.Include) == null)
            {
                var go = new GameObject(PanelInputConfigurationObjectName);
                var config = go.AddComponent<PanelInputConfiguration>();
                var configSo = new SerializedObject(config);
                configSo.FindProperty("m_Settings.m_PanelInputRedirection").intValue =
                    (int)PanelInputConfiguration.PanelInputRedirection.Never;
                configSo.FindProperty("m_Settings.m_ProcessWorldSpaceInput").boolValue = true;
                configSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
            }

            foreach (var module in Object.FindObjectsByType<XRUIInputModule>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var moduleSo = new SerializedObject(module);
                moduleSo.FindProperty("m_BypassUIToolkitEvents").boolValue = false;
                moduleSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(module);
            }

            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
            {
                Debug.LogWarning("[KitchenXR] EventSystem が見つかりません。UI の入力が届かない可能性があります。");
            }
        }

        /// <summary>
        /// モードで触れてよい層を切り替える Ray を拾って挿す。扱うのは rig の
        /// `Near-Far Interactor` 4つ（左右の手と左右のコントローラ）だけ——手でもコントローラでも
        /// 同じように効く。
        ///
        /// Gaze と Teleport は触らない。Gaze は <c>allowGazeInteraction</c>（既定 false）を立てた
        /// 板にしか効かず台所の板は誰も立てていない。Teleport の <c>interactionLayers</c> は
        /// Teleport（31 番）だけなので、「配置モードでは全層」を当てると逆に板を掴んでしまう。
        ///
        /// 拾えた数と名前をログに出すのは、rig を差し替えたときに黙って 0 個になるのを防ぐため。
        /// </summary>
        private static void WireCookingModeInputGate(Scene scene, CookingModeInputGate gate)
        {
            var xrOrigin = FindDeepChild(scene, "XR Origin (XR Rig)");
            if (xrOrigin == null)
            {
                Debug.LogWarning("[KitchenXR] XR Origin が見つからず、Ray の層の切り替えを配線できませんでした。");
                return;
            }

            // 手や持ち手から前へ伸びる Ray だけ。Poke・Gaze・Teleport はそのまま。
            var rayLike = xrOrigin.GetComponentsInChildren<NearFarInteractor>(true)
                .Cast<Behaviour>()
                .ToArray();

            if (rayLike.Length == 0)
            {
                Debug.LogWarning(
                    "[KitchenXR] rig に Near-Far Interactor が1つもありません。"
                    + "配置モードで遠くの板を掴めず、動画の板もレイで操作できません。");
            }
            else
            {
                Debug.Log(
                    $"[KitchenXR] Ray の Interactor を {rayLike.Length} 個 配線しました: "
                    + string.Join(", ", rayLike.Select(r => HierarchyPath(r.transform))));
            }

            var so = new SerializedObject(gate);
            var prop = so.FindProperty("_rayLikeInteractors");
            prop.arraySize = rayLike.Length;
            for (var i = 0; i < rayLike.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = rayLike[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            AttachRayLineVisibility(rayLike);
            RelaxFarCastStabilization(rayLike);
            AttachFingertipCursors(xrOrigin);
        }

        /// <summary>
        /// 指先の光る点（奥行きの距離感をつかみやすくするため。<see cref="FingertipCursor"/>）。
        ///
        /// rig の <c>Poke Interactor</c>（<see cref="XRPokeInteractor"/>）全部——
        /// つまり左右の手に1つずつ——へ子を1つ足して <see cref="FingertipCursor"/> を載せる。
        /// 点そのもの（球と材質）は実行時に作るので、プレハブも資産も増えない。
        /// 何個付けたかをログに出すのは、rig を差し替えたときに黙って 0 個になるのを防ぐため
        /// （レイの線の出し入れと同じ流儀）。
        /// </summary>
        private static void AttachFingertipCursors(Transform xrOrigin)
        {
            var pokes = xrOrigin.GetComponentsInChildren<XRPokeInteractor>(true);
            if (pokes.Length == 0)
            {
                Debug.LogWarning(
                    "[KitchenXR] rig に Poke Interactor が1つもありません（指先カーソルを出せません）。");
                return;
            }

            foreach (var poke in pokes)
            {
                var existing = poke.GetComponentInChildren<FingertipCursor>(true);
                var cursorGo = existing != null
                    ? existing.gameObject
                    : new GameObject(FingertipCursorObjectName);

                if (existing == null)
                {
                    cursorGo.transform.SetParent(poke.transform, false);
                    cursorGo.transform.localPosition = Vector3.zero;
                    cursorGo.transform.localRotation = Quaternion.identity;
                }

                var cursor = cursorGo.GetComponent<FingertipCursor>()
                             ?? cursorGo.AddComponent<FingertipCursor>();

                var so = new SerializedObject(cursor);
                so.FindProperty("_interactor").objectReferenceValue = poke;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(cursor);
            }

            Debug.Log(
                $"[KitchenXR] 指先カーソルを {pokes.Length} 個の Poke Interactor に付けました: "
                + string.Join(", ", pokes.Select(p => HierarchyPath(p.transform))));
        }

        /// <summary>
        /// 遠くを指すレイの姿勢の安定化を緩める（レイが正面に「吸われて」横の板へ向かない件）。
        ///
        /// XRI の far 側の caster は <c>m_AimTargetObject</c> に自分自身が挿さっているので、
        /// <c>XRTransformStabilizer</c> が「前のフレームの着地点を保つ回転」へ寄せてしまう。
        /// 効き幅は <c>angleStabilization × clamp(1 + ln(rayLength), 1, 3)</c> で、何にも
        /// 当たっていないとき <c>rayEndPoint</c> は 10m 先の空なので係数は上限の 3 ——
        /// 20° の設定が実効 60° まで広がり、空を薙いでいる間ほどレイが渋くなる。
        /// そこで <c>m_AngleStabilization</c> を下げる（手の震え 1〜3° は吸えたまま、狙って
        /// 振る動きは素通しになる）。位置の安定化には触らない。
        ///
        /// ただし主因はハンドトラッキングの aim 姿勢そのもの——Meta の system aim は肩あたりから
        /// 手を通る体に紐づいた向きで、手首をひねっても真横は指せない。効かなければ次の手は
        /// 同じ caster の <c>Aim Target Object</c> を空にすること。
        /// </summary>
        private const float FarCastAngleStabilization = 8f;

        private static void RelaxFarCastStabilization(Behaviour[] rayLike)
        {
            var relaxed = 0;

            foreach (var interactor in rayLike)
            {
                if (interactor == null)
                {
                    continue;
                }

                var caster = interactor.GetComponent<CurveInteractionCaster>();
                if (caster == null)
                {
                    continue;
                }

                var so = new SerializedObject(caster);
                var angle = so.FindProperty("m_AngleStabilization");
                if (angle == null)
                {
                    continue;
                }

                angle.floatValue = FarCastAngleStabilization;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(caster);
                relaxed++;
            }

            Debug.Log(
                $"[KitchenXR] 遠くを指すレイの姿勢の安定化を {relaxed} 個 緩めました"
                + $"（m_AngleStabilization = {FarCastAngleStabilization}）。");
        }

        /// <summary>
        /// レイの線を「指す先があるときだけ」出す。
        ///
        /// 4つの Near-Far Interactor 全部（左右の手・左右のコントローラ）に付ける。
        /// 仕掛けそのものは <see cref="RayLineVisibility"/>——
        /// XRI 3.5 に「無効なときは隠す」の設定が無いので、自前で線を落とす。
        /// </summary>
        private static void AttachRayLineVisibility(Behaviour[] rayLike)
        {
            var attached = 0;

            foreach (var interactor in rayLike)
            {
                if (interactor is not XRBaseInteractor baseInteractor)
                {
                    continue;
                }

                var go = baseInteractor.gameObject;
                var visibility = go.GetComponent<RayLineVisibility>() ?? go.AddComponent<RayLineVisibility>();

                // 描き手と線は Interactor 本体ではなく `LineVisual` という子に載っている
                // （XRI の Left_NearFarInteractor.prefab）。子まで見ないと黙って何も挿さらない。
                var visual = RayLineVisibility.FindLineVisual(go);
                var line = RayLineVisibility.FindLineRenderer(go);

                var visibilitySo = new SerializedObject(visibility);
                visibilitySo.FindProperty("_interactor").objectReferenceValue = baseInteractor;
                visibilitySo.FindProperty("_lineVisual").objectReferenceValue = visual;
                visibilitySo.FindProperty("_lineRenderer").objectReferenceValue = line;
                visibilitySo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(visibility);

                if (line == null)
                {
                    Debug.LogWarning(
                        $"[KitchenXR] {go.name} に LineRenderer が見つかりません（線を消せません）。");
                }

                attached++;
            }

            Debug.Log($"[KitchenXR] レイの線の出し入れを {attached} 個の Interactor に付けました。");
        }

        /// <summary>
        /// TLab の <c>YoutubePlayer.prefab</c> を動画の板の中へ置く。
        ///
        /// TLab の資産は書き換えない。ここでやるのはシーンへ実体を1つ置くことだけで、
        /// 要らない仕掛けを止めるのも位置を合わせるのも実行時（<c>YoutubePlayerBridge</c>・
        /// <c>VideoPanel.LayoutSurface</c>）に行う——プレハブ側へ差分が戻ることが無いように。
        ///
        /// 置いた実体は眠らせておく。起こすのは Android の実機だけで、
        /// Editor では板が「動画は実機で」の札を出す（WebView は Android のプラグイン）。
        /// 名前は <c>YoutubePlayer</c> のまま変えてはいけない——<c>youtube.html</c> が
        /// <c>unitySendMessage('YoutubePlayer', …)</c> でこの名前へ返してくる。
        /// </summary>
        private static void AttachYoutubePlayer(VideoPanel videoPanel)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(YoutubePlayerBridge.PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[KitchenXR] 動画プレイヤーのプレハブが見つかりません: {YoutubePlayerBridge.PlayerPrefabPath}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, videoPanel.transform);
            instance.name = YoutubePlayerBridge.PlayerObjectName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.SetActive(false);

            var so = new SerializedObject(videoPanel);
            so.FindProperty("_playerRoot").objectReferenceValue = instance;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(videoPanel);
        }

        private static void CreateBootstrap(
            RecipeListPanel recipeListPanel, RecipePanel recipePanel, IngredientsPanel ingredientsPanel,
            TimerPanel timerPanel, VideoPanel videoPanel, CookingModeInputGate inputGate,
            PanelPlacement panelPlacement, PlacementMenuPanel placementMenuPanel, WristMenu wristMenu,
            HazardParts hazards)
        {
            var go = new GameObject("Bootstrap");
            var bootstrap = go.AddComponent<Bootstrap>();

            var so = new SerializedObject(bootstrap);
            so.FindProperty("_recipeListPanel").objectReferenceValue = recipeListPanel;
            so.FindProperty("_recipePanel").objectReferenceValue = recipePanel;
            so.FindProperty("_ingredientsPanel").objectReferenceValue = ingredientsPanel;
            so.FindProperty("_timerPanel").objectReferenceValue = timerPanel;
            so.FindProperty("_videoPanel").objectReferenceValue = videoPanel;
            so.FindProperty("_cookingModeInputGate").objectReferenceValue = inputGate;
            so.FindProperty("_panelPlacement").objectReferenceValue = panelPlacement;
            so.FindProperty("_placementMenuPanel").objectReferenceValue = placementMenuPanel;
            so.FindProperty("_wristMenu").objectReferenceValue = wristMenu;
            so.FindProperty("_hazardBoards").objectReferenceValue = hazards.Boards;
            so.FindProperty("_hazardZones").objectReferenceValue = hazards.Zones;
            so.FindProperty("_hazardProximity").objectReferenceValue = hazards.Proximity;

            // 頭の Transform は注意の板の置き場と近さの判定にも要る（未指定なら Camera.main）。
            var head = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            so.FindProperty("_headTransform").objectReferenceValue =
                head != null ? head.transform : null;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>火気の注意の部品（<see cref="AttachHazards"/> が返す一組）。</summary>
        private readonly struct HazardParts
        {
            public HazardParts(HazardBoards boards, HazardZones zones, HazardProximity proximity)
            {
                Boards = boards;
                Zones = zones;
                Proximity = proximity;
            }

            public HazardBoards Boards { get; }
            public HazardZones Zones { get; }
            public HazardProximity Proximity { get; }
        }

        /// <summary>
        /// 火気の注意（注意の板とコンロの領域）。根1つに5つの部品を載せるだけで、
        /// 板も床の線も実行時に作る——プリセットの数だけシーンに板を並べる作りにすると、
        /// プリセットを1つ足すたびにシーンを組み直すことになる。
        ///
        /// Poke Interactor は手の位置（近さの判定と、XR Hands が動かない端末で囲む指先）、
        /// Near-Far Interactor は囲んでいる間の掴みの停止と select 入力に使う。
        /// 拾えた数をログに出すのは、rig を差し替えたときに黙って 0 個になるのを防ぐため
        /// （レイの線・指先カーソルと同じ流儀）。
        /// </summary>
        private static HazardParts AttachHazards(Scene scene, PanelSettings panelSettings)
        {
            var root = new GameObject(HazardRootObjectName);

            var boards = root.AddComponent<HazardBoards>();
            var drawing = root.AddComponent<HazardZoneDrawing>();
            var zones = root.AddComponent<HazardZones>();
            var sound = root.AddComponent<HazardSound>();
            var proximity = root.AddComponent<HazardProximity>();

            var xrOrigin = FindDeepChild(scene, "XR Origin (XR Rig)");
            var head = FindDeepChild(scene, "Main Camera");

            var rayLike = xrOrigin != null
                ? xrOrigin.GetComponentsInChildren<NearFarInteractor>(true)
                : System.Array.Empty<NearFarInteractor>();
            var pokes = xrOrigin != null
                ? xrOrigin.GetComponentsInChildren<XRPokeInteractor>(true)
                : System.Array.Empty<XRPokeInteractor>();

            if (rayLike.Length == 0 || pokes.Length == 0)
            {
                Debug.LogWarning(
                    "[KitchenXR] rig の Interactor が拾えず、領域の作図か近さの判定が効きません"
                    + $"（レイ {rayLike.Length} 個・ポーク {pokes.Length} 個）。");
            }
            else
            {
                Debug.Log(
                    $"[KitchenXR] 火気の注意を配線しました（レイ {rayLike.Length} 個・"
                    + $"ポーク {pokes.Length} 個）。");
            }

            var boardsSo = new SerializedObject(boards);
            boardsSo.FindProperty("_panelSettings").objectReferenceValue = panelSettings;
            boardsSo.FindProperty("_boardUxml").objectReferenceValue =
                LoadUxml("Assets/KitchenXR/Presentation/UI/HazardPanel.uxml");
            boardsSo.FindProperty("_deleteUxml").objectReferenceValue =
                LoadUxml("Assets/KitchenXR/Presentation/UI/HazardDelete.uxml");
            boardsSo.FindProperty("_headTransform").objectReferenceValue = head;
            boardsSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(boards);

            var drawingSo = new SerializedObject(drawing);
            AssignArray(drawingSo.FindProperty("_interactors"), rayLike);
            AssignArray(drawingSo.FindProperty("_pokeInteractors"), pokes);
            drawingSo.FindProperty("_originTransform").objectReferenceValue = xrOrigin;
            drawingSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(drawing);

            var zonesSo = new SerializedObject(zones);
            zonesSo.FindProperty("_originTransform").objectReferenceValue = xrOrigin;
            zonesSo.FindProperty("_drawing").objectReferenceValue = drawing;
            zonesSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(zones);

            var proximitySo = new SerializedObject(proximity);
            proximitySo.FindProperty("_zones").objectReferenceValue = zones;
            proximitySo.FindProperty("_sound").objectReferenceValue = sound;
            AssignArray(proximitySo.FindProperty("_hands"), pokes);
            proximitySo.FindProperty("_headTransform").objectReferenceValue = head;
            proximitySo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(proximity);

            return new HazardParts(boards, zones, proximity);
        }

        private static void AssignArray(SerializedProperty property, Object[] values)
        {
            if (property == null)
            {
                return;
            }

            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        /// <summary>
        /// 控え（`panels.json`）は XR Origin 基準の相対 Pose で持つ（アンカーの退避路）。
        /// その基準になる Transform を挿す。見つからなければ世界座標をそのまま書く
        /// （部屋の原点が動くとずれるが、無いよりまし——これは退避路であってアンカーの代わりではない）。
        /// </summary>
        private static void WirePanelPlacementOrigin(Scene scene, PanelPlacement placement)
        {
            var xrOrigin = FindDeepChild(scene, "XR Origin (XR Rig)");
            if (xrOrigin == null)
            {
                Debug.LogWarning("[KitchenXR] XR Origin が見つからず、板の控えの基準を挿せませんでした。");
                return;
            }

            var so = new SerializedObject(placement);
            so.FindProperty("_originTransform").objectReferenceValue = xrOrigin;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 手元のメニュー。XRI の <c>HandMenu</c> は使わない（理由は <see cref="WristMenu"/>）。
        /// 組むのは3枚: 根の <c>Wrist Menu</c>、その下に <c>WristToggle</c> ×2（左右の手首。
        /// 4.4cm 角。常に付いている）と <c>PlacementMenu</c>（130×92。既定は眠り）。
        /// 手のひらの Transform は <c>Left/Right Hand</c> の下の <c>Palm</c> で、左右どちらも
        /// 欠けたら作らない。
        ///
        /// 配置の「保存・元に戻す・板を手元に・やめる」は全部メニューに載っているので、これが
        /// 無いと配置モードから出られない——<c>Bootstrap</c> は挿さっていなければ入らない。
        /// </summary>
        private static PlacementMenuPanel AttachWristMenu(
            Scene scene, PanelSettings panelSettings, out WristMenu wristMenu)
        {
            wristMenu = null;

            var leftPalm = FindPalm(scene, "Left Hand");
            var rightPalm = FindPalm(scene, "Right Hand");

            if (leftPalm == null && rightPalm == null)
            {
                Debug.LogWarning(
                    "[KitchenXR] 手のひら（Palm）が見つからないので手首のメニューを作りません。"
                    + "配置の操作は全部この板に載っているので、このままだと配置モードは使えません。");
                return null;
            }

            var menuRoot = new GameObject(WristMenuObjectName);
            wristMenu = menuRoot.AddComponent<WristMenu>();

            var menuGo = CreatePanelObject(
                PlacementMenuObjectName, menuRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/PlacementMenu.uxml"),
                PlacementMenuWidthUnits, PlacementMenuHeightUnits,
                Vector3.zero, Quaternion.identity);
            var menuPanel = menuGo.AddComponent<PlacementMenuPanel>();
            menuGo.SetActive(false); // 起動時は閉じている（釦を押すまで何も出ない）。

            var leftToggle = leftPalm != null
                ? CreateWristToggle(menuRoot.transform, panelSettings, "Left")
                : null;
            var rightToggle = rightPalm != null
                ? CreateWristToggle(menuRoot.transform, panelSettings, "Right")
                : null;

            var head = FindDeepChild(scene, "Main Camera");

            var so = new SerializedObject(wristMenu);
            so.FindProperty("_leftPalmAnchor").objectReferenceValue = leftPalm;
            so.FindProperty("_rightPalmAnchor").objectReferenceValue = rightPalm;
            so.FindProperty("_leftToggleButton").objectReferenceValue = leftToggle;
            so.FindProperty("_rightToggleButton").objectReferenceValue = rightToggle;
            so.FindProperty("_menuRoot").objectReferenceValue = menuGo;
            so.FindProperty("_headTransform").objectReferenceValue = head;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(wristMenu);

            return menuPanel;
        }

        /// <summary>
        /// 手首の小さな板を1枚作る。
        ///
        /// レイの相手にしない（誤って遠くから押されないように）——
        /// 物理層を <see cref="CookingModeInputGate.OffRayPhysicsLayer"/>（8 番）に置く。
        /// Ray（Near-Far Interactor）の <c>raycastMask</c> は Default(0)／UI(5)／XR Simulation(31) なので
        /// 8 番には届かず、<c>Physics.DefaultRaycastLayers</c> には入ったままなので指では押せる。
        /// <see cref="CookingModeInputGate"/> には登録しない——モードで層を戻されては意味が無い。
        /// </summary>
        private static WristMenuButton CreateWristToggle(
            Transform parent, PanelSettings panelSettings, string side)
        {
            var go = CreatePanelObject(
                $"{WristToggleObjectName} ({side})", parent, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/WristToggle.uxml"),
                WristToggleWidthUnits, WristToggleHeightUnits,
                Vector3.zero, Quaternion.identity);

            go.layer = CookingModeInputGate.OffRayPhysicsLayer;

            return go.AddComponent<WristMenuButton>();
        }

        /// <summary>手のひらの Transform（`Left Hand`／`Right Hand` の下の `Palm`）を探す。</summary>
        private static Transform FindPalm(Scene scene, string handName)
        {
            var hand = FindDeepChild(scene, handName);
            return hand != null ? FindDeepChild(hand, "Palm") : null;
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == KitchenScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(KitchenScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>診断のログ用。rig の中のどこに居る Interactor かを見せる。</summary>
        private static string HierarchyPath(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent)
            {
                path = p.name + "/" + path;
            }

            return path;
        }

        private static Transform FindDeepChild(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindDeepChild(root.transform, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindDeepChild(Transform parent, string name)
        {
            if (parent.name == name)
            {
                return parent;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var found = FindDeepChild(parent.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void DestroyDeepChildByName(Scene scene, string name)
        {
            var found = FindDeepChild(scene, name);
            if (found != null)
            {
                Object.DestroyImmediate(found.gameObject);
            }
        }
    }
}
