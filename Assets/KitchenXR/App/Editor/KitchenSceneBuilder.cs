using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using KitchenXR.Presentation;

namespace KitchenXR.App.Editor
{
    /// <summary>
    /// Kitchen.unity を組み立てるバッチ用ツール（-executeMethod から一度だけ叩く）。
    /// MR テンプレートの SampleScene を複製し、チュートリアル UI とサンプル専用オブジェクトを外して、
    /// レシピ／材料／タイマーの3枚のワールド空間 UI Toolkit パネルと Bootstrap を足す。
    /// エディタを直接触らずにシーンを作るための使い捨てスクリプトなので、以後 UI の細部を
    /// 調整するときはこれを直接編集するのではなく、Kitchen.unity をエディタで開いて直す想定。
    /// </summary>
    public static class KitchenSceneBuilder
    {
        private const string SourceScenePath = "Assets/Scenes/SampleScene.unity";
        private const string KitchenScenePath = "Assets/KitchenXR/Scenes/Kitchen.unity";
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";

        // 設計 §9・§7 の実寸換算（詳細は theme.uss の先頭コメント）。
        // 1 UI 単位 ≒ 2mm（板の localScale を 0.2 にそろえた前提）。
        private const float PanelLocalScale = 0.2f;
        private const float RecipeWidthUnits = 260f; // 実測 ≒ 52cm
        private const float RecipeHeightUnits = 190f; // ≒ 38cm
        private const float SideWidthUnits = 150f; // ≒ 30cm
        private const float SideHeightUnits = 190f; // ≒ 38cm

        public static void Build()
        {
            var scene = OpenSourceSceneAsCopy();

            RemoveTutorialAndSampleContent(scene);

            var panelSettings = CreateOrLoadPanelSettings();

            var panelsRoot = new GameObject("Kitchen Panels");

            var recipeGo = CreatePanelObject(
                "RecipePanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/RecipePanel.uxml"),
                RecipeWidthUnits, RecipeHeightUnits);
            var recipePanel = recipeGo.AddComponent<RecipePanel>();

            var ingredientsGo = CreatePanelObject(
                "IngredientsPanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/IngredientsPanel.uxml"),
                SideWidthUnits, SideHeightUnits);
            var ingredientsPanel = ingredientsGo.AddComponent<IngredientsPanel>();

            var timerGo = CreatePanelObject(
                "TimerPanel", panelsRoot.transform, panelSettings,
                LoadUxml("Assets/KitchenXR/Presentation/UI/TimerPanel.uxml"),
                SideWidthUnits, SideHeightUnits);
            var timerPanel = timerGo.AddComponent<TimerPanel>();

            var inputGate = panelsRoot.AddComponent<CookingModeInputGate>();
            WireCookingModeInputGate(scene, inputGate);

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
                }
            }

            // チュートリアルの目標演出とサンプルのオブジェクト出現機（MR Interaction Setup の子）。
            // 設計「テンプレートのチュートリアルUIとサンプルの物は外す」対象。
            DestroyDeepChildByName(scene, "Goal Manager");
            DestroyDeepChildByName(scene, "Object Spawner");
        }

        private static PanelSettings CreateOrLoadPanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null)
            {
                return existing;
            }

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.renderMode = PanelRenderMode.WorldSpace;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.referenceSpritePixelsPerUnit = 100f;

            // Unity 既定の runtime テーマが見つかれば使う（見た目は theme.uss で上書きする前提。
            // ui-uitk skill の注意どおり UnityDefaultRuntimeTheme.tss を独自 USS からは参照しない）。
            var themeGuid = AssetDatabase.FindAssets("UnityDefaultRuntimeTheme t:ThemeStyleSheet").FirstOrDefault();
            if (themeGuid != null)
            {
                var themePath = AssetDatabase.GUIDToAssetPath(themeGuid);
                settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PanelSettingsPath)!);
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            return settings;
        }

        private static VisualTreeAsset LoadUxml(string path) => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);

        private static GameObject CreatePanelObject(
            string name, Transform parent, PanelSettings panelSettings, VisualTreeAsset uxml,
            float widthUnits, float heightUnits)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(PanelLocalScale, PanelLocalScale, PanelLocalScale);

            var uiDocument = go.AddComponent<UIDocument>();
            uiDocument.panelSettings = panelSettings;
            uiDocument.visualTreeAsset = uxml;
            uiDocument.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            uiDocument.worldSpaceSize = new Vector2(widthUnits, heightUnits);

            // パネルの原点は左上（XRI World Space UI サンプルと同じ向き）。当たり判定もそれに合わせる。
            var collider = go.AddComponent<BoxCollider>();
            collider.center = new Vector3(widthUnits / 2f, -heightUnits / 2f, 0f);
            collider.size = new Vector3(widthUnits, heightUnits, 2f);
            collider.isTrigger = true;

            var interactable = go.AddComponent<XRSimpleInteractable>();

            var pokeFilter = go.AddComponent<XRPokeFilter>();
            pokeFilter.pokeInteractable = interactable;
            pokeFilter.pokeCollider = collider;

            return go;
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
