using KitchenXR.Presentation.Hazard;
using NUnit.Framework;
using UnityEngine;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 領域の形と距離の検算。実機で確かめられないのはここ——手が近づいたときに線が出るかは
    /// 「点から上面の矩形までの最短距離」が正しいかで決まるので、机の上で縛る。
    /// </summary>
    public sealed class HazardZoneGeometryTests
    {
        /// <summary>床 0m・上面 0.9m・60×50cm の領域（一般的なコンロ）。</summary>
        private static HazardZone Stove() => new HazardZone(
            "stove", HazardZone.StoveKind, new Vector3(0f, 0.9f, 0f), 0.6f, 0.5f, 0f, 0.9f);

        [Test]
        public void 矩形の真上では距離が高さの差になる()
        {
            var zone = Stove();

            Assert.AreEqual(0f, zone.DistanceTo(new Vector3(0f, 0.9f, 0f)), 1e-4f, "上面の中心は 0。");
            Assert.AreEqual(0.3f, zone.DistanceTo(new Vector3(0f, 1.2f, 0f)), 1e-4f, "30cm 上。");
            Assert.AreEqual(0.1f, zone.DistanceTo(new Vector3(0.25f, 1.0f, 0.2f)), 1e-4f,
                "矩形の内側の真上なら水平の成分は 0。");
        }

        [Test]
        public void 矩形の外では水平のはみ出しも足す()
        {
            var zone = Stove();

            // x は 0.3 まで、z は 0.25 まで。0.7 は 0.4 はみ出す。
            Assert.AreEqual(0.4f, zone.DistanceTo(new Vector3(0.7f, 0.9f, 0f)), 1e-4f);

            // 角の外（x で 0.3・z で 0.4 はみ出し・同じ高さ）。
            Assert.AreEqual(0.5f, zone.DistanceTo(new Vector3(0.6f, 0.9f, 0.65f)), 1e-3f);
        }

        [Test]
        public void 向きを付けた矩形は回った先で測る()
        {
            var zone = new HazardZone(
                "z", HazardZone.StoveKind, new Vector3(0f, 0.9f, 0f), 1.0f, 0.2f, 90f, 0.9f);

            // 90° 回すと長い辺が z 方向へ向く。z = 0.5 は端、z = 0.7 は 20cm 外。
            Assert.AreEqual(0f, zone.DistanceTo(new Vector3(0f, 0.9f, 0.5f)), 1e-3f);
            Assert.AreEqual(0.2f, zone.DistanceTo(new Vector3(0f, 0.9f, 0.7f)), 1e-3f);
        }

        [Test]
        public void 上面の一番近い点を返す()
        {
            var zone = Stove();
            var closest = zone.ClosestPointOnTop(new Vector3(2f, 1.5f, 0f));

            Assert.AreEqual(0.3f, closest.x, 1e-4f, "矩形の縁で止まるはずです。");
            Assert.AreEqual(0.9f, closest.y, 1e-4f, "上面の高さ。");
        }

        [Test]
        public void 手前の辺と奥行きから矩形を作る()
        {
            // 手前の辺は x 方向に 60cm、奥行きは +z に 50cm。
            var zone = HazardZone.FromEdgeAndDepth(
                "z", HazardZone.StoveKind,
                new Vector3(-0.3f, 0.9f, 0f), new Vector3(0.3f, 0.9f, 0f), 0.5f, 0f);

            Assert.IsNotNull(zone);
            Assert.AreEqual(0.6f, zone.SizeX, 1e-4f, "幅は辺の長さ。");
            Assert.AreEqual(0.5f, zone.SizeZ, 1e-4f, "奥行きは辺に直角な方向の長さ。");
            Assert.AreEqual(0f, zone.Center.x, 1e-4f);
            Assert.AreEqual(0.25f, zone.Center.z, 1e-4f, "中心は辺から奥行きの半分だけ奥。");
            Assert.AreEqual(0.9f, zone.Center.y, 1e-4f, "上面の高さは辺の高さ。");
            Assert.AreEqual(0.9f, zone.Height, 1e-4f, "床（0m）からの高さ。");
            Assert.AreEqual(0f, zone.FloorY, 1e-4f);
        }

        /// <summary>
        /// 奥行きの符号は「手のある側」。負でも同じ大きさの矩形が、辺の反対側にできること。
        /// </summary>
        [Test]
        public void 奥行きが負なら辺の反対側に伸びる()
        {
            var a = new Vector3(-0.3f, 0.9f, 0f);
            var b = new Vector3(0.3f, 0.9f, 0f);

            var far = HazardZone.FromEdgeAndDepth("z", HazardZone.StoveKind, a, b, 0.5f, 0f);
            var near = HazardZone.FromEdgeAndDepth("z", HazardZone.StoveKind, a, b, -0.5f, 0f);

            Assert.IsNotNull(near);
            Assert.AreEqual(far.SizeX, near.SizeX, 1e-4f);
            Assert.AreEqual(far.SizeZ, near.SizeZ, 1e-4f);
            Assert.AreEqual(-far.Center.z, near.Center.z, 1e-4f, "辺を挟んで反対側のはずです。");
        }

        /// <summary>
        /// **向きは辺そのもの**から来る（頭の向きではない）。斜めに引いた辺の上に矩形が
        /// 立ち、辺の両端はどちらも矩形の角になること——実機で矩形が斜めに転んだ件の縛り。
        /// </summary>
        [Test]
        public void 斜めの辺の上に矩形が立つ()
        {
            const float yaw = 30f;
            var a = new Vector3(0f, 0.9f, 0f);
            var b = a + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.6f, 0f, 0f);

            var zone = HazardZone.FromEdgeAndDepth("z", HazardZone.StoveKind, a, b, 0.4f, 0f);

            Assert.IsNotNull(zone);
            Assert.AreEqual(0.6f, zone.SizeX, 1e-3f);
            Assert.AreEqual(0.4f, zone.SizeZ, 1e-3f);
            Assert.AreEqual(yaw, Mathf.DeltaAngle(0f, zone.YawDegrees), 1e-3f,
                "矩形の向きが辺の向きと違います。");

            // 辺の両端はどちらも矩形の角なので、上面までの距離は 0。
            Assert.AreEqual(0f, zone.DistanceTo(a), 1e-3f);
            Assert.AreEqual(0f, zone.DistanceTo(b), 1e-3f);
        }

        /// <summary>回した矩形への最短距離。局所座標へ写し損ねると斜めの矩形だけ狂う。</summary>
        [Test]
        public void 回した矩形への最短距離を辺の向きで測る()
        {
            const float yaw = 30f;
            var a = new Vector3(0f, 0.9f, 0f);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var b = a + rotation * new Vector3(0.6f, 0f, 0f);

            var zone = HazardZone.FromEdgeAndDepth("z", HazardZone.StoveKind, a, b, 0.4f, 0f);

            // 辺に沿って端から 20cm 外（局所 x の外）。
            Assert.AreEqual(
                0.2f, zone.DistanceTo(b + rotation * new Vector3(0.2f, 0f, 0f)), 1e-3f);

            // 奥へ 30cm 行き過ぎた点（局所 z の外）。
            Assert.AreEqual(
                0.3f, zone.DistanceTo(a + rotation * new Vector3(0.3f, 0f, 0.7f)), 1e-3f);

            // 矩形の内側の真上（水平の成分は 0）。
            Assert.AreEqual(
                0.25f, zone.DistanceTo(a + rotation * new Vector3(0.3f, 0.25f, 0.2f)), 1e-3f);

            // 角の外（辺の向きで x に 30cm・z に 40cm はみ出す）。
            Assert.AreEqual(
                0.5f, zone.DistanceTo(a + rotation * new Vector3(-0.3f, 0f, -0.4f)), 1e-3f);
        }

        [Test]
        public void 小さすぎる辺と奥行きは矩形にしない()
        {
            var a = new Vector3(0f, 0.9f, 0f);

            Assert.IsNull(
                HazardZone.FromEdgeAndDepth(
                    "z", HazardZone.StoveKind, a, a + new Vector3(0.04f, 0f, 0f), 0.5f, 0f),
                "5cm 未満の辺は領域にしない。");

            Assert.IsNull(
                HazardZone.FromEdgeAndDepth(
                    "z", HazardZone.StoveKind, a, a + new Vector3(0.6f, 0f, 0f), 0.04f, 0f),
                "5cm 未満の奥行きは領域にしない。");

            Assert.IsNotNull(
                HazardZone.FromEdgeAndDepth(
                    "z", HazardZone.StoveKind, a, a + new Vector3(0.06f, 0f, 0f), 0.06f, 0f),
                "6cm 角なら作れるはずです（境目は 5cm）。");
        }

        [Test]
        public void 高さをずらすと床は動かない()
        {
            var zone = Stove();
            zone.OffsetHeight(0.05f);

            Assert.AreEqual(0.95f, zone.Center.y, 1e-4f);
            Assert.AreEqual(0.95f, zone.Height, 1e-4f);
            Assert.AreEqual(0f, zone.FloorY, 1e-4f, "床の線の高さは変わらないはずです。");
        }

        [Test]
        public void 床と上面の四隅が同じ形で高さだけ違う()
        {
            var zone = Stove();
            var top = new Vector3[4];
            var floor = new Vector3[4];

            zone.TopCorners(top);
            zone.FloorCorners(floor);

            for (var i = 0; i < 4; i++)
            {
                Assert.AreEqual(top[i].x, floor[i].x, 1e-4f);
                Assert.AreEqual(top[i].z, floor[i].z, 1e-4f);
                Assert.AreEqual(0.9f, top[i].y, 1e-4f);
                Assert.AreEqual(0f, floor[i].y, 1e-4f);
            }
        }

        /// <summary>
        /// どの向きの辺でも、辺の長さ＝<c>SizeX</c>・手で測った奥行き＝<c>SizeZ</c> になること。
        /// 作図が使う <see cref="HazardZone.YawFromEdge"/>・<see cref="HazardZone.DepthAxis"/> と
        /// <see cref="HazardZone.FromEdgeAndDepth"/> が食い違うと、離した瞬間に形が変わる。
        /// </summary>
        [Test]
        public void どの向きの辺でも辺と奥行きがそのまま矩形の辺になる()
        {
            var a = new Vector3(0.2f, 0.9f, -0.4f);

            foreach (var yaw in new[] { 0f, 30f, 175f, -120f })
            {
                var rotation = Quaternion.Euler(0f, yaw, 0f);
                var b = a + rotation * new Vector3(0.6f, 0f, 0f);

                Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, HazardZone.YawFromEdge(a, b)), 1e-3f,
                    $"yaw {yaw} の辺から向きが出ていません。");
                Assert.AreEqual(0.6f, HazardZone.EdgeLength(a, b), 1e-4f);

                foreach (var depth in new[] { 0.4f, -0.4f })
                {
                    // 作図と同じ道筋——手の位置を辺に直角な軸へ射影して奥行きにする。
                    var hand = a + rotation * new Vector3(0.1f, 0f, depth);
                    var measured = Vector3.Dot(
                        hand - a, HazardZone.DepthAxis(HazardZone.YawFromEdge(a, b)));
                    Assert.AreEqual(depth, measured, 1e-3f, $"yaw {yaw} で奥行きの射影が狂います。");

                    var zone = HazardZone.FromEdgeAndDepth(
                        "z", HazardZone.StoveKind, a, b, measured, 0f);

                    Assert.IsNotNull(zone);
                    Assert.AreEqual(0.6f, zone.SizeX, 1e-3f);
                    Assert.AreEqual(0.4f, zone.SizeZ, 1e-3f);
                    Assert.AreEqual(0f, zone.DistanceTo(a), 1e-3f, "辺の端が矩形の角になっていません。");
                    Assert.AreEqual(0f, zone.DistanceTo(b), 1e-3f);
                }
            }
        }
    }
}
