using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Net
{
    /// <summary>
    /// 最後に開いていた調理（<c>Application.persistentDataPath/last_session.json</c>）。
    ///
    /// 途中起動の復帰（設計 §5・ROADMAP P5）は本来 manor の
    /// <c>GET /api/v1/kitchen/cook-sessions/current</c> が受け持つ。けれど**オフラインのときは
    /// それが読めない**——手を洗っている間にヘッドセットを外し、戻ったら Wi-Fi が切れていた、
    /// というのは十分ありうる。そのときはここから戻す（レシピ本体と画像は
    /// <see cref="RecipeStore"/> に既に揃っているので、工程番号さえあれば続きが出せる）。
    ///
    /// 書くのは「工程が動いたとき」と「調理を始めたとき」。消すのは調理を終えたとき。
    /// </summary>
    public sealed class LastSessionStore
    {
        public const string FileName = "last_session.json";

        private readonly string _path;

        public LastSessionStore(string path)
        {
            _path = path;
        }

        public static LastSessionStore CreateDefault() =>
            new LastSessionStore(Path.Combine(Application.persistentDataPath, FileName));

        public string FilePath => _path;

        public bool Exists => File.Exists(_path);

        /// <summary>書く（工程が動くたび。小さいファイルなので待たせない）。</summary>
        public void Save(string recipeId, int? sessionId, int currentStep, bool isComplete = false)
        {
            if (string.IsNullOrEmpty(recipeId))
            {
                return;
            }

            var obj = new JObject
            {
                ["recipe_id"] = recipeId,
                ["current"] = currentStep,
                ["complete"] = isComplete,
            };

            if (sessionId.HasValue)
            {
                obj["session_id"] = sessionId.Value;
            }

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
                Debug.LogWarning($"[KitchenXR] 途中の調理を控えられませんでした: {_path} ({e.Message})");
            }
        }

        /// <summary>読む。無い・壊れている・レシピ id が無いときは null。</summary>
        public LastSession Load()
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            string json;
            try
            {
                json = File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (IOException)
            {
                return null;
            }

            JObject obj;
            try
            {
                obj = JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }

            var recipeId = obj?["recipe_id"]?.ToString();
            if (string.IsNullOrEmpty(recipeId))
            {
                return null;
            }

            int? sessionId = int.TryParse(obj["session_id"]?.ToString(), out var sid) ? sid : (int?)null;
            var current = int.TryParse(obj["current"]?.ToString(), out var c) ? c : 1;
            var complete = bool.TryParse(obj["complete"]?.ToString(), out var done) && done;

            return new LastSession(recipeId, sessionId, current, complete);
        }

        /// <summary>消す（調理を終えた・一覧へ戻った）。</summary>
        public void Clear()
        {
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch (IOException)
            {
                // 消せなくても、次に開いたとき工程が戻るだけで害は無い。
            }
        }
    }

    /// <summary>控えた途中の調理。</summary>
    public sealed class LastSession
    {
        public string RecipeId { get; }
        public int? SessionId { get; }
        public int Current { get; }
        public bool IsComplete { get; }

        public LastSession(string recipeId, int? sessionId, int current, bool isComplete)
        {
            RecipeId = recipeId;
            SessionId = sessionId;
            Current = current;
            IsComplete = isComplete;
        }
    }
}
