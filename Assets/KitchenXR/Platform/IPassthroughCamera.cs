using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace KitchenXR.Platform
{
    /// <summary>
    /// パススルーカメラから1枚もらう口。実装は <c>Platform/MetaCamera/</c>（Quest 3）と
    /// <c>Platform/Null/</c>（Editor・非対応機）だけ——板はこの口しか見ない。
    ///
    /// Editor では絶対に絵が出ない（XR Simulator はカメラ非対応）。だから
    /// <see cref="IsSupported"/> が false のときの道筋（札に理由を出す）が本筋の半分で、
    /// 「実機でだけ動くもの」を Editor で確かめられる形にしてある。
    ///
    /// <see cref="Dispose"/> は必ず呼ぶ。取った画像を離し損ねると AR プラットフォーム側が
    /// メモリ切れになる（AR Foundation の Image capture の注意）。
    /// </summary>
    public interface IPassthroughCamera : IDisposable
    {
        /// <summary>この端末・この構成でカメラ画像が取れるか（権限とは別）。</summary>
        bool IsSupported { get; }

        /// <summary>最後に失敗した理由（札に出す一言）。何も起きていなければ空。</summary>
        string LastFailure { get; }

        /// <summary>
        /// 権限が今あるか（**訊かない**）。設定で有効のまま起動したときに
        /// 「後から拒否へ戻された」を見分けるのに使う——ここで訊いてしまうと、
        /// 拒否した人に起動のたびダイアログが出る。
        /// </summary>
        bool HasPermission { get; }

        /// <summary>
        /// **この呼び出しで**権限が下りたか（起動時から許されていたなら false）。
        /// 実機では許可の直後の 1 回は取れないことがあるので、札の文言を変えるのに使う。
        /// </summary>
        bool PermissionJustGranted { get; }

        /// <summary>絵に掛けた反転（札に出す一言。例「反転: X」）。無ければ空。</summary>
        string TransformationText { get; }

        /// <summary>
        /// 実行時の権限（<c>horizonos.permission.HEADSET_CAMERA</c>）を求める。
        /// manifest に入っているだけでは足りず、自分で呼ぶ必要がある（調査 §1.3）。
        /// 既に許されていれば即 true。
        ///
        /// **下りた直後にカメラの口を起こし直してはいけない**——パススルーの映像が
        /// カメラの持ち主（<c>ARCameraManager</c>）にぶら下がっているので、無効にすると
        /// MR の背景が黒いまま戻りません（実測 2026-09-13。調査 §7）。
        /// 権限が無いまま始まった subsystem はそのセッションの間ずっと 1 枚も返さないので、
        /// **権限は起動時に求める**（AR セッションが立つ前）のが唯一の道です。
        /// </summary>
        UniTask<bool> RequestPermissionAsync(CancellationToken token = default);

        /// <summary>
        /// 今ある最新の1枚を取る。取れなければ false（理由は <see cref="LastFailure"/>）。
        /// 変換も後始末もこの中で終わる——呼び出し側に <c>XRCpuImage</c> を渡さない。
        /// </summary>
        bool TryAcquire(out CameraFrame frame);

        /// <summary>
        /// 同じことを、変換だけ別スレッドで行う版（連写で使う）。
        /// 取れなければ null。取得そのものは主スレッドで起きる（SDK の決まり）が、
        /// YUV → RGBA32 の変換で 1 フレーム止めないためにこちらを用意してある。
        /// </summary>
        UniTask<CameraFrame> AcquireAsync(CancellationToken token = default);
    }
}
