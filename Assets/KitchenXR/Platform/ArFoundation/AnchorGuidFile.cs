using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Platform.ArFoundation
{
    /// <summary>
    /// 永続アンカーの鍵の帳簿（`Application.persistentDataPath/anchors.json`。設計 §4.3）。
    ///
    /// 形は <c>{ "panel.recipe": "1f0a…-…", … }</c>——**板1枚＝鍵1つ**で、値は
    /// <c>ARAnchorManager.TrySaveAnchorAsync</c> が返す <c>SerializableGuid</c> の文字列。
    ///
    /// これを自分で持つ理由: AR Foundation の「保存済みアンカー ID の一覧」は**任意の機能**で、
    /// Meta Quest（Unity OpenXR: Meta 2.5）は `supportsGetSavedAnchorIds` を実装していない。
    /// つまり GUID を落とすと、保存したアンカーを二度と読み出せない（文書
    /// `features/anchors/persistent-anchors.md` の IMPORTANT）。
    ///
    /// この型は AR Foundation の型に触れない（文字列の出し入れだけ）ので、
    /// 実機なしの EditMode 試験でそのまま回せる。
    /// </summary>
    public sealed class AnchorGuidFile
    {
        public const string FileName = "anchors.json";

        private readonly string _path;
        private readonly Dictionary<string, Guid> _guids = new Dictionary<string, Guid>();

        public AnchorGuidFile(string path)
        {
            _path = path;
        }

        public static AnchorGuidFile CreateDefault() =>
            new AnchorGuidFile(Path.Combine(Application.persistentDataPath, FileName));

        public string FilePath => _path;

        public IReadOnlyDictionary<string, Guid> Guids => _guids;

        /// <summary>読む。無い・壊れている・鍵の値が GUID でない——どれも「無かった」として扱う。</summary>
        public void Load()
        {
            _guids.Clear();

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
                Debug.LogWarning($"[KitchenXR] アンカーの帳簿を読めませんでした: {_path} ({e.Message})");
                return;
            }

            JObject root;
            try
            {
                root = JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                Debug.LogWarning($"[KitchenXR] アンカーの帳簿の形が読めません（控えへ落とします）: {_path}");
                return;
            }

            if (root == null)
            {
                return;
            }

            foreach (var property in root.Properties())
            {
                var text = property.Value?.Type == JTokenType.String ? property.Value.ToString() : null;
                if (!string.IsNullOrEmpty(text) && Guid.TryParse(text, out var guid))
                {
                    _guids[property.Name] = guid;
                }
            }
        }

        public bool TryGet(string key, out Guid guid)
        {
            if (string.IsNullOrEmpty(key))
            {
                guid = Guid.Empty;
                return false;
            }

            return _guids.TryGetValue(key, out guid);
        }

        /// <summary>同じ鍵は上書き（古いアンカーの消去は <see cref="ArAnchorStore"/> の仕事）。</summary>
        public void Set(string key, Guid guid)
        {
            if (!string.IsNullOrEmpty(key))
            {
                _guids[key] = guid;
            }
        }

        public void Remove(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                _guids.Remove(key);
            }
        }

        public void Clear() => _guids.Clear();

        /// <summary>書く。書けなければ false（アンカー自体は残るが、次回は読み出せない＝控えへ落ちる）。</summary>
        public bool Save()
        {
            var root = new JObject();
            foreach (var pair in _guids)
            {
                root[pair.Key] = pair.Value.ToString();
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
                Debug.LogWarning($"[KitchenXR] アンカーの帳簿を書けませんでした: {_path} ({e.Message})");
                return false;
            }
        }

        public void Delete()
        {
            _guids.Clear();

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
    }
}
