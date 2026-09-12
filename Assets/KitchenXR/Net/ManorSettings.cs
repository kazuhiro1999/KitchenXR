using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Net
{
    /// <summary>
    /// manor の繋ぎ先（<c>base_url</c>。形は <c>{"base_url": "https://…"}</c>）。
    /// 決まり方は優先順に3つ:
    ///   1. <c>persistentDataPath/manor.json</c> の <c>base_url</c>
    ///      （tailnet 越しなど探索が届かない置き方のための明示の上書き）
    ///   2. 端末の控え（<see cref="ManorDeviceFile"/>）に覚えている口
    ///   3. 探索（<see cref="ManorDiscovery"/>。UDP 8791 に manor が答える）
    /// どれも決まらなければ <see cref="IsConfigured"/> が false になり、板は見本だけを並べる。
    ///
    /// 合言葉（<c>passcode</c>）は読まない（代わりに端末ごとの鍵を使う）。古い
    /// <c>manor.json</c> に残っていたら警告を1行出す——「置いたのに使われない」で悩ませないため。
    /// </summary>
    public sealed class ManorSettings
    {
        public const string FileName = "manor.json";

        /// <summary>末尾の <c>/</c> は落としてある（<see cref="Url"/> が組み立てる）。</summary>
        public string BaseUrl { get; }

        /// <summary>繋ぎ先の出どころ（札とログに出す。場所を間違えたときに分かるように）。</summary>
        public string SourcePath { get; }

        private ManorSettings(string baseUrl, string sourcePath)
        {
            BaseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            SourcePath = sourcePath ?? string.Empty;
        }

        /// <summary>繋ぎ先が決まっているか（manor.json・控え・探索のどれかで）。</summary>
        public bool IsConfigured => !string.IsNullOrEmpty(BaseUrl);

        /// <summary>繋ぎ先が無いときの値（<see cref="IsConfigured"/> は false）。</summary>
        public static ManorSettings NotConfigured(string expectedPath = null) =>
            new ManorSettings(string.Empty, expectedPath);

        /// <summary>
        /// 控え・探索で決まった口から作る（<c>manor.json</c> を経由しない道）。
        /// <paramref name="source"/> は「控え」「探索」のような出どころの一言。
        /// </summary>
        public static ManorSettings ForBaseUrl(string baseUrl, string source = null) =>
            new ManorSettings(baseUrl, source);

        /// <summary>実機・Editor の既定の置き場（persistentDataPath）。</summary>
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>既定の置き場から読む。無ければ <see cref="NotConfigured"/>。</summary>
        public static ManorSettings LoadDefault() => Load(DefaultPath);

        /// <summary>
        /// 読む。ファイルが無い・壊れている・<c>base_url</c> が無いときは
        /// <see cref="NotConfigured"/>（＝控えか探索に任せる）。ここで落ちてはいけない。
        /// </summary>
        public static ManorSettings Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return NotConfigured(path);
            }

            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] {FileName} を読めませんでした: {path} ({e.Message})");
                return NotConfigured(path);
            }

            return Parse(json, path);
        }

        /// <summary>中身だけから作る（試験はこちらを通る）。</summary>
        public static ManorSettings Parse(string json, string sourcePath = null)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return NotConfigured(sourcePath);
            }

            JObject obj;
            try
            {
                obj = JToken.Parse(json) as JObject;
            }
            catch (JsonException e)
            {
                Debug.LogWarning($"[KitchenXR] {FileName} の形が読めませんでした: {e.Message}");
                return NotConfigured(sourcePath);
            }

            if (obj == null)
            {
                return NotConfigured(sourcePath);
            }

            WarnIfPasscode(obj);

            var baseUrl = obj["base_url"];
            if (baseUrl == null || baseUrl.Type == JTokenType.Null ||
                string.IsNullOrWhiteSpace(baseUrl.ToString()))
            {
                return NotConfigured(sourcePath);
            }

            return new ManorSettings(baseUrl.ToString().Trim(), sourcePath);
        }

        /// <summary>
        /// 古い使い方の合言葉が残っていたら教える。
        /// 読まないが、黙って捨てると「置いたのに繋がらない」の理由が分からなくなる。
        /// </summary>
        private static void WarnIfPasscode(JObject obj)
        {
            var passcode = obj["passcode"];
            if (passcode == null || passcode.Type == JTokenType.Null ||
                string.IsNullOrWhiteSpace(passcode.ToString()))
            {
                return;
            }

            Debug.LogWarning(
                $"[KitchenXR] {FileName} の passcode は廃止しました（読みません）。"
                + "起動時に出る6桁の番号を manor の 設定 → 端末 で許可してください"
                + "（Docs/MANOR.md）。行は消して構いません。");
        }

        /// <summary>
        /// 口の URL を組む。<paramref name="path"/> は <c>/api/v1/…</c> の形で渡す。
        /// </summary>
        public string Url(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BaseUrl;
            }

            return path.StartsWith("/") ? BaseUrl + path : BaseUrl + "/" + path;
        }
    }
}
