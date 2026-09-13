using UnityEngine;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>その瞬間の手——人差し指と親指の中点（世界）と、つまんでいるか。</summary>
    public readonly struct PinchSample
    {
        public PinchSample(bool isTracked, Vector3 position, bool isPinching)
        {
            IsTracked = isTracked;
            Position = position;
            IsPinching = isPinching && isTracked;
        }

        /// <summary>手の位置が取れているか。false のとき他の値は見ない。</summary>
        public bool IsTracked { get; }

        /// <summary>人差し指と親指の中点（世界）。</summary>
        public Vector3 Position { get; }

        public bool IsPinching { get; }

        public static readonly PinchSample None = new PinchSample(false, Vector3.zero, false);
    }

    /// <summary>
    /// ピンチの差し込み口。実機は <see cref="HandPinchSource"/>、PlayMode の検算は
    /// <see cref="ManualPinchSource"/>（<c>NullPassthrough</c> と同じ流儀）。
    /// </summary>
    public interface IPinchSource
    {
        /// <summary>今のフレームの手。毎フレーム1度だけ呼ばれる。</summary>
        PinchSample Read();
    }

    /// <summary>手で値を差し込むピンチ。試験と Editor の試し用。</summary>
    public sealed class ManualPinchSource : IPinchSource
    {
        private PinchSample _sample = PinchSample.None;

        /// <summary>つままずに手だけ動かす。</summary>
        public void Move(Vector3 position) =>
            _sample = new PinchSample(true, position, _sample.IsPinching);

        /// <summary>その場でつまむ（つまんだまま動かすときも同じ入口）。</summary>
        public void PinchAt(Vector3 position) => _sample = new PinchSample(true, position, true);

        /// <summary>指を離す（位置はそのまま）。</summary>
        public void Release() => _sample = new PinchSample(_sample.IsTracked, _sample.Position, false);

        /// <summary>手を見失う。</summary>
        public void Lose() => _sample = PinchSample.None;

        public PinchSample Read() => _sample;
    }
}
