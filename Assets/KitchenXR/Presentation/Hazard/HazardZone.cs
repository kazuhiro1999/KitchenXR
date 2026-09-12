using UnityEngine;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 手の届かない場所（コンロ・オーブン等）の領域。持つのは**水平な上面の矩形**1枚で、
    /// 中心・幅・奥行・水平回り（yaw）・床からの高さ。立体ではないのが肝心——危ないのは
    /// 天板の上の空間であって箱の中身ではないし、矩形までの最短距離は閉じた式で書ける。
    ///
    /// 座標は世界のもの。保存のときだけ XR Origin 基準の相対へ直す（<see cref="HazardZoneFile"/>）。
    /// <see cref="Center"/> は上面の中心なので、床の線は <c>Center.y - Height</c> に引く。
    ///
    /// MonoBehaviour ではなく、場面に触らない値の型。距離の計算は EditMode で検算する。
    /// </summary>
    public sealed class HazardZone
    {
        /// <summary>これより小さい矩形は描き間違い（指の震え）として捨てる。</summary>
        public const float MinSideMeters = 0.15f;

        /// <summary>既定の種類（コンロ）。</summary>
        public const string StoveKind = "stove";

        public HazardZone(
            string id, string kind, Vector3 center, float sizeX, float sizeZ, float yawDegrees, float height)
        {
            Id = id;
            Kind = string.IsNullOrEmpty(kind) ? StoveKind : kind;
            Center = center;
            SizeX = Mathf.Max(0f, sizeX);
            SizeZ = Mathf.Max(0f, sizeZ);
            YawDegrees = yawDegrees;
            Height = height;
        }

        public string Id { get; }
        public string Kind { get; }

        /// <summary>上面の矩形の中心（世界）。</summary>
        public Vector3 Center { get; private set; }

        public float SizeX { get; private set; }
        public float SizeZ { get; private set; }

        /// <summary>矩形の水平回り（度）。囲み始めたときの頭の向きから決める。</summary>
        public float YawDegrees { get; }

        /// <summary>床から上面までの高さ（m）。床の線を引く高さを決める。</summary>
        public float Height { get; private set; }

        /// <summary>床の高さ（世界の y）。</summary>
        public float FloorY => Center.y - Height;

        /// <summary>
        /// 点から上面の矩形までの最短距離（m）。
        /// 矩形のローカルへ写して、はみ出した分（x・z）と高さ（y）の3辺で測る——
        /// 矩形の内側の真上なら水平の成分は 0 になり、距離＝高さの差になる。
        /// </summary>
        public float DistanceTo(Vector3 point)
        {
            var local = ToLocal(point);
            var dx = Mathf.Max(0f, Mathf.Abs(local.x) - SizeX / 2f);
            var dz = Mathf.Max(0f, Mathf.Abs(local.z) - SizeZ / 2f);
            return new Vector3(dx, local.y, dz).magnitude;
        }

        /// <summary>上面の矩形の上で点に一番近い場所（世界）。診断と試験用。</summary>
        public Vector3 ClosestPointOnTop(Vector3 point)
        {
            var local = ToLocal(point);
            local.x = Mathf.Clamp(local.x, -SizeX / 2f, SizeX / 2f);
            local.z = Mathf.Clamp(local.z, -SizeZ / 2f, SizeZ / 2f);
            local.y = 0f;
            return Center + Rotation * local;
        }

        private Quaternion Rotation => Quaternion.Euler(0f, YawDegrees, 0f);

        private Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(Rotation) * (world - Center);

        /// <summary>上面の四隅（世界。左手前から時計回り）。</summary>
        public void TopCorners(Vector3[] into) => Corners(into, Center.y);

        /// <summary>床に投影した四隅（世界）。</summary>
        public void FloorCorners(Vector3[] into) => Corners(into, FloorY);

        private void Corners(Vector3[] into, float y)
        {
            if (into == null || into.Length < 4)
            {
                return;
            }

            var hx = SizeX / 2f;
            var hz = SizeZ / 2f;
            var rotation = Rotation;

            into[0] = Corner(rotation, -hx, -hz, y);
            into[1] = Corner(rotation, hx, -hz, y);
            into[2] = Corner(rotation, hx, hz, y);
            into[3] = Corner(rotation, -hx, hz, y);
        }

        private Vector3 Corner(Quaternion rotation, float x, float z, float y)
        {
            var point = Center + rotation * new Vector3(x, 0f, z);
            point.y = y;
            return point;
        }

        /// <summary>
        /// 上面の高さを ±<paramref name="deltaMeters"/> ずらす（床は動かさない）。
        /// 実機では天板の高さがレイの当たりからずれるので、あとから手元のメニューで直せるように。
        /// </summary>
        public void OffsetHeight(float deltaMeters)
        {
            var center = Center;
            center.y += deltaMeters;
            Center = center;
            Height += deltaMeters;
        }

        /// <summary>
        /// ピンチの始点と終点を対角とする矩形を作る。矩形は <paramref name="yawDegrees"/> の
        /// 向きに立て、2点をその向きのローカルへ写してから外接の箱を取る（＝「水平にドラッグして
        /// 囲む」がそのまま矩形になる）。高さは始点の高さ、床は <paramref name="floorY"/>。
        /// </summary>
        /// <returns>短い辺が <see cref="MinSideMeters"/> 未満なら null（描き間違い）。</returns>
        public static HazardZone FromCorners(
            string id, string kind, Vector3 start, Vector3 end, float yawDegrees, float floorY)
        {
            var rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            var inverse = Quaternion.Inverse(rotation);

            var a = inverse * start;
            var b = inverse * end;

            var sizeX = Mathf.Abs(b.x - a.x);
            var sizeZ = Mathf.Abs(b.z - a.z);
            if (sizeX < MinSideMeters || sizeZ < MinSideMeters)
            {
                return null;
            }

            var localCenter = new Vector3((a.x + b.x) / 2f, start.y, (a.z + b.z) / 2f);
            var center = rotation * localCenter;
            center.y = start.y;

            return new HazardZone(id, kind, center, sizeX, sizeZ, yawDegrees, start.y - floorY);
        }
    }
}
