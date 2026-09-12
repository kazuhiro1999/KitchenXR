using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KitchenXR.Net
{
    /// <summary>
    /// 端末の控え（<c>persistentDataPath/manor-device.json</c>）。形は
    /// <code>{"base_url":"http://…","token":"…","device_id":"…","user_id":"…","paired_at":"…"}</code>
    ///
    /// 鍵はペアリングで一度だけ降りてくるもので manor 側はハッシュしか持たない——落としたら
    /// 取り直すしかないので、受け取った瞬間に書く。<c>base_url</c> も覚えるのは、探索で見つけた
    /// 口を次の起動で先に試せるようにするため。
    ///
    /// 壊れていれば無かったことにする（落ちない・消さない）——ペアリングからやり直せばよい。
    /// </summary>
    public sealed class ManorDeviceFile
    {
        public const string FileName = "manor-device.json";

        private readonly string _path;

        public ManorDeviceFile(string path)
        {
            _path = path;
        }

        public static ManorDeviceFile CreateDefault() =>
            new ManorDeviceFile(Path.Combine(Application.persistentDataPath, FileName));

        public string FilePath => _path;

        public bool Exists => !string.IsNullOrEmpty(_path) && File.Exists(_path);

        /// <summary>
        /// 読む。無い・壊れている・中身が空のときは null（＝控えが無い扱い）。
        /// </summary>
        public ManorDevice Load()
        {
            if (!Exists)
            {
                return null;
            }

            string json;
            try
            {
                json = File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] {FileName} を読めませんでした: {_path} ({e.Message})");
                return null;
            }

            return Parse(json);
        }

        /// <summary>中身だけから作る（試験はこちらを通る）。読めなければ null。</summary>
        public static ManorDevice Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            JObject obj;
            try
            {
                // 日付として解釈させない（`paired_at` は控えた文字列のまま読み返したい——
                // Newtonsoft は既定で ISO 8601 を DateTime に直し、ToString で地域の書式に化ける）。
                using (var reader = new JsonTextReader(new StringReader(json))
                       { DateParseHandling = DateParseHandling.None })
                {
                    obj = JToken.ReadFrom(reader) as JObject;
                }
            }
            catch (JsonException)
            {
                // 壊れていれば無かったことにする（ペアリングからやり直せばよい）。
                return null;
            }

            if (obj == null)
            {
                return null;
            }

            var device = new ManorDevice(
                Text(obj["base_url"]), Text(obj["token"]),
                Text(obj["device_id"]), Text(obj["user_id"]), Text(obj["paired_at"]));

            // どちらも無ければ持っていても意味が無い（URL だけ・鍵だけなら残す）。
            return device.HasBaseUrl || device.HasToken ? device : null;
        }

        /// <summary>
        /// 鍵を控える（ペアリングが通った直後）。<paramref name="pairedAt"/> は試験のために渡せる。
        /// </summary>
        public void Save(string baseUrl, string token, string deviceId, string userId, string pairedAt = null)
        {
            var obj = new JObject
            {
                ["base_url"] = (baseUrl ?? string.Empty).TrimEnd('/'),
                ["token"] = token ?? string.Empty,
                ["device_id"] = deviceId ?? string.Empty,
                ["user_id"] = userId ?? string.Empty,
                ["paired_at"] = string.IsNullOrEmpty(pairedAt)
                    ? System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    : pairedAt,
            };

            Write(obj);
        }

        /// <summary>
        /// 繋ぎ先だけを書き換える（探索で見つけ直したとき）。
        /// 鍵は触らない——PC の住所が変わっただけで鍵を捨てさせない。
        /// </summary>
        public void SaveBaseUrl(string baseUrl)
        {
            var trimmed = (baseUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrEmpty(trimmed))
            {
                return;
            }

            var device = Load();
            if (device != null && device.BaseUrl == trimmed)
            {
                return; // 同じなら書かない（起動のたびに書き込まない）。
            }

            Write(new JObject
            {
                ["base_url"] = trimmed,
                ["token"] = device?.Token ?? string.Empty,
                ["device_id"] = device?.DeviceId ?? string.Empty,
                ["user_id"] = device?.UserId ?? string.Empty,
                ["paired_at"] = device?.PairedAt ?? string.Empty,
            });
        }

        /// <summary>
        /// 鍵を捨てる（manor が 401 を返した＝Web で失効させられた）。
        /// 繋ぎ先は残す——同じ manor にもう一度ペアリングするのだから、探索をやり直す必要はない。
        /// </summary>
        public void ForgetToken()
        {
            var device = Load();
            if (device == null)
            {
                return;
            }

            Write(new JObject
            {
                ["base_url"] = device.BaseUrl,
                ["token"] = string.Empty,
                ["device_id"] = string.Empty,
                ["user_id"] = string.Empty,
                ["paired_at"] = string.Empty,
            });
        }

        /// <summary>控えごと消す（試験と、繋ぎ先まで忘れたいとき）。</summary>
        public void Delete()
        {
            try
            {
                if (Exists)
                {
                    File.Delete(_path);
                }
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] {FileName} を消せませんでした: {_path} ({e.Message})");
            }
        }

        private void Write(JObject obj)
        {
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
                Debug.LogWarning($"[KitchenXR] {FileName} を書けませんでした: {_path} ({e.Message})");
            }
        }

        private static string Text(JToken token) =>
            token == null || token.Type == JTokenType.Null ? string.Empty : token.ToString().Trim();
    }

    /// <summary>控えの中身（不変。読んだ時点の写し）。</summary>
    public sealed class ManorDevice
    {
        /// <summary>末尾の <c>/</c> は落としてある。</summary>
        public string BaseUrl { get; }

        /// <summary>端末の鍵（<c>Authorization: Bearer</c> に付ける）。失効すると 401 が返る。</summary>
        public string Token { get; }

        public string DeviceId { get; }

        /// <summary>この端末が振る舞う利用者。札や記録に出すだけで、送りはしない。</summary>
        public string UserId { get; }

        public string PairedAt { get; }

        public ManorDevice(string baseUrl, string token, string deviceId, string userId, string pairedAt)
        {
            BaseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            Token = token ?? string.Empty;
            DeviceId = deviceId ?? string.Empty;
            UserId = userId ?? string.Empty;
            PairedAt = pairedAt ?? string.Empty;
        }

        public bool HasBaseUrl => !string.IsNullOrEmpty(BaseUrl);

        public bool HasToken => !string.IsNullOrEmpty(Token);
    }
}
