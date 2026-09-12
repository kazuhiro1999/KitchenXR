using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>注意の板の帯の色。琥珀（気をつける）と赤（危ない）の2段だけ。</summary>
    public enum HazardAccent
    {
        Amber,
        Red,
    }

    /// <summary>
    /// 注意の板1種類ぶんの型（題名・短い本文・記号・帯の色）。
    /// 絵文字は使わない——フォントの絵文字は端末とアトラスに左右されるので、
    /// 記号（▲／△）と帯の色だけで段を示す。
    /// </summary>
    public sealed class HazardPreset
    {
        public HazardPreset(string id, string mark, string title, string body, HazardAccent accent)
        {
            Id = id;
            Mark = mark;
            Title = title;
            Body = body;
            Accent = accent;
        }

        public string Id { get; }
        public string Mark { get; }
        public string Title { get; }
        public string Body { get; }
        public HazardAccent Accent { get; }
    }

    /// <summary>
    /// プリセットの一覧（<c>Resources/Hazards/presets.json</c>）。
    ///
    /// 読めない・形が違う・行が欠けている——どれも「その行は無かった」として飛ばし、
    /// 例外は投げない。注意の板が出ないことで調理は止まらないが、黙って空になるのは
    /// 困るので数は <see cref="Presets"/> で見える。
    /// </summary>
    public sealed class HazardPresetCatalog
    {
        public const string ResourcePath = "Hazards/presets";

        private readonly List<HazardPreset> _presets = new List<HazardPreset>();

        public IReadOnlyList<HazardPreset> Presets => _presets;

        public int Count => _presets.Count;

        /// <summary>同梱の一覧を読む。読めなければ空の一覧（板の入口は出るが行が並ばない）。</summary>
        public static HazardPresetCatalog LoadFromResources()
        {
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (text == null)
            {
                Debug.LogWarning($"[KitchenXR] 注意の板のプリセットが見つかりません: Resources/{ResourcePath}.json");
                return new HazardPresetCatalog();
            }

            return Parse(text.text);
        }

        public static HazardPresetCatalog Parse(string json)
        {
            var catalog = new HazardPresetCatalog();
            if (string.IsNullOrWhiteSpace(json))
            {
                return catalog;
            }

            JObject root;
            try
            {
                root = JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                Debug.LogWarning("[KitchenXR] 注意の板のプリセットの形が読めません（空の一覧にします）。");
                return catalog;
            }

            if (root?["presets"] is not JArray array)
            {
                return catalog;
            }

            foreach (var token in array)
            {
                if (token is not JObject obj)
                {
                    continue;
                }

                var id = obj["id"]?.ToString();
                var title = obj["title"]?.ToString();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                catalog._presets.Add(new HazardPreset(
                    id,
                    obj["mark"]?.ToString() ?? string.Empty,
                    title,
                    obj["body"]?.ToString() ?? string.Empty,
                    ParseAccent(obj["accent"]?.ToString())));
            }

            return catalog;
        }

        public bool TryGet(string id, out HazardPreset preset)
        {
            foreach (var candidate in _presets)
            {
                if (candidate.Id == id)
                {
                    preset = candidate;
                    return true;
                }
            }

            preset = null;
            return false;
        }

        /// <summary>知らない値は琥珀（軽い注意）へ落とす。</summary>
        public static HazardAccent ParseAccent(string value) =>
            value == "red" ? HazardAccent.Red : HazardAccent.Amber;
    }
}
