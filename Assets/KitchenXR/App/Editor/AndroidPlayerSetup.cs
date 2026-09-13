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
    /// Android の Player Settings のうち機械で決められるものを設定するバッチ用ツール
    /// （結果は `ProjectSettings/ProjectSettings.asset` に入る）。
    ///
    /// WebView（`Assets/TLab/TLabWebView`）の README の要求も揃える: Vulkan で組むなら
    /// OpenGLES も併記（プラグインの一部が GLES API に依存）、Minimum API Level 26 以上、
    /// Internet permission。あわせて OpenXR の「Force Remove Internet Permission」が
    /// 入っていないこと——入っているとビルド時に permission が剥がされる。
    /// ただし `Assets/XR/Settings/` は書き換えず検算だけする
    /// （<see cref="OpenXrRemovesInternetPermission"/>）。
    /// </summary>
    public static class AndroidPlayerSetup
    {
        public const string TargetBundleVersion = "1.1.0";

        /// <summary>`TLabWebView` の README が求める最小 API（Android 8.0）。</summary>
        public const int WebViewMinimumSdk = 26;

        /// <summary>
        /// パススルーカメラの CPU 画像が要る最小 API（Android 12L）。
        /// Horizon OS v74 は Android 12L 基盤なので、上げても実機で落ちる機は無い。
        /// </summary>
        public const int CameraMinimumSdk = 32;

        /// <summary>この構成が要る最小 API（上の2つの厳しい方）。</summary>
        public const int MinimumSupportedSdk = CameraMinimumSdk;

        /// <summary>
        /// OpenXR の設定。読むのが基本だが、カメラ画像だけはここから立てる
        /// （<see cref="ApplyCameraImageSupport"/>。permission が付くかどうかを決めるので、
        /// 手で入れたつもりのまま外れているのが一番困る）。
        /// </summary>
        public const string OpenXrSettingsPath = "Assets/XR/Settings/OpenXRPackageSettings.asset";

        /// <summary>「Meta Quest: Camera (Passthrough)」の feature id（Unity OpenXR: Meta）。</summary>
        public const string CameraFeatureId = "com.unity.openxr.feature.arfoundation-meta-camera";

        public const string AdaptiveForegroundPath = "Assets/KitchenXR/Icons/icon_adaptive_fg.png";
        public const string AdaptiveBackgroundPath = "Assets/KitchenXR/Icons/icon_adaptive_bg.png";
        public const string LegacyIconPath = "Assets/KitchenXR/Icons/icon_legacy.png";

        /// <summary>バッチの入口。版を上げ、アイコンを割り当て、WebView の要件を揃える。</summary>
        public static void Apply()
        {
            ApplyVersion();
            ApplyIcons();
            ApplyWebViewRequirements();
            ApplyCameraImageSupport();

            AssetDatabase.SaveAssets();
            Debug.Log($"[KitchenXR] bundleVersion={PlayerSettings.bundleVersion} " +
                      $"bundleVersionCode={PlayerSettings.Android.bundleVersionCode}");

            foreach (var issue in WebViewRequirementIssues().Concat(CameraRequirementIssues()))
            {
                Debug.LogWarning($"[KitchenXR] {issue}");
            }
        }

        // ---------------------------------------------------------------- カメラ（v1-d）

        /// <summary>
        /// パススルーカメラの CPU 画像を有効にする。立てると Unity OpenXR: Meta のビルド hook が
        /// manifest へ <c>horizonos.permission.HEADSET_CAMERA</c>（と
        /// <c>android.permission.CAMERA</c>・<c>com.oculus.permission.USE_PASSTHROUGH_CAMERA</c>）を
        /// 入れるので、<c>Plugins/Android/AndroidManifest.xml</c> は要らない。
        ///
        /// 触るのは Android 側だけ。型（<c>ARCameraFeature</c>）で引かずに feature id と
        /// <see cref="SerializedObject"/> で引くのは、機種固有の SDK への参照を
        /// <c>Platform/&lt;系&gt;/</c> の外へ出さないため（<c>PlatformIsolationTests</c> の線）。
        /// </summary>
        public static void ApplyCameraImageSupport()
        {
            var changed = false;
            foreach (var so in AndroidCameraFeatures())
            {
                var support = so.FindProperty("m_CameraImageSupport");
                if (support == null || support.boolValue)
                {
                    continue;
                }

                support.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[KitchenXR] OpenXR の Camera Image Support を立てました（HEADSET_CAMERA が manifest に入ります）。");
            }
        }

        /// <summary>Android 側の「Meta Quest: Camera (Passthrough)」が有効で、画像取得も立っているか。</summary>
        public static bool CameraImageSupportEnabled()
        {
            foreach (var so in AndroidCameraFeatures())
            {
                var featureEnabled = so.FindProperty("m_enabled");
                var support = so.FindProperty("m_CameraImageSupport");
                if (support != null && support.boolValue &&
                    (featureEnabled == null || featureEnabled.boolValue))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>カメラ画像が取れる条件（空なら問題なし）。EditMode 試験と Apply の両方が見る。</summary>
        public static List<string> CameraRequirementIssues()
        {
            var issues = new List<string>();

            if (!CameraImageSupportEnabled())
            {
                issues.Add("OpenXR の「Meta Quest: Camera (Passthrough)」の Camera Image Support が"
                           + "立っていません（カメラ画像が取れず、HEADSET_CAMERA も manifest に入りません）。"
                           + $"{nameof(ApplyCameraImageSupport)} を回してください。");
            }

            if ((int)PlayerSettings.Android.minSdkVersion < CameraMinimumSdk)
            {
                issues.Add($"Minimum API Level が {(int)PlayerSettings.Android.minSdkVersion} です"
                           + $"（CPU 画像は Android 12L ＝ {CameraMinimumSdk} 以上）。");
            }

            return issues;
        }

        /// <summary>Android の「Meta Quest: Camera (Passthrough)」の設定（普通は1つ）。</summary>
        private static IEnumerable<SerializedObject> AndroidCameraFeatures()
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(OpenXrSettingsPath))
            {
                if (asset == null || !asset.name.EndsWith("Android", StringComparison.Ordinal))
                {
                    continue;
                }

                var so = new SerializedObject(asset);
                var id = so.FindProperty("featureIdInternal");
                if (id != null && id.stringValue == CameraFeatureId)
                {
                    yield return so;
                }
            }
        }

        // ---------------------------------------------------------------- WebView

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

            // Unity 6 の既定は「HTTP（非暗号）を許さない」（実機ログ「Insecure connection not
            // allowed」）だが、manor は家の LAN に http://192.168.x.y:8789 で居る。
            // LAN の平文は受け入れる（HTTPS にしたら戻す）。
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

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
        /// 読むだけ——`Assets/XR/Settings/` はここからは書き換えず、立っていたら報告する
        /// （Project Settings &gt; XR Plug-in Management &gt; OpenXR の Meta Quest Support で外す）。
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

            if ((int)PlayerSettings.Android.minSdkVersion < WebViewMinimumSdk)
            {
                issues.Add($"Minimum API Level が {(int)PlayerSettings.Android.minSdkVersion} です"
                           + $"（WebView のプラグインは {WebViewMinimumSdk} 以上）。");
            }

            if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
            {
                issues.Add("Allow downloads over HTTP が Always allowed ではありません。"
                           + "manor（http://…:8789）へ繋げません。");
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
                           + "Project Settings で外してください（この設定は書き換えません）。");
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
