using System.Threading;
using Cysharp.Threading.Tasks;

namespace KitchenXR.Platform.Null
{
    /// <summary>
    /// カメラの無い受け皿（Editor・非対応機）。Quest 3 以外と XR Simulator はここへ落ちる。
    /// 何も取らず、理由だけを持つ——板はその文字を札に出す。
    /// </summary>
    public sealed class NullPassthroughCamera : IPassthroughCamera
    {
        public const string DefaultReason = "カメラは実機でだけ（Editor の XR Simulator は非対応）";

        public NullPassthroughCamera(string reason = DefaultReason)
        {
            LastFailure = string.IsNullOrEmpty(reason) ? DefaultReason : reason;
        }

        public bool IsSupported => false;

        public string LastFailure { get; }

        public bool PermissionJustGranted => false;

        public string TransformationText => string.Empty;

        public UniTask<bool> RequestPermissionAsync(CancellationToken token = default) =>
            UniTask.FromResult(false);

        public bool Restart() => false;

        public bool TryAcquire(out CameraFrame frame)
        {
            frame = null;
            return false;
        }

        public UniTask<CameraFrame> AcquireAsync(CancellationToken token = default) =>
            UniTask.FromResult<CameraFrame>(null);

        public void Dispose()
        {
        }
    }
}
