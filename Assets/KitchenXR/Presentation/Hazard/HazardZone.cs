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
        /// <summary>これより短い辺・奥行きは描き間違い（指の震え）として捨てる。</summary>
        public const float MinSideMeters = 0.05f;

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

        /// <summary>矩形の水平回り（度）。手前の辺 A→B の向きそのもの（<see cref="YawFromEdge"/>）。</summary>
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
        /// 手前の辺 A→B の向き（度）。矩形のローカル +x がこの辺に沿う。
        /// 始点と終点の2点だけでは向きが決まらない（斜めの対角でも同じ矩形が引ける）ので、
        /// **向きは辺そのものから**取る——作図の1段目が辺なのはこのため。
        /// </summary>
        /// <remarks>辺が短すぎて向きが出ないときは 0。</remarks>
        public static float YawFromEdge(Vector3 a, Vector3 b)
        {
            var dx = b.x - a.x;
            var dz = b.z - a.z;
            if (dx * dx + dz * dz < 1e-8f)
            {
                return 0f;
            }

            // R(yaw) * Vector3.right が A→B に重なる yaw。
            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg - 90f;
        }

        /// <summary>辺 A→B に直角な水平の向き（矩形のローカル +z）。奥行きを測る軸。</summary>
        public static Vector3 DepthAxis(float yawDegrees) =>
            Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;

        /// <summary>辺 A→B の長さ（水平。高さの差は見ない）。</summary>
        public static float EdgeLength(Vector3 a, Vector3 b) =>
            new Vector2(b.x - a.x, b.z - a.z).magnitude;

        /// <summary>
        /// 手前の辺 A→B と、その辺に直角な奥行きから矩形を作る。
        /// <paramref name="depthMeters"/> は符号付き（正負どちらでも手のある側へ伸びる）。
        /// 上面の高さは A の高さ、床は <paramref name="floorY"/>。
        /// </summary>
        /// <returns>辺か奥行きが <see cref="MinSideMeters"/> 未満なら null（描き間違い）。</returns>
        public static HazardZone FromEdgeAndDepth(
            string id, string kind, Vector3 a, Vector3 b, float depthMeters, float floorY)
        {
            var width = EdgeLength(a, b);
            var depth = Mathf.Abs(depthMeters);
            if (width < MinSideMeters || depth < MinSideMeters)
            {
                return null;
            }

            var yaw = YawFromEdge(a, b);
            var center = (a + b) / 2f + DepthAxis(yaw) * (depthMeters / 2f);
            center.y = a.y;

            return new HazardZone(id, kind, center, width, depth, yaw, a.y - floorY);
        }
    }
}
