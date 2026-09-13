using UnityEngine;

namespace KitchenXR.Platform
{
    /// <summary>
    /// パススルーカメラから取れた1枚。<see cref="IPassthroughCamera"/> の戻り値で、
    /// 機種にも SDK にも依らない形（画素の並びと寸法と時刻）だけを持つ。
    ///
    /// 画素は <see cref="TextureFormat.RGBA32"/> か <see cref="TextureFormat.RGB24"/> の
    /// 上から下への行並び。<c>Texture2D.LoadRawTextureData</c> にそのまま渡せる
    /// （Quest の内部形式は YUV420 だが、変換は <c>Platform/MetaCamera/</c> の中で済ませる）。
    /// </summary>
    public sealed class CameraFrame
    {
        public CameraFrame(
            byte[] pixels, int width, int height, TextureFormat format,
            double timestampSeconds, CameraIntrinsics? intrinsics = null)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            Format = format;
            TimestampSeconds = timestampSeconds;
            Intrinsics = intrinsics;
        }

        public byte[] Pixels { get; }

        public int Width { get; }

        public int Height { get; }

        public TextureFormat Format { get; }

        /// <summary>撮影の時刻（秒）。遅延を測るために、表示した時刻と引き算する。</summary>
        public double TimestampSeconds { get; }

        /// <summary>
        /// 内部パラメータ。Meta は <c>XRCameraIntrinsics</c> を対応表に載せていないので、
        /// 返らないことがある（調査 §1.5）。画像座標 → 世界座標が要るまでは無くてよい。
        /// </summary>
        public CameraIntrinsics? Intrinsics { get; }

        public bool HasIntrinsics => Intrinsics.HasValue;

        public int BytesPerPixel => Format == TextureFormat.RGB24 ? 3 : 4;

        public int ExpectedByteCount => Width * Height * BytesPerPixel;

        /// <summary>寸法と画素の数が噛み合っているか（噛み合わない絵は貼らない）。</summary>
        public bool IsValid =>
            Width > 0 && Height > 0 && Pixels != null && Pixels.Length >= ExpectedByteCount;

        /// <summary>札に出す寸法（「640×480」）。</summary>
        public string SizeText => $"{Width}×{Height}";
    }

    /// <summary>カメラの内部パラメータ（焦点距離・主点・その画素の寸法）。</summary>
    public readonly struct CameraIntrinsics
    {
        public CameraIntrinsics(Vector2 focalLength, Vector2 principalPoint, Vector2Int resolution)
        {
            FocalLength = focalLength;
            PrincipalPoint = principalPoint;
            Resolution = resolution;
        }

        public Vector2 FocalLength { get; }

        public Vector2 PrincipalPoint { get; }

        public Vector2Int Resolution { get; }
    }
}
