using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace KitchenXR.App.Editor
{
    /// <summary>
    /// XR Interaction Toolkit の <c>World Space UI</c> サンプル（UI Toolkit の板の見本）を
    /// <c>Assets/Samples/</c> へ取り込む。自分たちの板の作りと1つずつ突き合わせるための参照用で、
    /// アプリの動きには関わらない（DemoScene は Build Settings に入れない）。
    /// </summary>
    public static class XriSampleImporter
    {
        private const string PackageName = "com.unity.xr.interaction.toolkit";
        private const string SampleName = "World Space UI";

        public static void ImportWorldSpaceUiSample()
        {
            var samples = Sample.FindByPackage(PackageName, string.Empty).ToList();
            if (samples.Count == 0)
            {
                Debug.LogError($"[KitchenXR] {PackageName} のサンプルが見つかりません。");
                return;
            }

            var sample = samples.FirstOrDefault(s => s.displayName == SampleName);
            if (sample.displayName == null)
            {
                Debug.LogError($"[KitchenXR] サンプル '{SampleName}' が見つかりません。" +
                               $"候補: {string.Join(", ", samples.Select(s => s.displayName))}");
                return;
            }

            if (sample.isImported)
            {
                Debug.Log($"[KitchenXR] サンプル '{SampleName}' は既に {sample.importPath} に取り込み済みです。");
                return;
            }

            if (!sample.Import(Sample.ImportOptions.OverridePreviousImports))
            {
                Debug.LogError($"[KitchenXR] サンプル '{SampleName}' の取り込みに失敗しました。");
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[KitchenXR] サンプル '{SampleName}' を {sample.importPath} に取り込みました。");
        }
    }
}
