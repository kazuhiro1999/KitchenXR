using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KitchenXR.App.Editor;
using KitchenXR.Presentation;
using KitchenXR.Presentation.Video;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 実機（Quest 3）で踏んだ3点の再発を止める検算。
    ///   (1) 文字が1つも描かれない   → フォントが解決されているか
    ///   (2) パススルーが効かない     → テンプレートの環境が残っていないか・カメラ背景が透明か
    ///   (3) 指がすり抜ける           → XRI のワールド空間 UI Toolkit の受け口が揃っているか
    /// おまけ: アイコンと版。
    /// </summary>
    public class KitchenSceneIntegrityTests
    {
        private const string ThemeUssPath = "Assets/KitchenXR/Presentation/UI/theme.uss";
        private static readonly string[] PanelNames =
            { "RecipePanel", "IngredientsPanel", "TimerPanel", "VideoPanel" };

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
            // `-unity-font-definition` の指す資産の型が合わないと、
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

                // UI px ではなくローカル単位（px ÷ Pixels Per Unit）。
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

        /// <summary>
        /// 「触ると色は変わるのにボタンが押せない」を起こす当人。
        /// コライダーは左上原点の約束で置いているのに、UIDocument の原点が既定のままだと
        /// 板の矩形とコライダーが半分ずれる。当たってはいるので Interactable のホバー
        /// （色と振動）は効くが、当たり点を板のローカル座標へ写すと文字の外に落ちるため、
        /// UI Toolkit 側は Button を1度も掴めない。
        /// XRI の World Space UI サンプルの板は4枚とも TopLeft。
        /// </summary>
        [Test]
        public void 板の原点が左上でコライダーと揃っている()
        {
            foreach (var doc in AllPanelDocuments())
            {
                Assert.AreEqual(WorldSpacePanelFactory.PanelPivot, doc.pivot,
                    $"{doc.name}: UIDocument.pivot が {WorldSpacePanelFactory.PanelPivot} ではありません"
                    + "（既定のままだとコライダーと板が半分ずれて、ボタンが押せません）。");

                var collider = doc.GetComponent<BoxCollider>();
                Assert.IsNotNull(collider);

                var expectedCenter = WorldSpacePanelFactory.ColliderCenterFor(
                    doc.worldSpaceSize.x, doc.worldSpaceSize.y);

                Assert.AreEqual(expectedCenter.x, collider.center.x, 0.001f,
                    $"{doc.name}: pivot とコライダーの中心が食い違っています。");
                Assert.AreEqual(expectedCenter.y, collider.center.y, 0.001f,
                    $"{doc.name}: pivot とコライダーの中心が食い違っています。");
            }
        }

        /// <summary>
        /// 「触っても押せない」のもう一つの原因。指が当たり判定から出ると XRPokeInteractor は
        /// 掴みを手放すので、薄い板だと押し込んだ指がすぐ裏へ抜けて Button の clicked が
        /// 発火しない。箱は裏側にだけ十分な奥行きを持たせる（表の面は板のまま）。
        /// </summary>
        [Test]
        public void 板の当たり判定が裏側に十分な奥行きを持つ()
        {
            foreach (var doc in AllPanelDocuments())
            {
                var collider = doc.GetComponent<BoxCollider>();
                Assert.IsNotNull(collider);

                Assert.AreEqual(WorldSpacePanelFactory.PanelColliderDepth, collider.size.z, 0.001f,
                    $"{doc.name}: 当たり判定の奥行きが足りないと、押し込んだ指が抜けてクリックが落ちます。");

                // 表の面が板と同じ位置（＝手前に張り出していない）こと。
                var front = collider.center.z - collider.size.z / 2f;
                Assert.AreEqual(0f, front, 0.001f,
                    $"{doc.name}: 当たり判定が板より手前に張り出しています（触れる前に反応してしまう）。");
            }
        }

        [Test]
        public void 板が胸から目線の高さに離して置かれている()
        {
            var positions = AllPanelDocuments().ToDictionary(d => d.name, d => d.transform.position);

            foreach (var pair in positions)
            {
                Assert.Greater(pair.Value.y, 1.0f, $"{pair.Key}: 作業中の手が通る高さに置かない。");
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

        // ---------------------------------------------------------------- レシピを選ぶ板

        /// <summary>
        /// 起動時はレシピの板の場所に一覧を出す。同じ場所に同じ寸法で重ねて置き、
        /// 出し入れは <see cref="PanelVisibility"/> が行う
        /// （<c>GameObject.SetActive</c> だと UIDocument が rootVisualElement を作り直して、
        /// 各パネルが Awake で掴んだ要素の参照が死ぬ）。
        /// </summary>
        [Test]
        public void レシピを選ぶ板がレシピの板と同じ場所にある()
        {
            var list = Object.FindFirstObjectByType<RecipeListPanel>(FindObjectsInactive.Include);
            Assert.IsNotNull(list, "Kitchen.unity にレシピを選ぶ板（RecipeListPanel）がありません。");

            var recipe = Object.FindFirstObjectByType<RecipePanel>(FindObjectsInactive.Include);
            Assert.IsNotNull(recipe, "Kitchen.unity にレシピの板がありません。");

            Assert.Less(Vector3.Distance(list.transform.position, recipe.transform.position), 0.01f,
                "起動時はレシピの板の位置に一覧を出します。");

            var doc = list.GetComponent<UIDocument>();
            Assert.IsNotNull(doc, "RecipeListPanel に UIDocument がありません。");
            Assert.AreEqual(recipe.GetComponent<UIDocument>().worldSpaceSize, doc.worldSpaceSize,
                "入れ替わる2枚の寸法が違うと、切り替えのたびに板の大きさが変わります。");

            Assert.IsNotNull(list.GetComponent<XRPokeFilter>(), "RecipeListPanel に XRPokeFilter がありません。");
            Assert.IsNotNull(list.GetComponent<XRSimpleInteractable>(),
                "RecipeListPanel に XRSimpleInteractable がありません（行が指で押せません）。");
        }

        [Test]
        public void Bootstrapに一覧の板が挿さっている()
        {
            var bootstrap = Object.FindFirstObjectByType<KitchenXR.App.Bootstrap>(FindObjectsInactive.Include);
            Assert.IsNotNull(bootstrap, "Kitchen.unity に Bootstrap がありません。");

            var so = new SerializedObject(bootstrap);
            var listPanel = so.FindProperty("_recipeListPanel");
            Assert.IsNotNull(listPanel, "Bootstrap に _recipeListPanel の欄がありません。");
            Assert.IsNotNull(listPanel.objectReferenceValue,
                "Bootstrap にレシピを選ぶ板が挿さっていません（起動しても一覧が出ません）。");
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

        // ---------------------------------------------------------------- 動画の板

        /// <summary>
        /// 4枚目の板が在り、TLab の <c>YoutubePlayer</c> の実体を抱えていること。
        /// 実体は眠っているのが正しい（起こすのは Android の実機だけ。Editor では札を出す）。
        /// </summary>
        [Test]
        public void 動画の板にYoutubePlayerが入っている()
        {
            var videoPanel = Object.FindFirstObjectByType<VideoPanel>(FindObjectsInactive.Include);
            Assert.IsNotNull(videoPanel, "Kitchen.unity に動画の板（VideoPanel）がありません。");

            var so = new SerializedObject(videoPanel);
            var playerRoot = so.FindProperty("_playerRoot").objectReferenceValue as GameObject;
            Assert.IsNotNull(playerRoot,
                $"動画の板に {YoutubePlayerBridge.PlayerPrefabPath} の実体が挿さっていません。");

            Assert.AreEqual(YoutubePlayerBridge.PlayerObjectName, playerRoot.name,
                "youtube.html が unitySendMessage でこの名前へ返してくるので、名前は変えられません。");
            Assert.IsFalse(playerRoot.activeSelf,
                "WebView は Android のプラグイン。起こすのは実機だけなので、シーンでは眠らせておきます。");
            Assert.AreSame(videoPanel.transform, playerRoot.transform.parent,
                "絵は板の中に置きます（板が動けば絵も動く）。");
        }

        /// <summary>
        /// 16:9 で窓の幅 50cm。板は「左に窓・右に一覧」なので、板の幅は窓 ＋ 間 ＋ 一覧 で決まる。
        /// VideoPanel.uss の <c>.video-list-block</c> の <c>width</c>／<c>margin-left</c> が
        /// この3つと食い違うと窓が 50cm でなくなるので、定数の足し算が合っていることを縛る
        /// （実測は PlayMode の VideoPanelTests）。
        /// </summary>
        [Test]
        public void 動画の板の寸法が16対9の窓に合っている()
        {
            var doc = AllPanelDocuments().First(d => d.name == "VideoPanel");
            Assert.AreEqual(VideoPanel.LandscapeWidthUnits, doc.worldSpaceSize.x, 0.01f);
            Assert.AreEqual(VideoPanel.LandscapeHeightUnits, doc.worldSpaceSize.y, 0.01f);

            Assert.AreEqual(
                VideoPanel.LandscapeWindowWidthUnits + VideoPanel.ListGapUnits + VideoPanel.ListWidthUnits,
                VideoPanel.LandscapeWidthUnits, 0.01f,
                "板の幅が「窓 ＋ 間 ＋ 一覧」になっていません。");

            var windowCm = VideoPanel.LandscapeWindowWidthUnits * 0.2f;
            Assert.AreEqual(50f, windowCm, 0.5f, "動画の窓の幅が 50cm ではありません（設計 P4）。");

            // 9:16（ショーツ）は窓の高さを 36cm に取り、幅はその 9:16。
            Assert.AreEqual(36f, VideoPanel.PortraitWindowHeightUnits * 0.2f, 0.5f,
                "9:16 の窓の高さが 36cm ではありません。");
            Assert.AreEqual(
                VideoPanel.PortraitWindowHeightUnits * 9f / 16f, VideoPanel.PortraitWindowWidthUnits, 1f,
                "9:16 の窓の幅が高さの 9:16 になっていません。");
            Assert.AreEqual(
                VideoPanel.PortraitWindowWidthUnits + VideoPanel.ListGapUnits + VideoPanel.ListWidthUnits,
                VideoPanel.PortraitWidthUnits, 0.01f,
                "9:16 の板の幅が「窓 ＋ 間 ＋ 一覧」になっていません。");
        }

        /// <summary>
        /// 一覧の寸法が USS と合っていること。
        /// C# の定数と <c>VideoPanel.uss</c> は別の場所にあり、片方だけ直すと
        /// 窓の幅が静かに 50cm から外れる——数字そのものを突き合わせる。
        /// </summary>
        [Test]
        public void 動画の一覧の幅がUSSと合っている()
        {
            var uss = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
                "Assets/KitchenXR/Presentation/UI/VideoPanel.uss"));

            StringAssert.Contains($"width: {VideoPanel.ListWidthUnits:0}px", uss,
                "VideoPanel.uss の .video-list-block の幅が VideoPanel.ListWidthUnits と違います。");
            StringAssert.Contains($"margin-left: {VideoPanel.ListGapUnits:0}px", uss,
                "VideoPanel.uss の .video-list-block の間隔が VideoPanel.ListGapUnits と違います。");
        }

        /// <summary>
        /// 指先の光る点が左右の Poke Interactor に付いていること。
        /// rig を差し替えたときに黙って 0 個になるのを防ぐ。
        /// </summary>
        [Test]
        public void 指先カーソルが左右のPokeInteractorに付いている()
        {
            var pokes = Object.FindObjectsByType<XRPokeInteractor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.GreaterOrEqual(pokes.Length, 2,
                "rig に Poke Interactor が2つ（左右の手）ありません。");

            foreach (var poke in pokes)
            {
                var cursor = poke.GetComponentInChildren<FingertipCursor>(true);
                Assert.IsNotNull(cursor,
                    $"{poke.name} に指先カーソル（FingertipCursor）がありません"
                    + "（奥行きの距離感がつかめなくなります）。");

                var so = new SerializedObject(cursor);
                Assert.AreSame(poke, so.FindProperty("_interactor").objectReferenceValue,
                    "指先カーソルに Poke Interactor が挿さっていません（指先の位置が読めません）。");
            }
        }

        [Test]
        public void 動画の一覧の見本が同梱されている()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Assets/StreamingAssets/media.json");
            Assert.IsTrue(File.Exists(path), "Assets/StreamingAssets/media.json がありません（一覧が空になります）。");
        }

        // ---------------------------------------------------------------- Android（WebView の要件）

        /// <summary>
        /// WebView が実機で動くための Android の条件（`TLabWebView` の README）。
        /// 落ちたときは <see cref="AndroidPlayerSetup.ApplyWebViewRequirements"/> を回せば直る
        /// ——ただし OpenXR の「Force Remove Internet Permission」だけは手で外す。
        /// </summary>
        [Test]
        public void WebViewのためのAndroid設定が揃っている()
        {
            var issues = AndroidPlayerSetup.WebViewRequirementIssues();
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }

        [Test]
        public void AndroidのGraphicsAPIにVulkanとOpenGLES3が両方ある()
        {
            var apis = AndroidPlayerSetup.AndroidGraphicsApis();
            CollectionAssert.Contains(apis, GraphicsDeviceType.Vulkan);
            CollectionAssert.Contains(apis, GraphicsDeviceType.OpenGLES3,
                "TLabWebView は一部の処理が GLES API に依存しています（README の NOTICE）。"
                + "Vulkan で組むなら OpenGLES3 も並べること。");
        }

        [Test]
        public void 最小APIレベルが足りていてInternetPermissionが立っている()
        {
            Assert.GreaterOrEqual((int)PlayerSettings.Android.minSdkVersion, AndroidPlayerSetup.MinimumSupportedSdk,
                $"WebView は {AndroidPlayerSetup.WebViewMinimumSdk} 以上、"
                + $"パススルーカメラの CPU 画像は {AndroidPlayerSetup.CameraMinimumSdk} 以上を要ります。");
            Assert.IsTrue(PlayerSettings.Android.forceInternetPermission,
                "YouTube を開くので Internet permission が要ります。");
        }

        // ---------------------------------------------------------------- Android（カメラの要件）

        /// <summary>
        /// パススルーカメラ（v1-d 段 (a)）の設定。落ちたときは
        /// <see cref="AndroidPlayerSetup.ApplyCameraImageSupport"/> を回せば直る。
        ///
        /// Camera Image Support は「絵が取れるか」だけでなく
        /// <c>horizonos.permission.HEADSET_CAMERA</c> が manifest に入るかも決める
        /// （Unity OpenXR: Meta 2.5.0 以降は opt-in したときだけ入れる）。
        /// 立て忘れると実機で権限のダイアログすら出ない。
        /// </summary>
        [Test]
        public void パススルーカメラのAndroid設定が揃っている()
        {
            var issues = AndroidPlayerSetup.CameraRequirementIssues();
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }

        [Test]
        public void CameraImageSupportが立っている()
        {
            Assert.IsTrue(AndroidPlayerSetup.CameraImageSupportEnabled(),
                "OpenXR の「Meta Quest: Camera (Passthrough)」の Camera Image Support が立っていません"
                + "（HEADSET_CAMERA が manifest に入らず、実機で権限のダイアログが出ません）。");
        }

        /// <summary>
        /// OpenXR の Meta Quest Support の「Force Remove Internet Permission」は検算だけ
        /// （`Assets/XR/Settings/` は書き換えない）。
        /// </summary>
        [Test]
        public void OpenXRがInternetPermissionを剥がさない()
        {
            Assert.IsFalse(AndroidPlayerSetup.OpenXrRemovesInternetPermission(),
                "OpenXR の Meta Quest Support で「Force Remove Internet Permission」が入っています。"
                + "Project Settings > XR Plug-in Management > OpenXR で外してください。");
        }

        // ---------------------------------------------------------------- 配置モード

        /// <summary>
        /// 板を置き直す仕掛け一式がシーンに在ること。配置の操作は全部 手のひらメニューにあり、
        /// 保存もやめるもこの板にしか無いので、無ければ配置モードから出られない＝必須にする。
        /// </summary>
        [Test]
        public void 配置モードの仕掛けがシーンに在る()
        {
            var placement = Object.FindFirstObjectByType<PanelPlacement>(FindObjectsInactive.Include);
            Assert.IsNotNull(placement, "Kitchen.unity に PanelPlacement がありません（板の位置を覚えられません）。");

            var so = new SerializedObject(placement);
            Assert.IsNotNull(so.FindProperty("_originTransform").objectReferenceValue,
                "控え（panels.json）の基準になる XR Origin が挿さっていません（相対で覚えられません）。");

            var menu = Object.FindFirstObjectByType<PlacementMenuPanel>(FindObjectsInactive.Include);
            Assert.IsNotNull(menu,
                "手のひらメニューがありません。配置の「保存」「やめる」が押せず、配置モードから出られません。");

            var doc = menu.GetComponent<UIDocument>();
            Assert.IsNotNull(doc, "手のひらメニューに UIDocument がありません。");
            Assert.IsNotNull(menu.GetComponent<XRSimpleInteractable>(),
                "手のひらメニューに XRSimpleInteractable がありません（指で押せません）。");
            Assert.AreEqual(WorldSpacePanelFactory.PanelPivot, doc.pivot,
                "手のひらメニューの原点が左上でないと、板とコライダーが半分ずれます。");
        }

        /// <summary>
        /// 配置の操作が全部 手のひらメニューの uxml に在ること。
        /// </summary>
        [Test]
        public void 配置の操作が手のひらメニューに揃っている()
        {
            var text = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
                "Assets/KitchenXR/Presentation/UI/PlacementMenu.uxml"));

            foreach (var name in new[]
                     {
                         "placementButton", "saveButton", "undoButton", "recallButton", "cancelButton",
                         "hintLabel", PlacementMenuPanel.IdleGroupName, PlacementMenuPanel.PlacingGroupName,
                     })
            {
                StringAssert.Contains($"name=\"{name}\"", text,
                    $"PlacementMenu.uxml に {name} がありません（配置モードから出られなくなります）。");
            }
        }

        /// <summary>
        /// 動画の板だけが Video の Interaction Layer を名乗ること（調理中もレイで操作させるため）。
        /// Default を落としていないことも一緒に見る——落とすとポークも配置の掴みも効かなくなる。
        /// </summary>
        [Test]
        public void 動画の板だけがVideoの層を名乗る()
        {
            var videoBit = 1 << CookingModeInputGate.VideoInteractionLayer;

            foreach (var doc in AllPanelDocuments())
            {
                var interactable = doc.GetComponent<XRSimpleInteractable>();
                Assert.IsNotNull(interactable, $"{doc.name} に XRSimpleInteractable がありません。");

                var layers = (int)interactable.interactionLayers;
                var isVideo = doc.name == "VideoPanel";

                Assert.AreEqual(isVideo, (layers & videoBit) != 0,
                    isVideo
                        ? "動画の板が Video の層を名乗っていません（調理中にレイで操作できません）。"
                        : $"{doc.name} が Video の層を名乗っています（調理中にレイが当たってしまいます）。");

                Assert.AreNotEqual(0, layers & 1,
                    $"{doc.name} が Default の層を外しています（ポークも配置の掴みも効かなくなります）。");
            }
        }

        /// <summary>
        /// 「レイを分ける」ための2つの層が、設定の側に名前付きで在ること。
        /// どちらも番号で使うので、名前が消えたり別の意味で使い回されたりすると黙って壊れる。
        /// </summary>
        [Test]
        public void レイを分ける層が設定に在る()
        {
            Assert.AreEqual(CookingModeInputGate.OffRayPhysicsLayerName,
                LayerMask.LayerToName(CookingModeInputGate.OffRayPhysicsLayer),
                $"物理層 {CookingModeInputGate.OffRayPhysicsLayer} 番が別のものになっています"
                + "（調理中にレイがレシピの板へ届いてしまいます）。");

            // XRI の Interaction Layer は InteractionLayerSettings（Resources から読む）が持つ。
            var settings = Resources.Load("InteractionLayerSettings");
            Assert.IsNotNull(settings, "InteractionLayerSettings が Resources にありません。");

            var names = new SerializedObject(settings).FindProperty("m_LayerNames");
            Assert.AreEqual("Video",
                names.GetArrayElementAtIndex(CookingModeInputGate.VideoInteractionLayer).stringValue,
                $"Interaction Layer {CookingModeInputGate.VideoInteractionLayer} 番が Video ではありません"
                + "（動画の板を調理中にレイで操作できません）。");
        }

        [Test]
        public void Bootstrapに配置モードの板が挿さっている()
        {
            var bootstrap = Object.FindFirstObjectByType<KitchenXR.App.Bootstrap>(FindObjectsInactive.Include);
            Assert.IsNotNull(bootstrap, "Kitchen.unity に Bootstrap がありません。");

            var so = new SerializedObject(bootstrap);
            Assert.IsNotNull(so.FindProperty("_panelPlacement").objectReferenceValue,
                "Bootstrap に PanelPlacement が挿さっていません（起動しても板の位置が戻りません）。");
            Assert.IsNotNull(so.FindProperty("_placementMenuPanel").objectReferenceValue,
                "Bootstrap に手元のメニューが挿さっていません（配置モードから出られません）。");
            Assert.IsNotNull(so.FindProperty("_wristMenu").objectReferenceValue,
                "Bootstrap に手首のメニューが挿さっていません。"
                + "レシピ／一覧の板の「配置」から入ったときにメニューが開かず、配置モードから出られません。");
        }

        // ---------------------------------------------------------------- 手首の釦

        /// <summary>
        /// 手首の釦が左右に1つずつ在り、レイの相手にならないこと
        /// （理由は <see cref="WristMenu"/> に書いた）。
        ///
        /// 物理層 8 番 "Kitchen Panel Off Ray" は Ray の <c>raycastMask</c>（0/5/31）から外れ、
        /// <c>Physics.DefaultRaycastLayers</c> には入ったまま——つまり
        /// 指では押せるが遠くからレイでは押せない。手首の釦にちょうどよい性質。
        /// </summary>
        [Test]
        public void 手首の釦が左右に在りレイの相手にならない()
        {
            var wristMenu = Object.FindFirstObjectByType<WristMenu>(FindObjectsInactive.Include);
            Assert.IsNotNull(wristMenu,
                "Kitchen.unity に WristMenu がありません（メニューの出し入れができません）。");

            var buttons = Object.FindObjectsByType<WristMenuButton>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(2, buttons.Length,
                "手首の釦が左右に1つずつ在りません: " + string.Join(", ", buttons.Select(b => b.name)));

            foreach (var button in buttons)
            {
                Assert.AreEqual(CookingModeInputGate.OffRayPhysicsLayer, button.gameObject.layer,
                    $"{button.name} がレイの届く層に載っています（遠くから誤って押せてしまいます）。");

                var doc = button.GetComponent<UIDocument>();
                Assert.IsNotNull(doc, $"{button.name} に UIDocument がありません。");
                Assert.AreEqual(WorldSpacePanelFactory.PanelPivot, doc.pivot,
                    $"{button.name} の原点が左上でないと、板とコライダーが半分ずれます。");
                Assert.IsNotNull(button.GetComponent<XRSimpleInteractable>(),
                    $"{button.name} に XRSimpleInteractable がありません（指で押せません）。");
            }

            var text = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
                "Assets/KitchenXR/Presentation/UI/WristToggle.uxml"));
            StringAssert.Contains($"name=\"{WristMenuButton.ButtonName}\"", text,
                "WristToggle.uxml に釦がありません（メニューを出せなくなります）。");
        }

        /// <summary>
        /// XRI の <c>HandMenu</c>（手のひらの向きで出し入れする仕掛け）が残っていないこと
        /// ——手を横に向けただけで出てしまうのでやめた仕掛けが、シーンを組み直したときに
        /// 黙って戻らないように。
        /// </summary>
        [Test]
        public void 手のひらの向きで出るメニューが残っていない()
        {
            var offenders = Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(b => b != null && b.GetType().Name == "HandMenu")
                .Select(b => b.name)
                .ToList();

            Assert.IsEmpty(offenders,
                "XRI の HandMenu が残っています（手の向きでメニューが出てしまいます）: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// レイの線の出し入れ（<see cref="RayLineVisibility"/>）が
        /// 4つの Near-Far Interactor 全部に付いていること。
        /// 手だけに付けてコントローラを忘れる、を止める。
        /// </summary>
        [Test]
        public void レイの線の出し入れが全部のRayに付いている()
        {
            var gate = Object.FindFirstObjectByType<CookingModeInputGate>(FindObjectsInactive.Include);
            Assert.IsNotNull(gate, "Kitchen.unity に CookingModeInputGate がありません。");

            var interactors = new SerializedObject(gate).FindProperty("_rayLikeInteractors");
            Assert.Greater(interactors.arraySize, 0,
                "Ray の Interactor が1つも挿さっていません（rig の組み立てが変わった可能性）。");

            for (var i = 0; i < interactors.arraySize; i++)
            {
                var interactor = interactors.GetArrayElementAtIndex(i).objectReferenceValue as Component;
                Assert.IsNotNull(interactor, $"_rayLikeInteractors[{i}] が空です。");

                var visibility = interactor.GetComponent<RayLineVisibility>();
                Assert.IsNotNull(visibility,
                    $"{interactor.name} にレイの線の出し入れが付いていません（線が出っぱなしになります）。");

                // 線と描き手は Interactor 本体ではなく `LineVisual` という子に載っている。
                // 挿し先が空でも component は在るので、ここまで見ないと黙って効かない。
                var so = new SerializedObject(visibility);
                Assert.IsNotNull(so.FindProperty("_lineRenderer").objectReferenceValue,
                    $"{interactor.name} の LineRenderer が挿さっていません（線を消せません）。");
                Assert.IsNotNull(so.FindProperty("_lineVisual").objectReferenceValue,
                    $"{interactor.name} の線の描き手（CurveVisualController）が挿さっていません。");
            }
        }

        /// <summary>
        /// 「配置」の釦が両方の板（調理中のレシピの板と、起動直後の一覧の板）にあること。
        /// 手のひらメニューを出すのに手を返すのが面倒な場面（両手が塞がっている等）の入り口。
        /// 出口は手のひらメニューにしか無いので、こちらは入り口の控えという位置付け。
        /// </summary>
        [Test]
        public void 配置の釦がレシピと一覧の板にある()
        {
            foreach (var uxml in new[]
                     {
                         "Assets/KitchenXR/Presentation/UI/RecipePanel.uxml",
                         "Assets/KitchenXR/Presentation/UI/RecipeListPanel.uxml",
                     })
            {
                var text = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), uxml));
                StringAssert.Contains("name=\"placementButton\"", text,
                    $"{Path.GetFileName(uxml)} に「配置」の釦がありません。");
            }
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
