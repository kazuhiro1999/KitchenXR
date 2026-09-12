using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace KitchenXR.App.Editor
{
    /// <summary>
    /// Android の Player Settings のうち、機械で決められるものを設定する
    /// （結果は `ProjectSettings/ProjectSettings.asset` に入りコミットされる）。
    /// 手で Editor を開かずに済ませるためのバッチ用ツール。
    ///
    /// 2026-09-13（P4）に WebView（主人の `Assets/TLab/TLabWebView`）の要件を足した。
    /// README の要求は3つ:
    ///   - **Vulkan で組むなら OpenGLES も要る**（プラグインの一部が GLES API に依存している。
    ///     README「Vulkanでビルドをする場合はAndroidデバイスがVulkanだけでなくOpenGLESもサポート
    ///     していることが必要」）。Graphics API の一覧に両方を並べる
    ///   - **Minimum API Level 26 以上**（今は 34 なので満たしている）
    ///   - **Internet permission**（YouTube を開くので当然要る）。あわせて OpenXR の
    ///     Meta Quest Support にある「Force Remove Internet Permission」が**入っていない**こと
    ///     ——これが入っていると、上で足した permission がビルド時に剥がされる
    ///
    /// OpenXR の設定（`Assets/XR/Settings/OpenXRPackageSettings.asset`）は主人の持ち物なので
    /// **書き換えない。検算だけする**（<see cref="OpenXrRemovesInternetPermission"/>）。
    /// </summary>
    public static class AndroidPlayerSetup
    {
        public const string TargetBundleVersion = "1.0.11";

        /// <summary>`TLabWebView` の README が求める最小 API（Android 8.0）。</summary>
        public const int MinimumSupportedSdk = 26;

        /// <summary>主人の持ち物。読むだけ。</summary>
        public const string OpenXrSettingsPath = "Assets/XR/Settings/OpenXRPackageSettings.asset";

        public const string AdaptiveForegroundPath = "Assets/KitchenXR/Icons/icon_adaptive_fg.png";
        public const string AdaptiveBackgroundPath = "Assets/KitchenXR/Icons/icon_adaptive_bg.png";
        public const string LegacyIconPath = "Assets/KitchenXR/Icons/icon_legacy.png";

        /// <summary>バッチの入口。版を上げ、アイコンを割り当て、WebView の要件を揃える。</summary>
        public static void Apply()
        {
            ApplyVersion();
            ApplyIcons();
            ApplyWebViewRequirements();

            AssetDatabase.SaveAssets();
            Debug.Log($"[KitchenXR] bundleVersion={PlayerSettings.bundleVersion} " +
                      $"bundleVersionCode={PlayerSettings.Android.bundleVersionCode}");

            foreach (var issue in WebViewRequirementIssues())
            {
                Debug.LogWarning($"[KitchenXR] {issue}");
            }
        }

        // ---------------------------------------------------------------- WebView（P4）

        /// <summary>WebView（`TLabWebView`）が要る Android の設定を揃える。</summary>
        public static void ApplyWebViewRequirements()
        {
            // Vulkan を先頭に置いたまま OpenGLES3 を併記する（README の要件）。
            // 並びは意図的に Vulkan が先: Quest 3 のパススルー（Unity OpenXR: Meta）で
            // 実績があるのはこちらなので、動画のために描画全体の土台を変えない。
            // ——もし実機で WebView の絵が出なければ、この2つの順を入れ替えるのが最初の一手。
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            // YouTube を開くので Internet permission を明示的に立てる
            // （UnityWebRequest を使っていれば自動で付くが、「付いているつもり」を無くす）。
            PlayerSettings.Android.forceInternetPermission = true;

            if ((int)PlayerSettings.Android.minSdkVersion < MinimumSupportedSdk)
            {
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)MinimumSupportedSdk;
            }
        }

        /// <summary>Android の Graphics API の並び（検算用）。</summary>
        public static GraphicsDeviceType[] AndroidGraphicsApis() =>
            PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);

        /// <summary>
        /// OpenXR の Meta Quest Support が Internet permission を剥がす設定になっているか。
        /// **読むだけ**——`Assets/XR/Settings/` は主人の持ち物なのでここからは書き換えない
        /// （立っていたら報告して、主人に Project Settings &gt; XR Plug-in Management &gt; OpenXR の
        /// Meta Quest Support で外してもらう）。
        /// </summary>
        public static bool OpenXrRemovesInternetPermission()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), OpenXrSettingsPath);
            if (!File.Exists(path))
            {
                return false; // 設定が無ければ OpenXR 側は何もしない。
            }

            return File.ReadLines(path)
                .Any(line => line.Trim() == "forceRemoveInternetPermission: 1");
        }

        /// <summary>
        /// WebView が動くための Android の条件が揃っているか（EditMode 試験と Apply の両方が見る）。
        /// 空なら問題なし。
        /// </summary>
        public static List<string> WebViewRequirementIssues()
        {
            var issues = new List<string>();

            var apis = AndroidGraphicsApis();
            if (apis.Contains(GraphicsDeviceType.Vulkan) && !apis.Contains(GraphicsDeviceType.OpenGLES3))
            {
                issues.Add("Graphics API が Vulkan だけです。WebView のプラグインは一部の処理が GLES API に"
                           + "依存しているので OpenGLES3 も併記が要ります（README の NOTICE）。");
            }

            if (apis.Length == 0)
            {
                issues.Add("Android の Graphics API が空です。");
            }

            if ((int)PlayerSettings.Android.minSdkVersion < MinimumSupportedSdk)
            {
                issues.Add($"Minimum API Level が {(int)PlayerSettings.Android.minSdkVersion} です"
                           + $"（WebView のプラグインは {MinimumSupportedSdk} 以上）。");
            }

            if (!PlayerSettings.Android.forceInternetPermission)
            {
                issues.Add("Internet permission（Player Settings > Android > Internet Access = Require）が"
                           + "立っていません。YouTube が開けません。");
            }

            if (OpenXrRemovesInternetPermission())
            {
                issues.Add("OpenXR の Meta Quest Support で「Force Remove Internet Permission」が"
                           + "入っています。ビルド時に Internet permission が剥がされるので、"
                           + "主人が Project Settings で外してください（この設定は書き換えません）。");
            }

            return issues;
        }

        public static void ApplyVersion()
        {
            if (PlayerSettings.bundleVersion != TargetBundleVersion)
            {
                PlayerSettings.bundleVersion = TargetBundleVersion;
                PlayerSettings.Android.bundleVersionCode += 1;
            }
        }

        public static void ApplyIcons()
        {
            var foreground = ImportAsIconTexture(AdaptiveForegroundPath);
            var background = ImportAsIconTexture(AdaptiveBackgroundPath);
            var legacy = ImportAsIconTexture(LegacyIconPath);

            // Android の Adaptive／Round／Legacy をすべて埋める。
            // Adaptive は 2 層（前景・背景）、Round と Legacy は 1 層。
            foreach (var kind in SupportedAndroidIconKinds())
            {
                var layers = MaxLayerCount(kind) >= 2
                    ? new[] { foreground, background }
                    : new[] { legacy };
                SetKind(kind, layers);
            }
        }

        /// <summary>Android が持つアイコンの種類（Adaptive／Round／Legacy）。</summary>
        public static PlatformIconKind[] SupportedAndroidIconKinds() =>
            PlayerSettings.GetSupportedIconKindsForPlatform(BuildTargetGroup.Android);

        private static int MaxLayerCount(PlatformIconKind kind)
        {
            var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            return icons == null || icons.Length == 0 ? 1 : icons.Max(i => i.maxLayerCount);
        }

        private static void SetKind(PlatformIconKind kind, Texture2D[] layers)
        {
            var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            if (icons == null || icons.Length == 0)
            {
                Debug.LogWarning($"[KitchenXR] Android アイコンの種類 {kind} に枠がありません。");
                return;
            }

            foreach (var icon in icons)
            {
                var count = Math.Min(icon.maxLayerCount, layers.Length);
                var textures = new Texture2D[Math.Max(icon.minLayerCount, count)];
                for (var i = 0; i < textures.Length; i++)
                {
                    textures[i] = layers[Math.Min(i, layers.Length - 1)];
                }

                icon.SetTextures(textures);
            }

            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            Debug.Log($"[KitchenXR] Android アイコン {kind}: {icons.Length} サイズに割り当てました。");
        }

        /// <summary>アイコン用のテクスチャ設定（圧縮なし・アルファ有効・ミップなし）に揃えて読み込む。</summary>
        private static Texture2D ImportAsIconTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new Exception($"[KitchenXR] アイコン画像が見つかりません: {path}");
            }

            var changed = false;
            void Set<T>(Func<T> get, Action<T> set, T value)
            {
                if (!EqualityComparer<T>.Default.Equals(get(), value))
                {
                    set(value);
                    changed = true;
                }
            }

            Set(() => importer.textureType, v => importer.textureType = v, TextureImporterType.Default);
            Set(() => importer.textureShape, v => importer.textureShape = v, TextureImporterShape.Texture2D);
            Set(() => importer.alphaSource, v => importer.alphaSource = v, TextureImporterAlphaSource.FromInput);
            Set(() => importer.alphaIsTransparency, v => importer.alphaIsTransparency = v, true);
            Set(() => importer.mipmapEnabled, v => importer.mipmapEnabled = v, false);
            Set(() => importer.npotScale, v => importer.npotScale = v, TextureImporterNPOTScale.None);
            Set(() => importer.isReadable, v => importer.isReadable = v, true);
            Set(() => importer.sRGBTexture, v => importer.sRGBTexture = v, true);
            Set(() => importer.wrapMode, v => importer.wrapMode = v, TextureWrapMode.Clamp);

            var settings = importer.GetDefaultPlatformTextureSettings();
            if (settings.maxTextureSize != 1024 ||
                settings.textureCompression != TextureImporterCompression.Uncompressed ||
                settings.format != TextureImporterFormat.RGBA32)
            {
                settings.maxTextureSize = 1024;
                settings.textureCompression = TextureImporterCompression.Uncompressed;
                settings.format = TextureImporterFormat.RGBA32;
                settings.overridden = true;
                importer.SetPlatformTextureSettings(settings);
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new Exception($"[KitchenXR] アイコン画像を読み込めません: {path}");
            }

            return texture;
        }

        public static IEnumerable<string> IconPaths()
        {
            yield return AdaptiveForegroundPath;
            yield return AdaptiveBackgroundPath;
            yield return LegacyIconPath;
        }

        /// <summary>試験用: すべての Android アイコン枠に絵が入っているか。</summary>
        public static List<string> UnassignedIconSlots()
        {
            var missing = new List<string>();
            foreach (var kind in SupportedAndroidIconKinds())
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var icon in icons)
                {
                    var textures = icon.GetTextures();
                    if (textures == null || textures.Length == 0 || textures.Take(icon.minLayerCount).Any(t => t == null))
                    {
                        missing.Add($"{kind} {icon.width}x{icon.height}");
                    }
                }
            }

            return missing;
        }
    }
}
