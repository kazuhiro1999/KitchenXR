using KitchenXR.Platform;
using KitchenXR.Platform.Null;
using NUnit.Framework;
using UnityEngine;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// カメラの1枚（<see cref="CameraFrame"/>）の純粋な部分と、Editor の受け皿の検算。
    ///
    /// 実機でしか動かないのは「取る」ところだけで、寸法・時刻・画素の数の噛み合いは
    /// ここで確かめられる——噛み合っていない絵を <c>Texture2D</c> に貼ると実機で落ちる。
    /// </summary>
    public class CameraFrameTests
    {
        private static CameraFrame Frame(int width, int height, double timestamp = 12.5d,
            TextureFormat format = TextureFormat.RGBA32, int? byteCount = null)
        {
            var bytes = byteCount ?? width * height * (format == TextureFormat.RGB24 ? 3 : 4);
            return new CameraFrame(new byte[bytes], width, height, format, timestamp);
        }

        [Test]
        public void 寸法と時刻をそのまま持つ()
        {
            var frame = Frame(640, 480, 3.25d);

            Assert.AreEqual(640, frame.Width);
            Assert.AreEqual(480, frame.Height);
            Assert.AreEqual(3.25d, frame.TimestampSeconds, 1e-9);
            Assert.AreEqual("640×480", frame.SizeText);
        }

        [Test]
        public void RGBA32は1画素4バイトRGB24は3バイト()
        {
            Assert.AreEqual(4, Frame(2, 2).BytesPerPixel);
            Assert.AreEqual(3, Frame(2, 2, format: TextureFormat.RGB24).BytesPerPixel);

            Assert.AreEqual(640 * 480 * 4, Frame(640, 480).ExpectedByteCount);
            Assert.AreEqual(640 * 480 * 3, Frame(640, 480, format: TextureFormat.RGB24).ExpectedByteCount);
        }

        /// <summary>
        /// 画素が足りない絵は貼らない（<c>Texture2D.LoadRawTextureData</c> は
        /// 足りないまま渡すと実機で落ちる）。
        /// </summary>
        [Test]
        public void 画素が足りなければ無効とする()
        {
            Assert.IsTrue(Frame(4, 4).IsValid);
            Assert.IsFalse(Frame(4, 4, byteCount: 4 * 4 * 4 - 1).IsValid, "画素が1バイト足りないのに有効です。");
            Assert.IsFalse(Frame(0, 4).IsValid, "幅 0 が有効になっています。");
            Assert.IsFalse(new CameraFrame(null, 4, 4, TextureFormat.RGBA32, 0d).IsValid);

            // 多いぶんには構わない（行の詰め物が入ることがある）。
            Assert.IsTrue(Frame(4, 4, byteCount: 4 * 4 * 4 + 16).IsValid);
        }

        [Test]
        public void 内部パラメータは在ることも無いこともある()
        {
            Assert.IsFalse(Frame(8, 8).HasIntrinsics, "既定で内部パラメータが在ることになっています。");

            var intrinsics = new CameraIntrinsics(
                new Vector2(500f, 500f), new Vector2(320f, 240f), new Vector2Int(640, 480));
            var frame = new CameraFrame(new byte[8 * 8 * 4], 8, 8, TextureFormat.RGBA32, 1d, intrinsics);

            Assert.IsTrue(frame.HasIntrinsics);
            Assert.AreEqual(640, frame.Intrinsics.Value.Resolution.x);
            Assert.AreEqual(320f, frame.Intrinsics.Value.PrincipalPoint.x, 1e-4f);
        }

        /// <summary>
        /// Editor の受け皿。XR Simulator はカメラ非対応なので、Editor では必ずここへ落ちる
        /// ——「非対応」の理由を持っていることが板の札の中身になる。
        /// </summary>
        [Test]
        public void Editorの受け皿は何も取らず理由を持つ()
        {
            using var camera = new NullPassthroughCamera();

            Assert.IsFalse(camera.IsSupported);
            Assert.IsFalse(camera.TryAcquire(out var frame));
            Assert.IsNull(frame);
            Assert.IsNotEmpty(camera.LastFailure, "非対応の理由が空です（札に何も出せません）。");
        }
    }
}
