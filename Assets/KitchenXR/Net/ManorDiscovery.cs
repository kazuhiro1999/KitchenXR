using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// System.Diagnostics を丸ごと開くと Debug が UnityEngine.Debug と衝突するので、要る型だけ引く。
using Stopwatch = System.Diagnostics.Stopwatch;

namespace KitchenXR.Net
{
    /// <summary>
    /// manor を家の中で探す（manor の ADR-017 D3）。
    ///
    /// PC の LAN の住所は変わり得る——だから URL を固定で書かせない。UDP <see cref="Port"/> へ
    /// <see cref="Probe"/> の1行を投げると、manor が
    /// <c>{"kind":"manor","name":…,"base_url":"http://<![CDATA[<受けた口の IP>:<port>]]>","version":…}</c>
    /// を**自分宛の unicast で**返す。返す IP は「その問い合わせが届いた口のアドレス」なので、
    /// PC に NIC が複数あっても正しい方が来る。
    ///
    /// Android の許可は <c>INTERNET</c> だけで足りる（投げるのはブロードキャストだが、
    /// 受けるのは自分宛の返り）。Editor でも同じ道が通る——同じ PC で manor が待っていれば答える。
    ///
    /// **見つからないのは異常ではない**（manor が寝ている・ループバックで立っている・
    /// tailnet の向こうに居る）。空の文字列を返すだけで、呼び出し側は見本だけで動く。
    /// </summary>
    public sealed class ManorDiscovery
    {
        /// <summary>manor が待っている口（ADR-017 D3）。</summary>
        public const int Port = 8791;

        /// <summary>投げる1行。manor はこの文字列にだけ答える。</summary>
        public const string Probe = "manor-discover v1";

        /// <summary>1回の探索で返りを待つ長さ。家の中の1往復なので短くてよい。</summary>
        public const int WaitMilliseconds = 1500;

        /// <summary>既定の試行回数（UDP は落ちるもの。3回で諦める）。</summary>
        public const int DefaultAttempts = 3;

        /// <summary>
        /// 探す。見つかれば <c>base_url</c>（末尾の <c>/</c> は落とす）、見つからなければ空。
        /// **例外は投げない**——Wi-Fi が無い・許可が無い・口が塞がっているのは全部「見つからない」。
        /// </summary>
        public async UniTask<string> FindBaseUrlAsync(
            int attempts = DefaultAttempts, CancellationToken token = default)
        {
            for (var attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
            {
                if (token.IsCancellationRequested)
                {
                    return string.Empty;
                }

                var found = await ProbeOnceAsync(token);
                if (!string.IsNullOrEmpty(found))
                {
                    Debug.Log($"[KitchenXR] manor を探索で見つけました: {found}（{attempt}回目）");
                    return found;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 1回だけ投げて <see cref="WaitMilliseconds"/> 待つ。
        ///
        /// 受けるのは <c>UdpClient.Available</c> を見ながらの細かい待ち合わせにしている——
        /// <c>Receive</c> をそのまま呼ぶと返りが来るまで**主スレッドが止まる**ので、
        /// 「届いているものだけを取る」形にして 50ms ずつ譲る。
        /// </summary>
        private static async UniTask<string> ProbeOnceAsync(CancellationToken token)
        {
            UdpClient udp = null;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
                var payload = Encoding.UTF8.GetBytes(Probe);
                udp.Send(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, Port));

                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < WaitMilliseconds)
                {
                    if (token.IsCancellationRequested)
                    {
                        return string.Empty;
                    }

                    if (udp.Available > 0)
                    {
                        var remote = new IPEndPoint(IPAddress.Any, 0);
                        var data = udp.Receive(ref remote);
                        var baseUrl = ParseBaseUrl(Encoding.UTF8.GetString(data));
                        if (!string.IsNullOrEmpty(baseUrl))
                        {
                            return baseUrl;
                        }

                        // manor 以外の何かが答えた。残り時間だけ待ち続ける。
                        continue;
                    }

                    await UniTask.Delay(50, ignoreTimeScale: true, cancellationToken: token);
                }
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }
            catch (SocketException e)
            {
                // ブロードキャストが出せない（機内モード・許可が無い・口が塞がっている）。
                Debug.Log($"[KitchenXR] manor の探索を投げられませんでした（{e.SocketErrorCode}）");
                return string.Empty;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KitchenXR] manor の探索で想定外の失敗: {e.Message}");
                return string.Empty;
            }
            finally
            {
                udp?.Close();
            }

            return string.Empty;
        }

        /// <summary>
        /// 返りの JSON から <c>base_url</c> を取る（**純粋関数**。EditMode 試験はここを通る）。
        /// <c>kind</c> が <c>"manor"</c> でなければ空——同じ口に別の何かが答えても拾わない。
        /// </summary>
        public static string ParseBaseUrl(string json) => ParseAnnouncement(json)?.BaseUrl ?? string.Empty;

        /// <summary>返りの JSON を読む。manor の返りでなければ null。</summary>
        public static ManorAnnouncement ParseAnnouncement(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
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

            if (obj == null)
            {
                return null;
            }

            if (!string.Equals(Text(obj["kind"]), "manor", StringComparison.Ordinal))
            {
                return null;
            }

            var baseUrl = Text(obj["base_url"]).TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                return null; // 繋ぎ先が無い返りは使えない。
            }

            return new ManorAnnouncement(baseUrl, Text(obj["name"]), Text(obj["version"]));
        }

        private static string Text(JToken token) =>
            token == null || token.Type == JTokenType.Null ? string.Empty : token.ToString().Trim();
    }

    /// <summary>探索の返り（manor が名乗ったもの）。</summary>
    public sealed class ManorAnnouncement
    {
        /// <summary>末尾の <c>/</c> は落としてある。</summary>
        public string BaseUrl { get; }

        /// <summary>manor が動いている PC の名前（ログに出す。複数見つけたときの見分け）。</summary>
        public string Name { get; }

        public string Version { get; }

        public ManorAnnouncement(string baseUrl, string name, string version)
        {
            BaseUrl = baseUrl ?? string.Empty;
            Name = name ?? string.Empty;
            Version = version ?? string.Empty;
        }
    }
}
