using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace KitchenXR.Net
{
    /// <summary>
    /// manor への繋ぎ方（<c>Application.persistentDataPath/manor.json</c>）。
    ///
    /// <code>{"base_url": "https://…", "passcode": "…"}</code>
    ///
    /// **文字入力は板に置かない**（設計 §6・主人の指示）。Quest の空中キーボードで URL と
    /// 合言葉を打つのは調理の前にやりたいことではないし、打ち間違いが「繋がらない」に化ける。
    /// 主人は PC から <c>adb push</c> で置く（書き方は <c>Docs/manor-connection.md</c>）。
    /// 置かれていなければ <see cref="IsConfigured"/> が false になり、
    /// 一覧の板は「manor 未設定（見本だけ）」の札を出して見本の炒飯だけを並べる。
    ///
    /// 合言葉をここに平文で置くことになるが、これは manor 側の設計（ADR-005 §2 D4。
    /// 家庭内の1台を守るための合言葉）と同じ強さで、アプリ専用領域
    /// （<c>/sdcard/Android/data/com.kazuhiro.kitchenxr/files/</c>）に置く。
    /// </summary>
    public sealed class ManorSettings
    {
        public const string FileName = "manor.json";

        /// <summary>末尾の <c>/</c> は落としてある（<see cref="Url"/> が組み立てる）。</summary>
        public string BaseUrl { get; }

        public string Passcode { get; }

        /// <summary>読めた設定の出どころ（札に出す。主人が場所を間違えたときに分かるように）。</summary>
        public string SourcePath { get; }

        private ManorSettings(string baseUrl, string passcode, string sourcePath)
        {
            BaseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            Passcode = passcode ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
        }

        /// <summary>manor.json が在り、少なくとも base_url が読めた。</summary>
        public bool IsConfigured => !string.IsNullOrEmpty(BaseUrl);

        /// <summary>
        /// 合言葉が空＝manor が loopback（`auth_mode = "loopback"`）で動いている前提。
        /// その場合 <c>/auth/login</c> は cookie を返さず <c>{"ok":true,"mode":"loopback"}</c> だけを返す。
        /// </summary>
        public bool HasPasscode => !string.IsNullOrEmpty(Passcode);

        /// <summary>設定が無いときの値（<see cref="IsConfigured"/> は false）。</summary>
        public static ManorSettings NotConfigured(string expectedPath = null) =>
            new ManorSettings(string.Empty, string.Empty, expectedPath);

        /// <summary>実機・Editor の既定の置き場（persistentDataPath）。</summary>
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>既定の置き場から読む。無ければ <see cref="NotConfigured"/>。</summary>
        public static ManorSettings LoadDefault() => Load(DefaultPath);

        /// <summary>
        /// 読む。ファイルが無い・壊れている・鍵が足りないときは
        /// <see cref="NotConfigured"/>（＝見本だけで動く）。ここで落ちてはいけない。
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

            Dto dto;
            try
            {
                dto = JsonConvert.DeserializeObject<Dto>(json);
            }
            catch (JsonException e)
            {
                Debug.LogWarning($"[KitchenXR] {FileName} の形が読めませんでした: {e.Message}");
                return NotConfigured(sourcePath);
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.BaseUrl))
            {
                return NotConfigured(sourcePath);
            }

            return new ManorSettings(dto.BaseUrl.Trim(), dto.Passcode?.Trim(), sourcePath);
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

        private sealed class Dto
        {
            [JsonProperty("base_url")]
            public string BaseUrl;

            [JsonProperty("passcode")]
            public string Passcode;
        }
    }
}
