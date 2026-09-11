using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 「機種・SDK 固有の呼び出しは Platform/&lt;系&gt;/ の中だけ」（設計 §4.2）の検算。
    /// UnityEngine.XR.ARFoundation・OVR・PXR への言及が Platform/ フォルダの外の .cs に
    /// 現れたらこの試験が落ちる。
    /// </summary>
    public class PlatformIsolationTests
    {
        private static readonly string[] VendorMarkers = { "UnityEngine.XR.ARFoundation", "OVR", "PXR" };

        /// <summary>
        /// 主人の WebView SDK（`Assets/TLab/`）の印。P4 で足した規則
        /// （主人 2026-09-13「`TLab.Android.WebView` への参照は `Presentation/Video/` の中だけ」）。
        /// </summary>
        private static readonly string[] WebViewMarkers = { "TLab.Android.WebView", "TLabWebView", "TLabVKeyborad" };

        private static string KitchenXrRoot => Path.Combine(Application.dataPath, "KitchenXR");

        [Test]
        public void 機種固有の呼び出しはPlatformフォルダの外に無い()
        {
            var violations = new List<string>();

            foreach (var file in Directory.EnumerateFiles(KitchenXrRoot, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');

                // Platform/ 配下（ArFoundation 実装が置かれる場所）と、この試験自身は対象外。
                if (normalized.Contains("/Platform/") || normalized.Contains("/Tests/"))
                {
                    continue;
                }

                var content = File.ReadAllText(file);
                foreach (var marker in VendorMarkers)
                {
                    if (content.Contains(marker))
                    {
                        violations.Add($"{normalized} に '{marker}' への言及があります");
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        /// <summary>
        /// 主人の WebView SDK に触れてよいのは <c>Presentation/Video/</c> だけ（P4 の約束）。
        ///
        /// WebView は Android にしか無い（`Assets/TLab/TLabWebView/Plugins/Android`）。
        /// 呼び出しが板の外へ散ると、PICO・WebXR へ差し替えるときに追い切れなくなるし、
        /// Editor で動かないコードが増える。動画の板は <c>IVideoPlayer</c> の口だけを見る。
        /// </summary>
        [Test]
        public void WebViewへの参照はPresentationVideoの中だけにある()
        {
            var violations = new List<string>();

            foreach (var file in Directory.EnumerateFiles(KitchenXrRoot, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');

                // 包む場所（Presentation/Video/）と、この試験自身は対象外。
                if (normalized.Contains("/Presentation/Video/") || normalized.Contains("/Tests/"))
                {
                    continue;
                }

                // 見るのは**コードだけ**。注釈で SDK の名前を説明するのは違反ではない
                // （例: AndroidPlayerSetup は「なぜ OpenGLES3 も並べるのか」を README の
                //  文言ごと書き残している。それを禁じると理由が失われる）。
                var content = string.Join("\n", CodeLines(file));
                foreach (var marker in WebViewMarkers)
                {
                    if (content.Contains(marker))
                    {
                        violations.Add($"{normalized} に '{marker}' への言及があります"
                                       + "（WebView は Presentation/Video/ の中だけで包むこと）");
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        /// <summary>
        /// 注釈（<c>//</c>・<c>/* */</c>・<c>///</c>）を落とした行だけを返す、粗い切り分け。
        /// 「参照しているか」を見たいので、説明のための言及は数えない。
        /// 完全な字句解析ではない（文字列の中の <c>//</c> も注釈と見なす）が、
        /// この規則を守らせるにはこれで足りる。
        /// </summary>
        private static IEnumerable<string> CodeLines(string file)
        {
            var inBlock = false;

            foreach (var raw in File.ReadLines(file))
            {
                var line = raw;

                if (inBlock)
                {
                    var end = line.IndexOf("*/", StringComparison.Ordinal);
                    if (end < 0)
                    {
                        continue;
                    }

                    inBlock = false;
                    line = line.Substring(end + 2);
                }

                var start = line.IndexOf("/*", StringComparison.Ordinal);
                if (start >= 0)
                {
                    inBlock = true;
                    line = line.Substring(0, start);
                }

                var slash = line.IndexOf("//", StringComparison.Ordinal);
                if (slash >= 0)
                {
                    line = line.Substring(0, slash);
                }

                if (line.Trim().Length > 0)
                {
                    yield return line;
                }
            }
        }
    }
}
