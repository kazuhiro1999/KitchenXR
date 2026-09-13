using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Presentation
{
    /// <summary>見え方の3段（小・中・大）。中が既定。</summary>
    public enum DisplayScale
    {
        Small,
        Medium,
        Large,
    }

    /// <summary>
    /// 表示の設定。文字の大きさと板の大きさは小・中・大の3段で、自由な数値にしない
    /// ——調理中の手で細かい目盛りを合わせるのは無理だし、中途半端な値にすると
    /// 「1m 先で読める」が保証できなくなる。カメラの入／切もここに置く。
    ///
    /// 置き場は <c>Application.persistentDataPath/settings.json</c>
    /// （<see cref="KitchenXR.Net.LastSessionStore"/> と同じ流儀）。
    /// 壊れていたら黙って既定に戻す——設定が読めないことで起動が止まってはいけない。
    ///
    /// ここは純粋な読み書きだけ。実際に板へ当てるのは <see cref="DisplaySettingsApplier"/>。
    /// </summary>
    public sealed class DisplaySettings
    {
        public const string FileName = "settings.json";

        private const string FontScaleKey = "font_scale";
        private const string PanelScaleKey = "panel_scale";
        private const string CameraEnabledKey = "camera_enabled";

        private readonly string _path;

        public DisplaySettings(string path)
        {
            _path = path;
        }

        /// <summary>実機・Editor で使う既定の置き場。</summary>
        public static DisplaySettings CreateDefault() =>
            new DisplaySettings(Path.Combine(Application.persistentDataPath, FileName));

        public string FilePath => _path;

        /// <summary>文字の大きさ（板の <c>.root-panel</c> に付けるクラスへ変換される）。</summary>
        public DisplayScale FontScale { get; set; } = DisplayScale.Medium;

        /// <summary>板の大きさ（板の <c>localScale</c> の係数になる）。</summary>
        public DisplayScale PanelScale { get; set; } = DisplayScale.Medium;

        /// <summary>
        /// カメラを使うか。**既定は無効**——カメラを使わない日のほうが多いし、
        /// 無効のうちは権限も求めない（起動のたびにダイアログが出ない）。
        /// </summary>
        public bool CameraEnabled { get; set; }

        /// <summary>
        /// 読む。ファイルが無い・読めない・JSON が壊れている・知らない段が書かれている、
        /// のどれでも既定（中・中・カメラ無効）に戻す。戻り値は「ファイルから読めたか」。
        /// </summary>
        public bool Load()
        {
            FontScale = DisplayScale.Medium;
            PanelScale = DisplayScale.Medium;
            CameraEnabled = false;

            if (!File.Exists(_path))
            {
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 表示の設定を読めませんでした: {_path} ({e.Message})");
                return false;
            }

            JObject obj;
            try
            {
                obj = JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                // 壊れた JSON。既定のまま進む（次に保存したときに上書きされる）。
                return false;
            }

            if (obj == null)
            {
                return false;
            }

            FontScale = ParseScale(obj[FontScaleKey]?.ToString());
            PanelScale = ParseScale(obj[PanelScaleKey]?.ToString());
            CameraEnabled = obj[CameraEnabledKey]?.Type == JTokenType.Boolean &&
                            obj[CameraEnabledKey].Value<bool>();
            return true;
        }

        /// <summary>書く。書けなくても設定はこの起動の間は効いている（次の起動で戻るだけ）。</summary>
        public void Save()
        {
            var obj = new JObject
            {
                [FontScaleKey] = ToToken(FontScale),
                [PanelScaleKey] = ToToken(PanelScale),
                [CameraEnabledKey] = CameraEnabled,
            };

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_path, obj.ToString(Formatting.None), Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 表示の設定を控えられませんでした: {_path} ({e.Message})");
            }
        }

        /// <summary>知らない文字列は既定（中）。数値や null もここへ落ちる。</summary>
        public static DisplayScale ParseScale(string value)
        {
            switch (value)
            {
                case "small":
                    return DisplayScale.Small;
                case "large":
                    return DisplayScale.Large;
                default:
                    return DisplayScale.Medium;
            }
        }

        public static string ToToken(DisplayScale scale)
        {
            switch (scale)
            {
                case DisplayScale.Small:
                    return "small";
                case DisplayScale.Large:
                    return "large";
                default:
                    return "medium";
            }
        }
    }
}
