using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace KitchenXR.App.Editor
{
    /// <summary>
    /// 検算用の Android ビルド。`unity build` の --execute-method から叩く。
    /// 出力名は <c>Build/KitchenXR_v&lt;bundleVersion&gt;.apk</c>
    /// （`-buildOutput` が渡されたときだけそちらを優先する）。
    /// </summary>
    public static class AndroidBuilder
    {
        public const string BuildDirectory = "Build";

        public static string DefaultOutputPath =>
            Path.Combine(BuildDirectory, $"KitchenXR_v{PlayerSettings.bundleVersion}.apk").Replace('\\', '/');

        public static void PerformBuild()
        {
            var outputPath = GetCommandLineArg("-buildOutput") ?? DefaultOutputPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"[KitchenXR] Android ビルドが失敗しました: {report.summary.result}" +
                                     $"（エラー {report.summary.totalErrors} 件）");
            }

            UnityEngine.Debug.Log($"[KitchenXR] APK: {outputPath}（{report.summary.totalSize / (1024 * 1024)} MB）");
        }

        private static string GetCommandLineArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
