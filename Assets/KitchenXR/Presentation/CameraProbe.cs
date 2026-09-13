using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Net;
using KitchenXR.Platform;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// カメラの下見（ロードマップ v1-d）。板の側から見ると仕事は3つしかない——
    /// 1枚取って絵にする・2枚/秒で JPEG にして大きさと時間を測る・1枚だけ manor へ投げて往復を測る。
    ///
    /// 実測値は必ず札と <c>FileLog</c> の両方へ出す。実機を USB で繋がずに確かめる段なので、
    /// 見えなかった・出なかったときに後から読める場所が要る。
    ///
    /// 判定も認識もここではしない（段 (b)(d) の仕事）。カメラが落ちても調理は止まらない。
    /// </summary>
    public sealed class CameraProbe : IDisposable
    {
        /// <summary>連写の間隔（ミリ秒）。2枚/秒。</summary>
        public const int BurstIntervalMs = 500;

        /// <summary>JPEG の品質（調査 §3.1 の 640×480・q70）。</summary>
        public const int JpegQuality = 70;

        /// <summary>権限が下りた直後に取り直す回数（流れ始めが少し遅いことがある）。</summary>
        public const int RetriesAfterGrant = 4;

        /// <summary>その取り直しの間隔（ミリ秒）。</summary>
        public const int RetryIntervalMs = 400;

        /// <summary>
        /// 権限の直後に取れなかったときの札。**このセッションではもう取れません**——
        /// 権限が無いまま始まった subsystem は 1 枚も返さず、起こし直すとパススルーが
        /// 消えるので（調査 §7）、立ち上げ直してもらうしかない。
        /// </summary>
        public const string PressAgainText = "許可されました。アプリを立ち上げ直してください";

        private readonly IPassthroughCamera _camera;
        private readonly ManorClient _manor;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();

        private Texture2D _texture;
        private CancellationTokenSource _burst;
        private bool _busy;

        /// <summary>manor へ投げたのは1枚だけ（往復の時間が知りたいだけなので繰り返さない）。</summary>
        private bool _posted;

        public CameraProbe(IPassthroughCamera camera, ManorClient manor = null)
        {
            _camera = camera;
            _manor = manor;
        }

        /// <summary>札か絵が変わった。板はこれを受けて描き直す。</summary>
        public event Action Changed;

        /// <summary>今 絵を出しているか（もう一度押すと消える）。</summary>
        public bool IsShowing => _texture != null;

        public bool IsBursting => _burst != null;

        /// <summary>小さな窓に貼る絵。出していなければ null。</summary>
        public Texture2D Texture => _texture;

        /// <summary>札の文言（実測か、失敗の理由）。</summary>
        public string StatusText { get; private set; } = string.Empty;

        // ---------------------------------------------------------------- 1枚

        /// <summary>「カメラ」の釦。出していれば消し、出していなければ1枚取る。</summary>
        public void Toggle()
        {
            if (IsShowing)
            {
                ReleaseTexture();
                SetStatus(string.Empty);
                return;
            }

            AcquireOnceAsync(_life.Token).Forget();
        }

        private async UniTaskVoid AcquireOnceAsync(CancellationToken token)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            try
            {
                var frame = await AcquireAsync(token);
                if (frame == null)
                {
                    return;
                }

                var clock = Stopwatch.StartNew();
                ApplyTexture(frame);
                clock.Stop();

                // 「取得〜表示」は取りに行ってから絵に貼り終わるまで（下の AcquireAsync が測った分を足す）。
                // 反転も出すのは、上下左右が合っているかを実機で目で確かめるため。
                Report($"取得 {frame.SizeText} / 取得〜表示 {_lastAcquireMs + clock.Elapsed.TotalMilliseconds:0}ms"
                       + $" / 内部パラメータ: {(frame.HasIntrinsics ? "あり" : "なし")}{FlipText}");

                Debug.Log($"[KitchenXR] カメラ: 撮影時刻 {frame.TimestampSeconds:0.000}s"
                          + $" 画素 {frame.Pixels.Length} バイト（{frame.Format}）");
            }
            finally
            {
                _busy = false;
            }
        }

        // ---------------------------------------------------------------- 連写（段 (c) の下見）

        /// <summary>「連写 2fps」のトグル。</summary>
        public void ToggleBurst()
        {
            if (IsBursting)
            {
                StopBurst();
                SetStatus("連写を止めました");
                return;
            }

            _burst = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
            BurstAsync(_burst.Token).Forget();
            SetStatus("連写 2fps: 始めました");
        }

        private void StopBurst()
        {
            _burst?.Cancel();
            _burst?.Dispose();
            _burst = null;
        }

        private async UniTaskVoid BurstAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var frame = await AcquireAsync(token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                // 1枚も取れないまま回し続けると、同じ理由を 2回/秒 ログへ積むだけになる。
                // 理由は既に札に出ているので、そこで止める。
                if (frame == null)
                {
                    StopBurst();
                    SetStatus($"{StatusText}（連写は止めました）");
                    return;
                }

                ApplyTexture(frame);

                var clock = Stopwatch.StartNew();
                var jpeg = await EncodeJpegAsync(frame, token);
                clock.Stop();

                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (jpeg == null)
                {
                    Report("連写 2fps: JPEG にできませんでした");
                }
                else
                {
                    Report($"連写 2fps: {frame.SizeText} JPEG {jpeg.Length / 1024f:0.0}KB"
                           + $" / 取得 {_lastAcquireMs:0}ms + 変換 {clock.Elapsed.TotalMilliseconds:0}ms{FlipText}");

                    await TryPostOnceAsync(jpeg, token);
                }

                await UniTask.Delay(BurstIntervalMs, ignoreTimeScale: true, cancellationToken: token)
                    .SuppressCancellationThrow();
            }
        }

        /// <summary>
        /// 1枚だけ manor へ投げて往復を測る。受け口はまだ無いので 404 が返るのが正常——
        /// 知りたいのは時間だけ。繋ぎ先も鍵も無ければ黙って何もしない。
        /// </summary>
        private async UniTask TryPostOnceAsync(byte[] jpeg, CancellationToken token)
        {
            if (_posted || _manor == null || !_manor.IsConfigured)
            {
                return;
            }

            _posted = true;

            var clock = Stopwatch.StartNew();
            var result = await _manor.PostVisionFrameAsync(jpeg, token);
            clock.Stop();

            if (token.IsCancellationRequested)
            {
                return;
            }

            Report(result.IsOffline
                ? $"送信: manor に繋がりません（{clock.Elapsed.TotalMilliseconds:0}ms）"
                : $"送信 {jpeg.Length / 1024f:0.0}KB → HTTP {result.StatusCode} / 往復 {clock.Elapsed.TotalMilliseconds:0}ms");
        }

        // ---------------------------------------------------------------- 中身

        private double _lastAcquireMs;

        /// <summary>
        /// 1枚取る（対応と権限の確認つき）。失敗はすべて札の文言にして null を返す。
        /// </summary>
        private async UniTask<CameraFrame> AcquireAsync(CancellationToken token)
        {
            _lastAcquireMs = 0d;

            if (_camera == null)
            {
                Report("カメラ: 口が挿さっていません");
                return null;
            }

            if (!_camera.IsSupported)
            {
                Report($"カメラ: {Reason("非対応")}");
                return null;
            }

            if (!await _camera.RequestPermissionAsync(token))
            {
                Report($"カメラ: {Reason("権限が下りていません")}");
                return null;
            }

            // 権限が下りた**そのセッション**では 1 枚も来ないことがある（実測 2026-09-13）。
            // 流れ始めるのを数回だけ待ってみて、駄目なら立ち上げ直してもらう
            // （起こし直すとパススルーが消えるので、それはしない）。
            var attempts = _camera.PermissionJustGranted ? 1 + RetriesAfterGrant : 1;

            for (var i = 0; i < attempts; i++)
            {
                if (i > 0)
                {
                    await UniTask.Delay(RetryIntervalMs, ignoreTimeScale: true, cancellationToken: token)
                        .SuppressCancellationThrow();
                }

                if (token.IsCancellationRequested)
                {
                    return null;
                }

                var clock = Stopwatch.StartNew();
                var frame = await _camera.AcquireAsync(token);
                clock.Stop();
                _lastAcquireMs = clock.Elapsed.TotalMilliseconds;

                if (token.IsCancellationRequested)
                {
                    return null;
                }

                if (frame != null && frame.IsValid)
                {
                    return frame;
                }
            }

            // 取り直しても駄目なら、次の1回では必ず取れる（subsystem は起き直っている）。
            Report(_camera.PermissionJustGranted
                ? PressAgainText
                : $"カメラ: {Reason("1枚も取れませんでした")}");
            return null;
        }

        private string Reason(string fallback) =>
            string.IsNullOrEmpty(_camera?.LastFailure) ? fallback : _camera.LastFailure;

        /// <summary>札の末尾に足す反転の一言（「 / 反転: X」）。無ければ空。</summary>
        private string FlipText =>
            string.IsNullOrEmpty(_camera?.TransformationText) ? string.Empty : $" / {_camera.TransformationText}";

        /// <summary>
        /// JPEG 化は主スレッドを止めない。<c>EncodeArrayToJPG</c> は
        /// <c>Texture2D</c> に触らないので別スレッドで回せるが、Unity の版で弾かれることが
        /// あるので、弾かれたら主スレッドでやり直す（黙って落とさない）。
        /// </summary>
        private static async UniTask<byte[]> EncodeJpegAsync(CameraFrame frame, CancellationToken token)
        {
            var format = frame.Format == TextureFormat.RGB24
                ? UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8_SRGB
                : UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB;

            byte[] jpeg = null;

            await UniTask.SwitchToThreadPool();
            try
            {
                jpeg = ImageConversion.EncodeArrayToJPG(
                    frame.Pixels, format, (uint)frame.Width, (uint)frame.Height, 0, JpegQuality);
            }
            catch (Exception)
            {
                jpeg = null;
            }

            await UniTask.SwitchToMainThread();
            if (jpeg != null || token.IsCancellationRequested)
            {
                return jpeg;
            }

            try
            {
                return ImageConversion.EncodeArrayToJPG(
                    frame.Pixels, format, (uint)frame.Width, (uint)frame.Height, 0, JpegQuality);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KitchenXR] カメラ: JPEG にできませんでした（{e.Message}）");
                return null;
            }
        }

        private void ApplyTexture(CameraFrame frame)
        {
            if (_texture != null &&
                (_texture.width != frame.Width || _texture.height != frame.Height ||
                 _texture.format != frame.Format))
            {
                ReleaseTexture();
            }

            if (_texture == null)
            {
                _texture = new Texture2D(frame.Width, frame.Height, frame.Format, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            _texture.LoadRawTextureData(frame.Pixels);
            _texture.Apply(false, false);
            Changed?.Invoke();
        }

        private void ReleaseTexture()
        {
            if (_texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_texture);
            }

            _texture = null;
            Changed?.Invoke();
        }

        /// <summary>札とログの両方へ同じ文を出す（実機で後から読めるように）。</summary>
        private void Report(string text)
        {
            SetStatus(text);
            if (!string.IsNullOrEmpty(text))
            {
                Debug.Log($"[KitchenXR] カメラ: {text}");
            }
        }

        private void SetStatus(string text)
        {
            StatusText = text ?? string.Empty;
            Changed?.Invoke();
        }

        /// <summary>
        /// 板（<c>VideoPanel</c>）と <c>Bootstrap</c> の両方が持ち主なので、2度呼ばれても壊れないこと。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopBurst();
            _life.Cancel();
            _life.Dispose();
            ReleaseTexture();
            _camera?.Dispose();
        }

        private bool _disposed;
    }
}
