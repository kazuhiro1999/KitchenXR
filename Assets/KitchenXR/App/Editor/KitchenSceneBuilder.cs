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
using UnityEngine.XR.Interaction.Toolkit.UI;
using KitchenXR.Presentation;

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

        private const float RecipeWidthUnits = 260f; // 実測 ≒ 52cm
        private const float RecipeHeightUnits = 190f; // ≒ 38cm
        private const float SideWidthUnits = 150f; // ≒ 30cm
        private const float SideHeightUnits = 190f; // ≒ 38cm

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

            var ingredientsGo = CreatePanelObject(
                "IngredientsPanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/IngredientsPanel.uxml"),
                SideWidthUnits, SideHeightUnits,
                new Vector3(-0.72f, 1.35f, 1.05f), Quaternion.Euler(0f, -25f, 0f));
            var ingredientsPanel = ingredientsGo.AddComponent<IngredientsPanel>();

            var timerGo = CreatePanelObject(
                "TimerPanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/TimerPanel.uxml"),
                SideWidthUnits, SideHeightUnits,
                new Vector3(0.72f, 1.35f, 1.05f), Quaternion.Euler(0f, 25f, 0f));
            var timerPanel = timerGo.AddComponent<TimerPanel>();

            var inputGate = panelsRoot.AddComponent<CookingModeInputGate>();
            WireCookingModeInputGate(scene, inputGate);

            EnsureUiToolkitInput(scene);

            CreateBootstrap(recipePanel, ingredientsPanel, timerPanel, inputGate);

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

        private static GameObject CreatePanelObject(
            string name, Transform parent, PanelSettings panelSettings, VisualTreeAsset uxml,
            float widthUnits, float heightUnits, Vector3 localPosition, Quaternion localRotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;

            // (3) 板の組み立ては PlayMode 試験と同じ WorldSpacePanelFactory を通す。
            // UIDocument.pivot を左上に据える（既定の中央のままだとコライダーと板の矩形が
            // 半分ずれて、当たってはいるのにボタンを掴めない）ところまで含めてここが面倒を見る。
            WorldSpacePanelFactory.Configure(go, panelSettings, uxml, widthUnits, heightUnits);

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

        private static void WireCookingModeInputGate(Scene scene, CookingModeInputGate gate)
        {
            var xrOrigin = FindDeepChild(scene, "XR Origin (XR Rig)");
            if (xrOrigin == null)
            {
                Debug.LogWarning("[KitchenXR] XR Origin が見つからず、調理モードの Ray 無効化を配線できませんでした。");
                return;
            }

            // Ray っぽい Interactor だけを止める。Poke Interactor はそのまま（設計 §4.4・§7）。
            var rayLike = xrOrigin.GetComponentsInChildren<NearFarInteractor>(true).Cast<Behaviour>()
                .Concat(xrOrigin.GetComponentsInChildren<XRRayInteractor>(true))
                .ToArray();

            var so = new SerializedObject(gate);
            var prop = so.FindProperty("_rayLikeInteractors");
            prop.arraySize = rayLike.Length;
            for (var i = 0; i < rayLike.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = rayLike[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateBootstrap(
            RecipePanel recipePanel, IngredientsPanel ingredientsPanel, TimerPanel timerPanel,
            CookingModeInputGate inputGate)
        {
            var go = new GameObject("Bootstrap");
            var bootstrap = go.AddComponent<Bootstrap>();

            var so = new SerializedObject(bootstrap);
            so.FindProperty("_recipePanel").objectReferenceValue = recipePanel;
            so.FindProperty("_ingredientsPanel").objectReferenceValue = ingredientsPanel;
            so.FindProperty("_timerPanel").objectReferenceValue = timerPanel;
            so.FindProperty("_cookingModeInputGate").objectReferenceValue = inputGate;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == KitchenScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(KitchenScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
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
