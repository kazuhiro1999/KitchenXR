using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace KitchenXR.App.Editor
{
    /// <summary>
    /// 検算用の Android ビルド（ROADMAP §3・PROTOTYPE §5「検算」）。
    /// `unity build` の --execute-method から叩く。-buildOutput を自分で読んで honor する
    /// （unity-cli skill: 「--execute-method の場合は自分の方法で --output-path を守る」）。
    /// </summary>
    public static class AndroidBuilder
    {
        public static void PerformBuild()
        {
            var outputPath = GetCommandLineArg("-buildOutput") ?? "Build/KitchenXR.apk";
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
