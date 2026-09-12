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
using KitchenXR.Presentation.Video;

namespace KitchenXR.App.Editor
{
    /// <summary>
    /// Kitchen.unity を組み立てるバッチ用ツール（-executeMethod から叩く。何度でも作り直せる）。
    /// MR テンプレートの SampleScene を複製し、チュートリアル UI・サンプル専用オブジェクト・
    /// テンプレートの仮想環境（Environment）を外して、レシピ／材料／タイマーの3枚の
    /// ワールド空間 UI Toolkit パネルと Bootstrap を足す。
    ///
    /// 2026-09-12 の実機確認で見つかった3点をここで直している（詳細は各所のコメント）:
    ///   (2) `Environment` ルートを消し忘れていたのでパススルーが見えなかった
    ///   (3) XRI 3.5 のワールド空間 UI Toolkit に必要な受け口
    ///       （XRUIToolkitManager・PanelInputConfiguration・bypassUIToolkitEvents=false）が無く、
    ///       さらにパネルのコライダーが UI px のまま（100倍）で isTrigger だったので指がすり抜けた
    /// </summary>
    public static class KitchenSceneBuilder
    {
        private const string SourceScenePath = "Assets/Scenes/SampleScene.unity";
        public const string KitchenScenePath = "Assets/KitchenXR/Scenes/Kitchen.unity";
        public const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";

        public const string UiToolkitManagerObjectName = "XR UI Toolkit Manager";
        public const string PanelInputConfigurationObjectName = "Panel Input Configuration";

        // 設計 §9・§7 の実寸換算（詳細は theme.uss の先頭コメント）。
        // PanelSettings の Pixels Per Unit = 100、板の localScale = 0.2 なので 1 UI px ≒ 2mm。
        // 実体は WorldSpacePanelFactory（PlayMode 試験と同じ組み立てを通すため）。
        public const float PanelLocalScale = WorldSpacePanelFactory.PanelLocalScale;
        public const float PanelPixelsPerUnit = WorldSpacePanelFactory.PanelPixelsPerUnit;

        // レシピ（＝一覧と同じ寸法。重ねて出す2枚なので必ず揃える）。
        // 2026-09-13 の主人の実機確認で「1画面に進捗・画像・説明」へ組み直したが、
        // 左右2列にしたら 260×190 のままで収まった（PlayMode 試験 PanelLayoutTests で検算）ので広げていない。
        // 広げるなら 280 までだが、Bootstrap の初期配置（左右 0.5m 間隔）だと
        // 260（0.52m）の時点で既にタイマーの板と 2cm 重なっており、280 にすると 6cm になる。
        private const float RecipeWidthUnits = 260f; // 実測 ≒ 52cm
        private const float RecipeHeightUnits = 190f; // ≒ 38cm

        // 材料（2026-09-13 主人「もうちょいパネルは大きくてもいい」「一目で全部見たい」）。
        // 150×190 → 170×240（34cm×48cm）。行を 8px に詰めた（theme.uss）ので、
        // 見本の炒飯（13 点）どころか 20 点程度までスクロール無しで並ぶ。
        // 左隣（基準点から -0.5m）に置いても、右へ 0.34m なのでレシピの板（0m から）に届かない。
        private const float IngredientsWidthUnits = 170f; // ≒ 34cm
        private const float IngredientsHeightUnits = 240f; // ≒ 48cm

        // タイマーは §11 追補で「常時使える」作り口（1/3/5/10分・±30秒）と3つ積む場所が要るので、
        // 材料の板より一回り大きい（4cm角のボタンを6つ並べるのに 44cm 要る）。
        private const float TimerWidthUnits = 220f; // ≒ 44cm
        private const float TimerHeightUnits = 220f; // ≒ 44cm

        // 動画の板は向きで寸法が変わる（VideoPanel が持つ。ここは 16:9 の初期値だけ）。
        private const float VideoWidthUnits = VideoPanel.LandscapeWidthUnits;
        private const float VideoHeightUnits = VideoPanel.LandscapeHeightUnits;

        // 手元のメニュー（P2 → 配置の操作を全部ここへ集めた。設計 §11 追補「配置とレイ」）。
        // 70×36（14cm×7cm）では「保存・元に戻す・板を手元に・やめる」の4つが入らないので
        // 130×92（26cm×18.4cm）へ広げた。中身の寸法の根拠は PlacementMenu.uss に書いた。
        private const float PlacementMenuWidthUnits = 130f;
        private const float PlacementMenuHeightUnits = 92f;

        // 手首の釦（2026-09-13 主人の実機確認 v1.0.8 の③）。22×22 ≒ 4.4cm 角
        // ——設計 §7 の「押す釦は最小 4cm 角」をちょうど満たす一番小さい板（WristToggle.uss）。
        private const float WristToggleWidthUnits = 22f;
        private const float WristToggleHeightUnits = 22f;

        /// <summary>手首の釦とメニューを載せる根。</summary>
        public const string WristMenuObjectName = "Wrist Menu";

        public const string WristToggleObjectName = "WristToggle";
        public const string PlacementMenuObjectName = "PlacementMenu";

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

            // 設計 §7「パネルは手の高さより上（胸〜目線）」。正面 1.2m・高さ 1.35m を既定に、
            // 材料を左、タイマーを右へ内向きに振る（P2 でアンカーに保存するまでの初期位置）。
            var recipeGo = CreatePanelObject(
                "RecipePanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/RecipePanel.uxml"),
                RecipeWidthUnits, RecipeHeightUnits,
                new Vector3(0f, 1.35f, 1.2f), Quaternion.identity);
            var recipePanel = recipeGo.AddComponent<RecipePanel>();

            // P3。レシピを選ぶ板は**レシピの板と同じ場所・同じ寸法**に重ねて置く
            // （起動時はこちらが出て、選ぶと入れ替わる。主人の指示）。
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

            // 4枚目（動画）。レシピの右上＝タイマーの上（設計 P4）。実行時の位置は
            // Bootstrap.PlaceVideoPanel が頭の向きから決め直すので、ここは Editor で見たときの目安。
            // 動画の板だけ **Video の Interaction Layer** を名乗る（設計 §11 追補 2026-09-13。
            // 主人「Youtube プレイヤーだけレイ操作を有効化してほしい」）。Default も残すので、
            // 指で押す（ポーク）のと配置モードで掴むのは今までどおり。
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

            // P2 →（v1.0.8）手首の釦で出し入れするメニュー（設計 §4.4・§11 追補 2026-09-13 の③）。
            // 揃わなければ黙って作らない——レシピ／一覧の板の頭の「配置」（2度押し）が確実な入り口。
            var placementMenuPanel = AttachWristMenu(scene, panelSettings, out var wristMenu);

            EnsureUiToolkitInput(scene);

            CreateBootstrap(recipeListPanel, recipePanel, ingredientsPanel, timerPanel, videoPanel, inputGate,
                panelPlacement, placementMenuPanel, wristMenu);

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

                // (2) パススルーが効かなかった原因。テンプレートの仮想の部屋（グリッドの床と空）。
                // 本来はチュートリアル UI のトグルが FadeMaterial でこれを消すが、その UI を外したので
                // 消えないまま残り、カメラの透明な背景（パススルー）を完全に覆っていた。
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
        /// P2 で遮蔽の切り替えを自前の UI から操作したくなったら、ここで参照を挿し直すこと。
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
        /// Default を残すのが肝心——外すと**ポークと配置モードの掴み**まで効かなくなる
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
        /// モードで触れてよい層を切り替える Ray を拾って挿す（設計 §4.4・§7・§11 追補）。
        ///
        /// MR テンプレートの rig（`MR Interaction Setup` の中の
        /// `XR Origin Hands (XR Rig)` を `XR Origin (XR Rig)` に改名したもの）が持つ Interactor は
        /// 2026-09-13 に数えたところ 7 つ:
        ///   - `Near-Far Interactor`（NearFarInteractor）×4 —— **これだけをここで扱う**。
        ///     `Camera Offset/` の下の `Left Hand`・`Right Hand`（ハンドトラッキング）と
        ///     `Left Controller`・`Right Controller`（コントローラ）に1つずつ。
        ///     つまり**手でもコントローラでも**同じように効く。
        ///   - `Gaze Interactor`（XRGazeInteractor）×1
        ///   - `Teleport Interactor`（XRRayInteractor）×2
        /// ほかに左右の手に `Poke Interactor`（XRPokeInteractor）が1つずつ（ここでは触らない）。
        ///
        /// **視線と移動は触らない**（v1.0.7 は調理モードで一緒に止めていた）。
        ///   - Gaze は <c>XRBaseInteractable.allowGazeInteraction</c>（既定 false）を立てた板にしか
        ///     効かない。台所の板は誰も立てていないので、生きていても悪さをしない。
        ///   - Teleport の <c>interactionLayers</c> は Teleport（31 番）だけ。
        ///     ここで「配置モードでは全層」を当ててしまうと、逆に板を掴んでしまう。
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

            // 手や持ち手から前へ伸びる Ray だけ。Poke・Gaze・Teleport はそのまま（設計 §4.4・§7）。
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
        /// 指先の光る点（設計 §11 追補 2026-09-13 v1.0.9 の⑤。
        /// 主人「指が UI に近づいたときだけ、人差し指の先端に小さな『光るドット（カーソル）』…
        /// これがあるだけで、奥行きの距離感が一気につかみやすくなります」）。
        ///
        /// rig の <c>Poke Interactor</c>（<see cref="XRPokeInteractor"/>）**全部**——
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
        /// 遠くを指すレイの**姿勢の安定化**を緩める（2026-09-13 主人の実機確認 v1.0.8 の②の一部）。
        ///
        /// 主人の言葉:
        ///   「Youtube のパネルはレイ操作反応しましたが、横に（正面と垂直になるように）配置した時に、
        ///     レイが正面に吸われてしまって、Youtube パネルに近づいてもレイはそっちに向きませんでした」
        ///
        /// 「吸われる」の**仕掛けの側の理由**はここにある。XRI の far 側の caster
        /// （<c>CurveInteractionCaster</c>）は <c>m_EnableStabilization = 1</c> で、
        /// <c>m_AimTargetObject</c> に**自分自身**（<c>NearFarInteractor</c>＝<c>IXRRayProvider</c>）が
        /// 挿さっている。<c>XRTransformStabilizer</c> はこのとき
        /// 「**前のフレームのレイの着地点を保ち続ける回転**」（<c>antiRotation</c>）を作って、
        /// そちらへ寄せるほうが安ければそちらを選ぶ（<c>StabilizeOptimalRotation</c>）。
        /// しかも効き幅は
        ///   <c>targetAngleScale = angleStabilization × clamp(1 + ln(rayLength), 1, 3)</c>
        /// で、**何にも当たっていないとき**の <c>rayEndPoint</c> は 10m 先の空
        /// （<c>farInteractionCaster.lastSamplePoint</c>）なので係数は上限の 3 になる。
        /// つまり 20° の設定が実効 **60°** まで広がり、空を薙いでいる間ほどレイが渋くなる。
        ///
        /// そこで <c>m_AngleStabilization</c> を 20 → <see cref="FarCastAngleStabilization"/> にする。
        /// 手の震え（およそ 1〜3°）はこれでも十分に吸うが、**狙って振る動き**は素通しになる。
        /// 位置の安定化（<c>m_PositionStabilization</c>）には触らない。
        ///
        /// **これは②の主因ではない**（詳しくは設計 §11 の追補）。主因は
        /// ハンドトラッキングの aim（照準）姿勢そのもの——Meta の system aim は
        /// 肩あたりから手を通る**体に紐づいた**向きで、手首をひねっても真横は指せない。
        /// ここで直せるのは「振ったときの渋さ」だけで、それでも実機の手触りは変わるはず。
        /// 効かなければ次の手は、同じ caster の <c>Aim Target Object</c> を空にすること
        /// （着地点を保つ働きが丸ごと止まる）。
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
        /// レイの線を「指す先があるときだけ」出す（設計 §11 追補 2026-09-13 v1.0.8 の④。
        /// 主人「レイが操作できない場合は表示を消してほしい」）。
        ///
        /// 4つの Near-Far Interactor **全部**（左右の手・左右のコントローラ）に付ける。
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

                // 描き手と線は Interactor 本体ではなく **`LineVisual` という子**に載っている
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
        /// 主人の <c>YoutubePlayer.prefab</c> を動画の板の中へ置く（設計 §6・ROADMAP P4）。
        ///
        /// **主人の資産は書き換えない。** ここでやるのはシーンへ実体を1つ置くことだけで、
        /// 要らない仕掛けを止めるのも位置を合わせるのも**実行時**（<c>YoutubePlayerBridge</c>・
        /// <c>VideoPanel.LayoutSurface</c>）に行う——プレハブ側へ差分が戻ることが無いように。
        ///
        /// 置いた実体は**眠らせておく**。起こすのは Android の実機だけで、
        /// Editor では板が「動画は実機で」の札を出す（WebView は Android のプラグイン）。
        /// 名前は <c>YoutubePlayer</c> のまま変えてはいけない——主人の <c>youtube.html</c> が
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
            PanelPlacement panelPlacement, PlacementMenuPanel placementMenuPanel, WristMenu wristMenu)
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
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 控え（`panels.json`）は **XR Origin 基準の相対 Pose** で持つ（設計 §4.3 の退避路）。
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
        /// 手元のメニュー（設計 §4.4「入り方＝手のひらメニュー」・§11 追補 2026-09-13 v1.0.8 の③）。
        ///
        /// **v1.0.8 で XRI の <c>HandMenu</c> をやめた。** 主人の言葉:
        ///   「手のひらメニューはいい感じですが、手を横に向けているときにも表示されています。
        ///     手を洗ってるときなどに出ると邪魔なので、要改善。手首の手のひら側にボタンを作っておいて、
        ///     それを押すとパネルがトグルでも全然いいと思います」
        /// <c>HandMenu</c> は手のひらの向きを合図に出し入れするが、サンプルの設定の閾値
        /// （手のひらが上を向いている＝95.7° 以内）は水平より広いので横を向けても成立する。
        /// 理由と代わりの仕掛けは <see cref="WristMenu"/> に書いた。
        ///
        /// ここで組むのは3枚:
        ///   - <c>Wrist Menu</c>（根。<see cref="WristMenu"/> が板を手首に追わせる）
        ///     - <c>WristToggle</c> ×2（左右の手首。4.4cm 角。**常に付いている**）
        ///     - <c>PlacementMenu</c>（130×92。釦を押したときだけ出る。既定は眠り）
        ///
        /// 手のひらの Transform の取り方は <c>HandMenu</c> と同じ——
        /// <c>Left Hand</c>／<c>Right Hand</c> の下の <c>Palm</c>。
        /// **左右どちらも欠けたら作らない。**
        /// 配置の「保存・元に戻す・板を手元に・やめる」は全部メニューに載っているので、
        /// これが無いと**配置モードから出られない**。そのため <c>Bootstrap</c> は、
        /// この板が挿さっていなければ配置モードへ入らない。
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
        /// **レイの相手にしない**（主人の④と同じ筋。誤って遠くから押されないように）——
        /// 物理層を <see cref="CookingModeInputGate.OffRayPhysicsLayer"/>（8 番）に置く。
        /// Ray（Near-Far Interactor）の <c>raycastMask</c> は Default(0)／UI(5)／XR Simulation(31) なので
        /// 8 番には届かず、<c>Physics.DefaultRaycastLayers</c> には入ったままなので**指では押せる**。
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
