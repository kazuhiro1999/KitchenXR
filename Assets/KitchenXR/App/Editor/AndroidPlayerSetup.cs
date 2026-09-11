using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace KitchenXR.App.Editor
{
    /// <summary>
    /// Android の Player Settings のうち、版とアイコンだけを機械で設定する
    /// （結果は `ProjectSettings/ProjectSettings.asset` に入りコミットされる）。
    /// 手で Editor を開かずに済ませるためのバッチ用ツール。
    /// </summary>
    public static class AndroidPlayerSetup
    {
        public const string TargetBundleVersion = "1.0.2";

        public const string AdaptiveForegroundPath = "Assets/KitchenXR/Icons/icon_adaptive_fg.png";
        public const string AdaptiveBackgroundPath = "Assets/KitchenXR/Icons/icon_adaptive_bg.png";
        public const string LegacyIconPath = "Assets/KitchenXR/Icons/icon_legacy.png";

        /// <summary>バッチの入口。版を上げ、アイコンを全サイズに割り当てる。</summary>
        public static void Apply()
        {
            ApplyVersion();
            ApplyIcons();

            AssetDatabase.SaveAssets();
            Debug.Log($"[KitchenXR] bundleVersion={PlayerSettings.bundleVersion} " +
                      $"bundleVersionCode={PlayerSettings.Android.bundleVersionCode}");
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
