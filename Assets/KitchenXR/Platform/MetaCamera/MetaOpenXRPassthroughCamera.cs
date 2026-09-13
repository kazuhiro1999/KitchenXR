using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace KitchenXR.Platform.MetaCamera
{
    /// <summary>
    /// Quest 3 のパススルーカメラ（左目・mono）。Unity OpenXR: Meta 2.5.1 の
    /// provider 固有の口を直に叩く——AR Foundation の汎用 API（<c>ARCameraManager</c>）の
    /// 対応表では Meta の "Camera image" は非対応で、そちらからは取れない（調査 §1.5）。
    ///
    /// 要るのは3つだけで、追加パッケージは要らない:
    ///   - OpenXR の「Meta Quest: Camera (Passthrough)」の <b>Camera Image Support</b>
    ///     （立てると manifest に <c>horizonos.permission.HEADSET_CAMERA</c> が入る）
    ///   - <b>minSdk 32</b>（CPU 画像は Android 12L 以上）
    ///   - 実行時の権限要求（manifest に在るだけでは足りない。<see cref="RequestPermissionAsync"/>）
    ///
    /// この型に触れてよいのは <c>Platform/MetaCamera/</c> の中だけ
    /// （<c>PlatformIsolationTests</c> が検算する線）。
    /// </summary>
    public sealed class MetaOpenXRPassthroughCamera : IPassthroughCamera
    {
        /// <summary>Horizon OS のカメラ権限。manifest は Camera Image Support が入れる。</summary>
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

        /// <summary>
        /// 出す絵の幅の上限。元は 1280×960 だが、認識にも板の窓にも過剰で、
        /// 変換と JPEG 化のぶんだけ遅くなる（調査 §3.1 は 640×480・JPEG q70 を薦めている）。
        /// </summary>
        public const int MaxOutputWidth = 640;

        /// <summary>権限のダイアログの返事を待つ上限（ミリ秒）。</summary>
        public const int PermissionTimeoutMs = 60_000;

        /// <summary>起こし直したあと、最初の1枚が流れてくるまでの待ち（ミリ秒）。</summary>
        public const int RestartSettleMs = 600;

        /// <summary>
        /// 実機の実測（2026-09-13）で絵が 180 度回っていた。<c>MirrorY</c> を掛けた結果が
        /// 180 度回転だったので、真っ直ぐ出すのは <c>MirrorX</c>
        /// （<c>MirrorY(元) = Rot180(真)</c> ⇒ <c>真 = MirrorX(元)</c>）。
        /// </summary>
        public const XRCpuImage.Transformation OutputTransformation = XRCpuImage.Transformation.MirrorX;

        private static readonly List<XRCameraSubsystem> Subsystems = new List<XRCameraSubsystem>();

        private bool _disposed;

        public string LastFailure { get; private set; } = string.Empty;

        public bool PermissionJustGranted { get; private set; }

        public string TransformationText => "反転: X";

        public bool IsSupported => FindSubsystem() != null;

        /// <summary>
        /// 実行時の権限。Unity は <c>horizonos.permission.HEADSET_CAMERA</c> を自動では求めないので
        /// 自分で呼ぶ（調査 §1.3）。返事は callback で来るので、決着が付くまで待つ。
        /// </summary>
        public async UniTask<bool> RequestPermissionAsync(CancellationToken token = default)
        {
            PermissionJustGranted = false;

            if (_disposed)
            {
                return false;
            }

            if (Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                return true;
            }

            var settled = false;
            var granted = false;

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                granted = true;
                settled = true;
            };
            callbacks.PermissionDenied += _ => settled = true;
            callbacks.PermissionDeniedAndDontAskAgain += _ => settled = true;

            Permission.RequestUserPermission(HeadsetCameraPermission, callbacks);

            var deadline = Time.realtimeSinceStartup + PermissionTimeoutMs / 1000f;
            while (!settled && Time.realtimeSinceStartup < deadline && !token.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token).SuppressCancellationThrow();
            }

            // callback が来ないまま抜けることがある（別のダイアログに割り込まれた等）。
            // 最後は OS に訊いて決める。
            granted = granted || Permission.HasUserAuthorizedPermission(HeadsetCameraPermission);
            if (!granted)
            {
                LastFailure = "カメラの権限が下りていません（設定 → アプリ → 権限）";
                return false;
            }

            // ここに来たのは「今 許可が下りた」ときだけ（既に許されていれば上で返っている）。
            // 権限が無いまま始まった subsystem はそのセッションの間ずっと 1 枚も返さないので、
            // 口を起こし直してから最初の1枚が流れてくるのを待つ。
            PermissionJustGranted = true;
            Restart();
            await UniTask.Delay(RestartSettleMs, ignoreTimeScale: true, cancellationToken: token)
                .SuppressCancellationThrow();

            return true;
        }

        /// <summary>
        /// カメラの口を起こし直す。持ち主は <c>ARCameraManager</c>（MR テンプレートの rig に居る）
        /// なので、まずその <c>enabled</c> を落として立て直す——<c>OnDisable</c>／<c>OnEnable</c> は
        /// setter の中で同期に走るので、その場で subsystem の Stop／Start まで通る。
        /// 見つからないときだけ subsystem を直に Stop／Start する。
        /// </summary>
        public bool Restart()
        {
            if (_disposed)
            {
                return false;
            }

            var manager = UnityEngine.Object.FindFirstObjectByType<ARCameraManager>(
                FindObjectsInactive.Include);

            if (manager != null && manager.enabled)
            {
                manager.enabled = false;
                manager.enabled = true;
                Debug.Log("[KitchenXR] カメラ: ARCameraManager を起こし直しました。");
                return true;
            }

            var subsystem = FindSubsystem();
            if (subsystem == null)
            {
                return false;
            }

            if (subsystem.running)
            {
                subsystem.Stop();
            }

            subsystem.Start();
            Debug.Log("[KitchenXR] カメラ: subsystem を Stop／Start しました。");
            return true;
        }

        public bool TryAcquire(out CameraFrame frame)
        {
            frame = null;

            if (!TryAcquireImage(out var subsystem, out var image))
            {
                return false;
            }

            try
            {
                var conversion = BuildConversion(image);
                var size = image.GetConvertedDataSize(conversion);
                using var buffer = new NativeArray<byte>(size, Allocator.Temp);
                image.Convert(conversion, buffer);
                frame = BuildFrame(subsystem, conversion, image.timestamp, buffer.ToArray());
                return true;
            }
            catch (Exception e)
            {
                LastFailure = $"変換に失敗しました（{e.Message}）";
                return false;
            }
            finally
            {
                // 離し損ねると AR プラットフォーム側がメモリ切れになる。
                image.Dispose();
            }
        }

        public async UniTask<CameraFrame> AcquireAsync(CancellationToken token = default)
        {
            if (!TryAcquireImage(out var subsystem, out var image))
            {
                return null;
            }

            var conversion = BuildConversion(image);
            XRCpuImage.AsyncConversion request;
            try
            {
                request = image.ConvertAsync(conversion);
            }
            catch (Exception e)
            {
                LastFailure = $"変換を頼めませんでした（{e.Message}）";
                image.Dispose();
                return null;
            }

            // 元の画像は変換を頼んだ時点で手放してよい（AR Foundation の Image capture）。
            var timestamp = image.timestamp;
            image.Dispose();

            try
            {
                while (request.status == XRCpuImage.AsyncConversionStatus.Pending ||
                       request.status == XRCpuImage.AsyncConversionStatus.Processing)
                {
                    if (token.IsCancellationRequested)
                    {
                        return null;
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, token).SuppressCancellationThrow();
                }

                if (request.status != XRCpuImage.AsyncConversionStatus.Ready)
                {
                    LastFailure = $"変換が終わりませんでした（{request.status}）";
                    return null;
                }

                var data = request.GetData<byte>();
                return BuildFrame(subsystem, conversion, timestamp, data.ToArray());
            }
            finally
            {
                request.Dispose();
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }

        // ---------------------------------------------------------------- 中身

        /// <summary>
        /// 動いている Meta のカメラ subsystem を引く。
        /// <c>ARCameraManager</c>（MR テンプレートの rig に居る）が Start／Stop を持つので、
        /// ここでは見つけるだけ——勝手に Start すると管理の持ち主が2つになる。
        /// </summary>
        private static MetaOpenXRCameraSubsystem FindSubsystem()
        {
            Subsystems.Clear();
            SubsystemManager.GetSubsystems(Subsystems);

            foreach (var subsystem in Subsystems)
            {
                if (subsystem is MetaOpenXRCameraSubsystem meta)
                {
                    return meta;
                }
            }

            return null;
        }

        private bool TryAcquireImage(out MetaOpenXRCameraSubsystem subsystem, out XRCpuImage image)
        {
            image = default;
            subsystem = FindSubsystem();

            if (_disposed)
            {
                LastFailure = "カメラは閉じてあります";
                return false;
            }

            if (subsystem == null)
            {
                LastFailure = "カメラの subsystem がありません（Quest 3 以外・Camera Image Support が未設定）";
                return false;
            }

            if (!subsystem.running)
            {
                LastFailure = "カメラの subsystem が動いていません（ARCameraManager が無効）";
                return false;
            }

            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                LastFailure = "カメラの権限が下りていません（設定 → アプリ → 権限）";
                return false;
            }

            if (!subsystem.TryAcquireLatestCpuImage(out image))
            {
                LastFailure = "1枚も取れませんでした（まだ来ていない・カメラが止まっている）";
                return false;
            }

            LastFailure = string.Empty;
            return true;
        }

        /// <summary>
        /// 変換の指定。<c>RGBA32</c> へ落とし、長辺を <see cref="MaxOutputWidth"/> までに縮める。
        /// 反転は <see cref="OutputTransformation"/>（＝<c>MirrorX</c>）。
        /// </summary>
        private static XRCpuImage.ConversionParams BuildConversion(XRCpuImage image)
        {
            var conversion = new XRCpuImage.ConversionParams(
                image, TextureFormat.RGBA32, OutputTransformation);

            if (image.width > MaxOutputWidth && image.width > 0)
            {
                var height = Mathf.Max(1, Mathf.RoundToInt(image.height * (MaxOutputWidth / (float)image.width)));
                conversion.outputDimensions = new Vector2Int(MaxOutputWidth, height);
            }

            return conversion;
        }

        private static CameraFrame BuildFrame(
            XRCameraSubsystem subsystem, XRCpuImage.ConversionParams conversion,
            double timestamp, byte[] pixels)
        {
            CameraIntrinsics? intrinsics = null;
            if (subsystem != null && subsystem.TryGetIntrinsics(out var raw))
            {
                intrinsics = new CameraIntrinsics(raw.focalLength, raw.principalPoint, raw.resolution);
            }

            return new CameraFrame(
                pixels, conversion.outputDimensions.x, conversion.outputDimensions.y,
                conversion.outputFormat, timestamp, intrinsics);
        }
    }
}
