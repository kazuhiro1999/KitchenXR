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
    }
}
