using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>注意の板1枚ぶんの控え（鍵と種類）。場所は持たない。</summary>
    public readonly struct HazardPanelRecord
    {
        public HazardPanelRecord(string key, string presetId)
        {
            Key = key;
            PresetId = presetId;
        }

        /// <summary>アンカーと控え（<c>panels.json</c>）の鍵（<c>panel.hazard.&lt;uuid&gt;</c>）。</summary>
        public string Key { get; }

        /// <summary>どのプリセットから作った板か。</summary>
        public string PresetId { get; }
    }

    /// <summary>
    /// 注意の板の控え（<c>persistentDataPath/hazards.json</c>）。形は
    /// <c>{"version":1,"panels":[{"key":"panel.hazard.&lt;uuid&gt;","preset":"fire"}]}</c>。
    ///
    /// **場所は持たない。** 位置と向きは既存の仕組み（アンカー ＋ <c>panels.json</c>）が鍵ごとに
    /// 覚えるので、ここが持つのは「どの板が在って何の種類か」だけ。分けておくと、板の位置の
    /// 復元の道筋（アンカー → 控え → 既定）を注意の板にもそのまま使える。
    /// </summary>
    public sealed class HazardPanelFile
    {
        public const string FileName = "hazards.json";

        private readonly string _path;

        public HazardPanelFile(string path)
        {
            _path = path;
        }

        public static HazardPanelFile CreateDefault() =>
            new HazardPanelFile(Path.Combine(Application.persistentDataPath, FileName));

        public string FilePath => _path;

        /// <summary>読む。無い・壊れているときは空（例外は投げない）。</summary>
        public List<HazardPanelRecord> Load()
        {
            var records = new List<HazardPanelRecord>();

            if (!File.Exists(_path))
            {
                return records;
            }

            string json;
            try
            {
                json = File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 注意の板の控えを読めませんでした: {_path} ({e.Message})");
                return records;
            }

            JObject root;
            try
            {
                root = JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                Debug.LogWarning($"[KitchenXR] 注意の板の控えの形が読めません（板なしで始めます）: {_path}");
                return records;
            }

            if (root?["panels"] is not JArray array)
            {
                return records;
            }

            foreach (var token in array)
            {
                if (token is not JObject obj)
                {
                    continue;
                }

                var key = obj["key"]?.ToString();
                var preset = obj["preset"]?.ToString();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(preset))
                {
                    continue;
                }

                records.Add(new HazardPanelRecord(key, preset));
            }

            return records;
        }

        public bool Save(IEnumerable<HazardPanelRecord> records)
        {
            var array = new JArray();
            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.Key))
                {
                    continue;
                }

                array.Add(new JObject
                {
                    ["key"] = record.Key,
                    ["preset"] = record.PresetId,
                });
            }

            var root = new JObject
            {
                ["version"] = 1,
                ["panels"] = array,
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
                Debug.LogWarning($"[KitchenXR] 注意の板の控えを書けませんでした: {_path} ({e.Message})");
                return false;
            }
        }
    }
}
