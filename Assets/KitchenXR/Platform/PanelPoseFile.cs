using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Platform
{
    /// <summary>
    /// 板の位置の**控え**（`Application.persistentDataPath/panels.json`。設計 §4.3 の「退避路」）。
    ///
    /// アンカーが使えない機（Editor の XR Simulation・アンカー非対応機）では、これが唯一の
    /// 記憶になる。アンカーが使えるときも**同じファイルを必ず書く**——アンカーの復元は
    /// 部屋が変わった・空間の地図が作り直された等で普通に失敗するので、そのときの受け皿が要る。
    ///
    /// 持つのは <b>XR Origin 基準の相対 Pose</b>。ワールド座標をそのまま書かないのは、
    /// XR Origin の原点（＝部屋の原点）が起動のたびに取り直されるため——相対で持てば、
    /// 「原点がずれれば板もずれる」で済む。ずれるのは承知の上で、無いよりましという判断
    /// （アンカーが本命。これは退避路）。
    ///
    /// 形:
    /// <code>
    /// { "panel.recipe": {"px":0,"py":1.35,"pz":1.2,"qx":0,"qy":0,"qz":0,"qw":1}, ... }
    /// </code>
    ///
    /// **壊れていても落ちない。** 読めない・形が違う・鍵の中身が数でない——どれも
    /// 「その鍵は無かった」として扱い、既定の位置へ落ちる（設計の原則: 起動を止めない）。
    /// </summary>
    public sealed class PanelPoseFile
    {
        public const string FileName = "panels.json";

        private readonly string _path;
        private readonly Dictionary<string, Pose> _poses = new Dictionary<string, Pose>();

        public PanelPoseFile(string path)
        {
            _path = path;
        }

        public static PanelPoseFile CreateDefault() =>
            new PanelPoseFile(Path.Combine(Application.persistentDataPath, FileName));

        public string FilePath => _path;

        /// <summary>今持っている鍵（試験・診断用）。</summary>
        public IReadOnlyDictionary<string, Pose> Poses => _poses;

        /// <summary>ファイルを読み込む。無い・壊れているときは空のまま（例外は投げない）。</summary>
        public void Load()
        {
            _poses.Clear();

            if (!File.Exists(_path))
            {
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 板の控えを読めませんでした: {_path} ({e.Message})");
                return;
            }

            JObject root;
            try
            {
                root = JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                Debug.LogWarning($"[KitchenXR] 板の控えの形が読めません（既定の位置に出します）: {_path}");
                return;
            }

            if (root == null)
            {
                return;
            }

            foreach (var property in root.Properties())
            {
                if (TryReadPose(property.Value as JObject, out var pose))
                {
                    _poses[property.Name] = pose;
                }
            }
        }

        public bool TryGet(string key, out Pose pose)
        {
            if (string.IsNullOrEmpty(key))
            {
                pose = default;
                return false;
            }

            return _poses.TryGetValue(key, out pose);
        }

        /// <summary>鍵1つを差し替える（同じ鍵は上書き）。書き出しは <see cref="Save"/>。</summary>
        public void Set(string key, Pose pose)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            _poses[key] = pose;
        }

        public void Remove(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                _poses.Remove(key);
            }
        }

        public void Clear() => _poses.Clear();

        /// <summary>書き出す。書けなければ false（呼び出し側は続けてよい——次の起動で既定に戻るだけ）。</summary>
        public bool Save()
        {
            var root = new JObject();
            foreach (var pair in _poses)
            {
                root[pair.Key] = WritePose(pair.Value);
            }

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_path, root.ToString(Formatting.None), Encoding.UTF8);
                return true;
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 板の控えを書けませんでした: {_path} ({e.Message})");
                return false;
            }
        }

        /// <summary>控えを消す（<c>IAnchorStore.ClearAsync</c> と対。無くても落ちない）。</summary>
        public void Delete()
        {
            _poses.Clear();

            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch (IOException)
            {
                // 消せなくても次の Save で上書きされる。
            }
        }

        private static JObject WritePose(Pose pose) => new JObject
        {
            ["px"] = pose.position.x,
            ["py"] = pose.position.y,
            ["pz"] = pose.position.z,
            ["qx"] = pose.rotation.x,
            ["qy"] = pose.rotation.y,
            ["qz"] = pose.rotation.z,
            ["qw"] = pose.rotation.w,
        };

        private static bool TryReadPose(JObject obj, out Pose pose)
        {
            pose = default;
            if (obj == null)
            {
                return false;
            }

            if (!TryReadFloat(obj, "px", out var px) ||
                !TryReadFloat(obj, "py", out var py) ||
                !TryReadFloat(obj, "pz", out var pz) ||
                !TryReadFloat(obj, "qx", out var qx) ||
                !TryReadFloat(obj, "qy", out var qy) ||
                !TryReadFloat(obj, "qz", out var qz) ||
                !TryReadFloat(obj, "qw", out var qw))
            {
                return false;
            }

            var rotation = new Quaternion(qx, qy, qz, qw);

            // 長さ 0 の回転（全部 0）は使えない。既定へ落とす。
            if (rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w < 1e-6f)
            {
                return false;
            }

            pose = new Pose(new Vector3(px, py, pz), rotation.normalized);
            return true;
        }

        private static bool TryReadFloat(JObject obj, string name, out float value)
        {
            value = 0f;

            var token = obj[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            if (!float.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
