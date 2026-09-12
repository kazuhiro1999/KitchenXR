using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Net
{
    /// <summary>
    /// 進行の記録（<c>next</c> / <c>prev</c> / <c>end</c>）の追記ファイルの待ち行列
    /// （<c>Application.persistentDataPath/cook_events.jsonl</c>）。
    ///
    /// 調理の最中に電子レンジを回せば Wi-Fi は切れる。そのとき「次へ」が送れないことで調理が
    /// 止まってはいけないので、板の操作はまず Domain に効かせ、サーバへの報せはここへ
    /// 1行積むだけにする（ファイルへの追記1回なので待たせない）。送るのは <see cref="FlushAsync"/>。
    ///
    /// 1行1件の JSON（JSON Lines）なのは、途中で電源が落ちても壊れるのが最後の1行だけで
    /// 済むから——配列の JSON だと書き直しのたびに全体を書き換えることになる。
    /// </summary>
    public sealed class CookEventQueue
    {
        public const string FileName = "cook_events.jsonl";

        /// <summary>調理の終わり。manor では <c>/events</c> ではなく <c>/end</c> の口へ送る。</summary>
        public const string EndType = "end";

        private readonly string _path;

        public CookEventQueue(string path)
        {
            _path = path;
        }

        /// <summary>実機・Editor で使う既定の置き場。</summary>
        public static CookEventQueue CreateDefault() =>
            new CookEventQueue(Path.Combine(Application.persistentDataPath, FileName));

        /// <summary>待ち行列のファイル（実機では persistentDataPath 配下）。</summary>
        public string FilePath => _path;

        /// <summary>まだ送れていない件数。</summary>
        public int PendingCount => ReadAll().Count;

        /// <summary>
        /// 1件積む（ファイルへの追記1回）。<paramref name="sessionId"/> が無い
        /// （＝見本を進めている・manor 未設定）ときは何もしない。
        /// </summary>
        public void Enqueue(int? sessionId, string type, int? step = null)
        {
            if (!sessionId.HasValue || string.IsNullOrEmpty(type))
            {
                return;
            }

            var record = new JObject
            {
                ["session_id"] = sessionId.Value,
                ["type"] = type,
            };

            if (step.HasValue)
            {
                record["step"] = step.Value;
            }

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_path, record.ToString(Formatting.None) + "\n", Encoding.UTF8);
            }
            catch (IOException e)
            {
                // 積めなくても調理は続く（記録は二の次）。
                Debug.LogWarning($"[KitchenXR] 進行の記録を積めませんでした: {_path} ({e.Message})");
            }
        }

        /// <summary>
        /// 溜まっているものを積んだ順に送る。戻り値は送れた件数。止め方は3つ:
        ///   - 繋がらない → そこで止めて残りを残す（次の機会にまた先頭から）
        ///   - サーバが 5xx → 同じく残す（manor 側の一時的な不調）
        ///   - サーバが 4xx → その1件だけ捨てて先へ進む（既に終わったセッションへの
        ///     `next` など、何度送っても通らないもの。残すと行列が永久に詰まる）
        /// </summary>
        public async UniTask<int> FlushAsync(ManorClient client, CancellationToken token = default)
        {
            if (client == null || !client.IsConfigured)
            {
                return 0;
            }

            var records = ReadAll();
            if (records.Count == 0)
            {
                return 0;
            }

            var sent = 0;
            var index = 0;

            for (; index < records.Count; index++)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var record = records[index];
                var outcome = await SendOneAsync(client, record, token);

                if (outcome == Outcome.Sent || outcome == Outcome.Dropped)
                {
                    if (outcome == Outcome.Sent)
                    {
                        sent++;
                    }

                    continue;
                }

                break; // 繋がらない・5xx。ここから先は残す。
            }

            Rewrite(records.GetRange(index, records.Count - index));
            return sent;
        }

        private enum Outcome
        {
            Sent,
            Dropped,
            Keep,
        }

        private static async UniTask<Outcome> SendOneAsync(
            ManorClient client, CookEventRecord record, CancellationToken token)
        {
            if (record.Type == EndType)
            {
                var ended = await client.EndSessionAsync(record.SessionId, token);
                return Classify(ended.IsSuccess, ended.IsOffline, ended.StatusCode);
            }

            var posted = await client.PostEventAsync(record.SessionId, record.Type, record.Step, token);
            return Classify(posted.IsSuccess, posted.IsOffline, posted.StatusCode);
        }

        private static Outcome Classify(bool success, bool offline, int statusCode)
        {
            if (success)
            {
                return Outcome.Sent;
            }

            if (offline || statusCode >= 500 || statusCode <= 0)
            {
                return Outcome.Keep;
            }

            // 4xx。何度送っても通らないので捨てる（行列を詰まらせない）。
            Debug.LogWarning($"[KitchenXR] 進行の記録が manor に断られたので捨てます（{statusCode}）。");
            return Outcome.Dropped;
        }

        /// <summary>溜まっているものを読む。壊れた行は飛ばす。</summary>
        public List<CookEventRecord> ReadAll()
        {
            var result = new List<CookEventRecord>();
            if (!File.Exists(_path))
            {
                return result;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(_path, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 進行の記録を読めませんでした: {_path} ({e.Message})");
                return result;
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JObject obj;
                try
                {
                    obj = JToken.Parse(line) as JObject;
                }
                catch (JsonException)
                {
                    continue; // 電源断で千切れた最後の1行など。
                }

                if (obj == null || !int.TryParse(obj["session_id"]?.ToString(), out var sessionId))
                {
                    continue;
                }

                var type = obj["type"]?.ToString();
                if (string.IsNullOrEmpty(type))
                {
                    continue;
                }

                int? step = int.TryParse(obj["step"]?.ToString(), out var s) ? s : (int?)null;
                result.Add(new CookEventRecord(sessionId, type, step));
            }

            return result;
        }

        /// <summary>全部消す（試験と、行列が壊れたときの手当て）。</summary>
        public void Clear() => Rewrite(new List<CookEventRecord>());

        private void Rewrite(List<CookEventRecord> remaining)
        {
            try
            {
                if (remaining.Count == 0)
                {
                    if (File.Exists(_path))
                    {
                        File.Delete(_path);
                    }

                    return;
                }

                var sb = new StringBuilder();
                foreach (var record in remaining)
                {
                    sb.Append(record.ToJson()).Append('\n');
                }

                File.WriteAllText(_path, sb.ToString(), Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 進行の記録を書き戻せませんでした: {_path} ({e.Message})");
            }
        }
    }

    /// <summary>待ち行列の1件。</summary>
    public sealed class CookEventRecord
    {
        public int SessionId { get; }
        public string Type { get; }
        public int? Step { get; }

        public CookEventRecord(int sessionId, string type, int? step)
        {
            SessionId = sessionId;
            Type = type;
            Step = step;
        }

        public string ToJson()
        {
            var obj = new JObject
            {
                ["session_id"] = SessionId,
                ["type"] = Type,
            };

            if (Step.HasValue)
            {
                obj["step"] = Step.Value;
            }

            return obj.ToString(Formatting.None);
        }
    }
}
