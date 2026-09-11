using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KitchenXR.App.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 2026-09-12 に実機（Quest 3）で見つかった3点の再発を止める検算。
    ///   (1) 文字が1つも描かれない   → フォントが解決されているか
    ///   (2) パススルーが効かない     → テンプレートの環境が残っていないか・カメラ背景が透明か
    ///   (3) 指がすり抜ける           → XRI のワールド空間 UI Toolkit の受け口が揃っているか
    /// おまけ: アイコンと版（主人の追加指示）。
    /// </summary>
    public class KitchenSceneIntegrityTests
    {
        private const string ThemeUssPath = "Assets/KitchenXR/Presentation/UI/theme.uss";
        private static readonly string[] PanelNames = { "RecipePanel", "IngredientsPanel", "TimerPanel" };

        private Scene _scene;

        [OneTimeSetUp]
        public void OpenKitchenScene()
        {
            _scene = EditorSceneManager.OpenScene(KitchenSceneBuilder.KitchenScenePath, OpenSceneMode.Single);
        }

        // ---------------------------------------------------------------- (1) 文字

        [Test]
        public void UIToolkit用のフォント資産とTextSettingsが在る()
        {
            var missing = KitchenFontAssetBuilder.MissingAssets();
            Assert.IsEmpty(missing, "UI Toolkit 用のフォント一式が足りません: " + string.Join(", ", missing));

            var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(KitchenFontAssetBuilder.FontAssetPath);
            Assert.IsNotNull(fontAsset,
                "UI Toolkit が使えるのは UnityEngine.TextCore.Text.FontAsset だけ（TMPro.TMP_FontAsset は不可）。");
        }

        [Test]
        public void PanelSettingsにTextSettingsが結ばれている()
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(KitchenSceneBuilder.PanelSettingsPath);
            Assert.IsNotNull(panelSettings, "KitchenPanelSettings が見つかりません。");

            var so = new SerializedObject(panelSettings);
            var textSettings = so.FindProperty("textSettings");
            Assert.IsNotNull(textSettings);
            Assert.IsNotNull(textSettings.objectReferenceValue,
                "PanelSettings.textSettings が空だと日本語の代替フォントが無い。");
            Assert.AreEqual(PanelRenderMode.WorldSpace, panelSettings.renderMode);
        }

        [Test]
        public void themeUssがTextCoreのFontAssetを取り込んでいる()
        {
            // これが今回の不具合の核心。`-unity-font-definition` の指す資産の型が合わないと、
            // USS の取り込みは黙って成功し、フォントの参照だけが落ちる（= 字が1つも出ない）。
            // 取り込み済みの StyleSheet が本当に FontAsset を掴んでいるかを見る。
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemeUssPath);
            Assert.IsNotNull(sheet, "theme.uss が取り込まれていません。");

            var referenced = new List<Object>();
            var iterator = new SerializedObject(sheet).GetIterator();
            while (iterator.NextVisible(true))
            {
                if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                    iterator.objectReferenceValue != null)
                {
                    referenced.Add(iterator.objectReferenceValue);
                }
            }

            Assert.IsTrue(referenced.Any(o => o is FontAsset),
                "theme.uss が UI Toolkit の FontAsset を1つも掴んでいません。参照: "
                + string.Join(", ", referenced.Select(o => $"{o.name}({o.GetType().Name})")));

            var font = referenced.OfType<FontAsset>().First();
            Assert.AreEqual(KitchenFontAssetBuilder.FontAssetPath, AssetDatabase.GetAssetPath(font));
        }

        [Test]
        public void TMPのFontAssetをUSSから参照していない()
        {
            var offenders = new List<string>();
            foreach (var path in Directory.EnumerateFiles(
                         Path.Combine(Directory.GetCurrentDirectory(), "Assets/KitchenXR"), "*.uss",
                         SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(path);
                foreach (Match match in Regex.Matches(text, @"-unity-font-definition:\s*url\(""([^""]+)""\)"))
                {
                    var url = match.Groups[1].Value;
                    var assetPath = Regex.Match(url, @"project://database/([^?]+)").Groups[1].Value
                        .Replace("%20", " ");
                    if (assetPath.Length == 0)
                    {
                        continue;
                    }

                    if (AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath) == null)
                    {
                        offenders.Add($"{Path.GetFileName(path)}: {assetPath} は TextCore の FontAsset ではありません");
                    }
                }
            }

            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        [Test]
        public void themeの長さの変数に単位が付いている()
        {
            var text = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), ThemeUssPath));
            var offenders = Regex.Matches(text, @"--(?:font-size|radius|button|gap)[\w-]*\s*:\s*([^;]+);")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Where(v => Regex.IsMatch(v, @"^-?\d+(\.\d+)?$"))
                .ToList();

            Assert.IsEmpty(offenders,
                "theme.uss の長さの変数に単位（px）がありません: " + string.Join(", ", offenders));
        }

        [Test]
        public void themeのフォント指定がTextCoreのFontAssetを指している()
        {
            var text = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), ThemeUssPath));
            var match = Regex.Match(text, @"-unity-font-definition:\s*url\(""([^""]+)""\)");
            Assert.IsTrue(match.Success, "theme.uss に -unity-font-definition がありません。");

            StringAssert.DoesNotContain("TextMesh Pro", match.Groups[1].Value,
                "TMP の FontAsset は UI Toolkit では使えません（型が違うので指定ごと無視されます）。");
            StringAssert.Contains("NotoSansJP-Regular%20UITK", match.Groups[1].Value);
        }

        // ---------------------------------------------------------------- (2) パススルー

        [Test]
        public void パススルーを覆うテンプレートの環境が残っていない()
        {
            var roots = _scene.GetRootGameObjects().Select(g => g.name).ToList();
            CollectionAssert.DoesNotContain(roots, "Environment",
                "MR テンプレートの仮想の部屋（グリッドの床と空）が残るとパススルーが完全に隠れます。");

            var fadeMaterials = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(b => b != null && b.GetType().Name == "FadeMaterial")
                .Select(b => b.name)
                .ToList();
            Assert.IsEmpty(fadeMaterials,
                "環境をフェードさせる FadeMaterial が残っています（チュートリアル UI を外したので誰も消してくれません）: "
                + string.Join(", ", fadeMaterials));
        }

        [Test]
        public void カメラの背景が透明になっている()
        {
            var camera = MainCamera();
            Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags,
                "Meta のパススルーはカメラの背景が Solid Color であることが条件です。");
            Assert.AreEqual(0f, camera.backgroundColor.a, 0.001f,
                "背景色のアルファが 0 でないとパススルーが覆われます。");
        }

        [Test]
        public void ARのカメラ機能がカメラに付いている()
        {
            var camera = MainCamera();
            var names = camera.GetComponents<Component>().Select(c => c.GetType().Name).ToList();
            CollectionAssert.Contains(names, "ARCameraManager",
                "AR Camera Manager がパススルーの入り切りを担っています。");
        }

        // ---------------------------------------------------------------- (3) Poke

        [Test]
        public void XRUIToolkitManagerがシーンに在る()
        {
            var managers = Object.FindObjectsByType<XRUIToolkitManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsNotEmpty(managers,
                "XRUIToolkitManager が無いと XRI の UI Toolkit 対応が丸ごと無効になります"
                + "（XRI manual: ui-world-space-ui-toolkit-support）。");
        }

        [Test]
        public void PanelInputConfigurationが入力を横取りしない設定で在る()
        {
            var configs = Object.FindObjectsByType<PanelInputConfiguration>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsNotEmpty(configs, "PanelInputConfiguration が無いと EventSystem が UI Toolkit の入力を邪魔します。");

            foreach (var config in configs)
            {
                var so = new SerializedObject(config);
                Assert.AreEqual((int)PanelInputConfiguration.PanelInputRedirection.Never,
                    so.FindProperty("m_Settings.m_PanelInputRedirection").intValue,
                    "Panel Input Redirection は No input redirection（Never）にします。");
                Assert.IsTrue(so.FindProperty("m_Settings.m_ProcessWorldSpaceInput").boolValue,
                    "ワールド空間のパネルが入力を受けるには Process World Space Input が要ります。");
            }
        }

        [Test]
        public void XRUIInputModuleがUIToolkitのイベントを止めていない()
        {
            var modules = Object.FindObjectsByType<XRUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsNotEmpty(modules, "EventSystem に XR UI Input Module がありません。");

            foreach (var module in modules)
            {
                Assert.IsFalse(module.bypassUIToolkitEvents,
                    $"{module.name}: bypassUIToolkitEvents が true だと UI Toolkit にイベントが届きません。");
            }
        }

        [Test]
        public void 板にPokeの受け口が揃っている()
        {
            foreach (var doc in AllPanelDocuments())
            {
                var go = doc.gameObject;
                Assert.IsNotNull(go.GetComponent<XRSimpleInteractable>(), $"{go.name}: XRSimpleInteractable がありません。");

                var pokeFilter = go.GetComponent<XRPokeFilter>();
                Assert.IsNotNull(pokeFilter, $"{go.name}: XRPokeFilter がありません。");
                Assert.IsNotNull(pokeFilter.pokeCollider, $"{go.name}: XRPokeFilter の Poke Collider が空です。");
                Assert.IsNotNull(pokeFilter.pokeInteractable, $"{go.name}: XRPokeFilter の Interactable が空です。");
            }
        }

        [Test]
        public void 板のコライダーが板と同じ大きさで当たり判定に入る()
        {
            foreach (var doc in AllPanelDocuments())
            {
                var go = doc.gameObject;
                var collider = go.GetComponent<BoxCollider>();
                Assert.IsNotNull(collider, $"{go.name}: BoxCollider がありません。");

                // UI px ではなくローカル単位（px ÷ Pixels Per Unit）。以前は 100 倍だった。
                var expectedWidth = doc.worldSpaceSize.x / KitchenSceneBuilder.PanelPixelsPerUnit;
                var expectedHeight = doc.worldSpaceSize.y / KitchenSceneBuilder.PanelPixelsPerUnit;

                Assert.AreEqual(expectedWidth, collider.size.x, 0.001f, $"{go.name}: コライダーの幅が板と合いません。");
                Assert.AreEqual(expectedHeight, collider.size.y, 0.001f, $"{go.name}: コライダーの高さが板と合いません。");
                Assert.AreEqual(expectedWidth / 2f, collider.center.x, 0.001f, $"{go.name}: コライダーの中心がずれています。");
                Assert.AreEqual(-expectedHeight / 2f, collider.center.y, 0.001f, $"{go.name}: コライダーの中心がずれています。");

                Assert.IsFalse(collider.isTrigger,
                    $"{go.name}: isTrigger を立てると XRSimpleInteractable のコライダー一覧から外れて poke が当たりません。");

                var interactable = go.GetComponent<XRSimpleInteractable>();
                CollectionAssert.Contains(interactable.colliders, collider,
                    $"{go.name}: Interactable のコライダー一覧に板のコライダーが入っていません。");
            }
        }

        [Test]
        public void 板が胸から目線の高さに離して置かれている()
        {
            var positions = AllPanelDocuments().ToDictionary(d => d.name, d => d.transform.position);

            foreach (var pair in positions)
            {
                Assert.Greater(pair.Value.y, 1.0f, $"{pair.Key}: 作業中の手が通る高さに置かない（設計 §7）。");
            }

            // 3枚が同じ場所に重なっていない（原点に積み上がっていた）。
            var pairs = positions.ToList();
            for (var i = 0; i < pairs.Count; i++)
            {
                for (var j = i + 1; j < pairs.Count; j++)
                {
                    Assert.Greater(Vector3.Distance(pairs[i].Value, pairs[j].Value), 0.3f,
                        $"{pairs[i].Key} と {pairs[j].Key} が重なっています。");
                }
            }
        }

        // ---------------------------------------------------------------- 版とアイコン

        [Test]
        public void bundleVersionが目的の版になっている()
        {
            Assert.AreEqual(AndroidPlayerSetup.TargetBundleVersion, PlayerSettings.bundleVersion);
            Assert.Greater(PlayerSettings.Android.bundleVersionCode, 1);
        }

        [Test]
        public void APKの名前に版が入る()
        {
            StringAssert.Contains($"KitchenXR_v{PlayerSettings.bundleVersion}.apk", AndroidBuilder.DefaultOutputPath);
        }

        [Test]
        public void Androidのアイコンが全サイズに割り当てられている()
        {
            foreach (var path in AndroidPlayerSetup.IconPaths())
            {
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(path), $"アイコン画像がありません: {path}");
            }

            var missing = AndroidPlayerSetup.UnassignedIconSlots();
            Assert.IsEmpty(missing, "絵が入っていないアイコン枠: " + string.Join(", ", missing));
        }

        // ---------------------------------------------------------------- helpers

        private static IEnumerable<UIDocument> AllPanelDocuments()
        {
            var docs = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(d => PanelNames.Contains(d.name))
                .ToList();

            Assert.AreEqual(PanelNames.Length, docs.Count,
                "Kitchen.unity の3枚の板が揃っていません: " + string.Join(", ", docs.Select(d => d.name)));
            return docs;
        }

        private static Camera MainCamera()
        {
            var camera = Camera.main ?? Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            Assert.IsNotNull(camera, "Kitchen.unity にカメラがありません。");
            return camera;
        }
    }
}
