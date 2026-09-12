using System;
using System.Collections.Generic;
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
        /// 最後に探索が投げられなかった理由（札に出す。主人が実機で読める言葉）。
        /// 見つかったとき・まだ投げていないときは空。
        /// </summary>
        public string LastFailure { get; private set; } = string.Empty;

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
                    LastFailure = string.Empty;
                    return found;
                }
            }

            if (string.IsNullOrEmpty(LastFailure))
            {
                LastFailure = $"返事がありません（{attempts}回・UDP {Port}）";
            }

            return string.Empty;
        }

        /// <summary>
        /// 投げる宛先。限定ブロードキャスト（255.255.255.255）に加えて、自分の IPv4 の
        /// サブネット向けブロードキャスト。マスクが取れないときは /24 と見なす
        /// （家庭の Wi-Fi はほぼ /24。外れても限定ブロードキャストが残る）。
        /// </summary>
        public static IEnumerable<IPAddress> BroadcastTargets()
        {
            var targets = new List<IPAddress> { IPAddress.Broadcast };
            foreach (var local in LocalIPv4Addresses())
            {
                var bytes = local.GetAddressBytes();
                bytes[3] = 255;
                var directed = new IPAddress(bytes);
                if (!targets.Contains(directed))
                {
                    targets.Add(directed);
                }
            }

            return targets;
        }

        /// <summary>
        /// 自分の IPv4（loopback 以外）。NetworkInterface が Android で空を返すことがあるので、
        /// 8.8.8.8 へ「繋いだつもり」の UDP ソケットの送り元アドレスも見る（パケットは出ない）。
        /// </summary>
        private static IEnumerable<IPAddress> LocalIPv4Addresses()
        {
            var found = new List<IPAddress>();
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    {
                        continue;
                    }

                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        var address = unicast.Address;
                        if (address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(address) && !found.Contains(address))
                        {
                            found.Add(address);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Android の一部で NetworkInterface が投げる。下の経路表の手で補う。
            }

            try
            {
                using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    probe.Connect(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
                    if (probe.LocalEndPoint is IPEndPoint local && !IPAddress.IsLoopback(local.Address) &&
                        !IPAddress.Any.Equals(local.Address) && !found.Contains(local.Address))
                    {
                        found.Add(local.Address);
                    }
                }
            }
            catch (Exception)
            {
                // 経路が無い（機内モード）。空のまま。
            }

            return found;
        }

        /// <summary>
        /// 1回だけ投げて <see cref="WaitMilliseconds"/> 待つ。
        ///
        /// 受けるのは <c>UdpClient.Available</c> を見ながらの細かい待ち合わせにしている——
        /// <c>Receive</c> をそのまま呼ぶと返りが来るまで**主スレッドが止まる**ので、
        /// 「届いているものだけを取る」形にして 50ms ずつ譲る。
        /// </summary>
        private async UniTask<string> ProbeOnceAsync(CancellationToken token)
        {
            UdpClient udp = null;
            try
            {
                udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
                var payload = Encoding.UTF8.GetBytes(Probe);

                // 2026-09-13 実機: 「探しています」も出ずに「見つかりません」——Android では
                // 255.255.255.255（限定ブロードキャスト）の送信が経路無しで失敗することがある。
                // 自分の IPv4 から**サブネット向けのブロードキャスト**（192.168.0.255 等）も組んで、
                // 両方へ投げる。片方が失敗しても続ける。1つも出せなければ理由を残して諦める。
                var sent = 0;
                var failures = new StringBuilder();
                foreach (var target in BroadcastTargets())
                {
                    try
                    {
                        udp.Send(payload, payload.Length, new IPEndPoint(target, Port));
                        sent++;
                    }
                    catch (SocketException e)
                    {
                        failures.Append($"{target}: {e.SocketErrorCode}; ");
                    }
                }

                if (sent == 0)
                {
                    LastFailure = $"ブロードキャストを出せません（{failures.ToString().TrimEnd(' ', ';')}）";
                    Debug.Log($"[KitchenXR] manor の探索: {LastFailure}");
                    return string.Empty;
                }

                Debug.Log($"[KitchenXR] manor の探索を {sent} 宛てに投げました（{string.Join(", ", BroadcastTargets())}）");

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
                // 口を開けない（機内モード・許可が無い・口が塞がっている）。
                LastFailure = $"UDP の口を開けません（{e.SocketErrorCode}）";
                Debug.Log($"[KitchenXR] manor の探索: {LastFailure}");
                return string.Empty;
            }
            catch (Exception e)
            {
                LastFailure = $"想定外の失敗（{e.GetType().Name}: {e.Message}）";
                Debug.LogWarning($"[KitchenXR] manor の探索: {LastFailure}");
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
