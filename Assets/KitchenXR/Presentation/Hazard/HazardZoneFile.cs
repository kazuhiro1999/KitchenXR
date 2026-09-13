using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 領域の控え（<c>persistentDataPath/zones.json</c>）。形は
    /// <c>{"version":1,"zones":[{"id":…,"kind":…,"center":{x,y,z},"size":{x,z},"yaw":…,"height":…}]}</c>。
    ///
    /// <see cref="Platform.PanelPoseFile"/> と同じ作り・同じ約束で、持つのは **XR Origin 基準の
    /// 相対座標**（XR Origin の原点は起動のたびに取り直されるので、世界座標をそのまま書くと
    /// 使えない）。アンカーが取れるときはアンカーが勝つが、控えは必ず書く——アンカーの復元は
    /// 部屋が変わった等で普通に失敗する。
    ///
    /// 読めない・形が違う・数でない——どれも「その行は無かった」として飛ばす（起動を止めない）。
    /// </summary>
    public sealed class HazardZoneFile
    {
        public const string FileName = "zones.json";

        private readonly string _path;

        public HazardZoneFile(string path)
        {
            _path = path;
        }

        public static HazardZoneFile CreateDefault() =>
            new HazardZoneFile(Path.Combine(Application.persistentDataPath, FileName));

        public string FilePath => _path;

        /// <summary>読む。無い・壊れているときは空の一覧（例外は投げない）。</summary>
        public List<HazardZone> Load()
        {
            var zones = new List<HazardZone>();

            if (!File.Exists(_path))
            {
                return zones;
            }

            string json;
            try
            {
                json = File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 領域の控えを読めませんでした: {_path} ({e.Message})");
                return zones;
            }

            JObject root;
            try
            {
                root = JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                Debug.LogWarning($"[KitchenXR] 領域の控えの形が読めません（領域なしで始めます）: {_path}");
                return zones;
            }

            if (root?["zones"] is not JArray array)
            {
                return zones;
            }

            foreach (var token in array)
            {
                if (token is JObject obj && TryReadZone(obj, out var zone))
                {
                    zones.Add(zone);
                }
            }

            return zones;
        }

        /// <summary>書き出す。書けなければ false（次の起動で領域が消えるだけで、調理は止まらない）。</summary>
        public bool Save(IEnumerable<HazardZone> zones)
        {
            var array = new JArray();
            foreach (var zone in zones)
            {
                if (zone != null)
                {
                    array.Add(WriteZone(zone));
                }
            }

            var root = new JObject
            {
                ["version"] = 1,
                ["zones"] = array,
            };

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
                Debug.LogWarning($"[KitchenXR] 領域の控えを書けませんでした: {_path} ({e.Message})");
                return false;
            }
        }

        private static JObject WriteZone(HazardZone zone) => new JObject
        {
            ["id"] = zone.Id,
            ["kind"] = zone.Kind,
            ["center"] = new JObject
            {
                ["x"] = zone.Center.x,
                ["y"] = zone.Center.y,
                ["z"] = zone.Center.z,
            },
            ["size"] = new JObject
            {
                ["x"] = zone.SizeX,
                ["z"] = zone.SizeZ,
            },
            ["yaw"] = zone.YawDegrees,
            ["height"] = zone.Height,
        };

        private static bool TryReadZone(JObject obj, out HazardZone zone)
        {
            zone = null;

            var id = obj["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var center = obj["center"] as JObject;
            var size = obj["size"] as JObject;
            if (center == null || size == null)
            {
                return false;
            }

            if (!TryReadFloat(center, "x", out var cx) ||
                !TryReadFloat(center, "y", out var cy) ||
                !TryReadFloat(center, "z", out var cz) ||
                !TryReadFloat(size, "x", out var sx) ||
                !TryReadFloat(size, "z", out var sz) ||
                !TryReadFloat(obj, "yaw", out var yaw) ||
                !TryReadFloat(obj, "height", out var height))
            {
                return false;
            }

            if (sx < HazardZone.MinSideMeters || sz < HazardZone.MinSideMeters)
            {
                return false;
            }

            zone = new HazardZone(
                id, obj["kind"]?.ToString(), new Vector3(cx, cy, cz), sx, sz, yaw, height);
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

            if (!float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
