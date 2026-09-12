using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace KitchenXR.App.Editor
{
    /// <summary>
    /// UI Toolkit 用の日本語フォント一式を作る。
    ///
    /// `-unity-font-definition` は <c>UnityEngine.TextCore.Text.FontAsset</c> しか受け付けず、
    /// TextMeshPro の <c>TMP_FontAsset</c> を指すと型が合わずに USS の font 指定が丸ごと落ちる
    /// （すべての Label が font=null で描かれない）。そこで同じ TTF から TextCore の FontAsset を
    /// Dynamic（実行時に必要な字だけ焼く）で作り、PanelTextSettings の既定／代替にも据える。
    /// Dynamic にするのは、静的な TMP 版が 8192x8192 の巨大アトラスで APK を膨らませるため。
    /// </summary>
    public static class KitchenFontAssetBuilder
    {
        public const string SourceTtfPath = "Assets/TextMesh Pro/Fonts/NotoSansJP-Regular.ttf";
        public const string FontAssetPath = "Assets/KitchenXR/Presentation/UI/Fonts/NotoSansJP-Regular UITK.asset";
        public const string TextSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenTextSettings.asset";

        /// <summary>バッチから叩く入口。</summary>
        public static void Build()
        {
            var fontAsset = CreateOrLoadFontAsset();
            CreateOrLoadTextSettings(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var guid = AssetDatabase.AssetPathToGUID(FontAssetPath);
            Debug.Log($"[KitchenXR] UI Toolkit フォント資産: {FontAssetPath} (guid={guid})");
        }

        public static FontAsset CreateOrLoadFontAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (existing != null)
            {
                return existing;
            }

            var ttf = AssetDatabase.LoadAssetAtPath<Font>(SourceTtfPath);
            if (ttf == null)
            {
                throw new FileNotFoundException($"元の TTF が見つかりません: {SourceTtfPath}");
            }

            // 90pt でサンプリングした SDF、1024 角のアトラス。Dynamic なので使う字だけ焼かれる。
            var fontAsset = FontAsset.CreateFontAsset(
                ttf,
                samplingPointSize: 90,
                atlasPadding: 9,
                renderMode: GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            fontAsset.name = Path.GetFileNameWithoutExtension(FontAssetPath);

            Directory.CreateDirectory(Path.GetDirectoryName(FontAssetPath)!);
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            // アトラス材質とテクスチャを副資産として同じファイルに入れる（TMP の作法と同じ）。
            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            if (fontAsset.atlasTextures != null)
            {
                for (var i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    var tex = fontAsset.atlasTextures[i];
                    if (tex == null)
                    {
                        continue;
                    }

                    tex.name = $"{fontAsset.name} Atlas {i}";
                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        public static PanelTextSettings CreateOrLoadTextSettings(FontAsset fontAsset)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelTextSettings>();
                settings.name = Path.GetFileNameWithoutExtension(TextSettingsPath);
                Directory.CreateDirectory(Path.GetDirectoryName(TextSettingsPath)!);
                AssetDatabase.CreateAsset(settings, TextSettingsPath);
            }

            // USS の指定が何かの拍子に外れても、既定／代替フォントとして日本語が出るようにする。
            var so = new SerializedObject(settings);
            var defaultFont = so.FindProperty("m_DefaultFontAsset");
            if (defaultFont != null)
            {
                defaultFont.objectReferenceValue = fontAsset;
            }

            var fallbacks = so.FindProperty("m_FallbackFontAssets");
            if (fallbacks != null)
            {
                fallbacks.arraySize = 1;
                fallbacks.GetArrayElementAtIndex(0).objectReferenceValue = fontAsset;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return settings;
        }

        /// <summary>PanelSettings に TextSettings とテーマを結ぶ。</summary>
        public static void ApplyToPanelSettings(PanelSettings panelSettings, PanelTextSettings textSettings)
        {
            var so = new SerializedObject(panelSettings);
            var prop = so.FindProperty("textSettings");
            if (prop != null)
            {
                prop.objectReferenceValue = textSettings;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(panelSettings);
            }
        }

        public static List<string> MissingAssets()
        {
            var missing = new List<string>();
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath) == null)
            {
                missing.Add(FontAssetPath);
            }

            if (AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath) == null)
            {
                missing.Add(TextSettingsPath);
            }

            return missing;
        }
    }
}
