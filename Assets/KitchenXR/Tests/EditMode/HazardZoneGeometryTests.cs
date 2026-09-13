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
        public void 対角の2点から矩形を作る()
        {
            var zone = HazardZone.FromCorners(
                "z", HazardZone.StoveKind,
                new Vector3(-0.3f, 0.9f, -0.25f), new Vector3(0.3f, 0.9f, 0.25f), 0f, 0f);

            Assert.IsNotNull(zone);
            Assert.AreEqual(0.6f, zone.SizeX, 1e-4f);
            Assert.AreEqual(0.5f, zone.SizeZ, 1e-4f);
            Assert.AreEqual(0f, zone.Center.x, 1e-4f);
            Assert.AreEqual(0.9f, zone.Center.y, 1e-4f, "上面の高さは始点の高さ。");
            Assert.AreEqual(0.9f, zone.Height, 1e-4f, "床（0m）からの高さ。");
            Assert.AreEqual(0f, zone.FloorY, 1e-4f);
        }

        [Test]
        public void 向きを付けて囲むと矩形もその向きに立つ()
        {
            const float yaw = 30f;
            var start = new Vector3(0f, 0.9f, 0f);
            var end = start + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.6f, 0f, 0.4f);

            var zone = HazardZone.FromCorners("z", HazardZone.StoveKind, start, end, yaw, 0f);

            Assert.IsNotNull(zone);
            Assert.AreEqual(0.6f, zone.SizeX, 1e-3f, "囲んだ向きで測った幅になるはずです。");
            Assert.AreEqual(0.4f, zone.SizeZ, 1e-3f);

            // 始点と終点はどちらも矩形の角なので、上面までの距離は 0。
            Assert.AreEqual(0f, zone.DistanceTo(start), 1e-3f);
            Assert.AreEqual(0f, zone.DistanceTo(end), 1e-3f);
        }

        [Test]
        public void 小さすぎる矩形は作らない()
        {
            var start = new Vector3(0f, 0.9f, 0f);
            var tiny = start + new Vector3(0.05f, 0f, 0.05f);

            Assert.IsNull(
                HazardZone.FromCorners("z", HazardZone.StoveKind, start, tiny, 0f, 0f),
                "指の震えほどの大きさは領域にしない。");
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
        /// 「離したら確定してよいか」の判定が <see cref="HazardZone.FromCorners"/> と食い違わないこと。
        /// 食い違うと、離した瞬間に確定したのに領域ができない（か、その逆）ことになる。
        /// </summary>
        [Test]
        public void 確定してよい大きさの判定が矩形を作れるかと一致する()
        {
            var start = new Vector3(0f, 0.9f, 0f);

            foreach (var yaw in new[] { 0f, 30f, 175f })
            {
                foreach (var local in new[]
                         {
                             new Vector3(0.6f, 0f, 0.4f),   // 十分
                             new Vector3(-0.6f, 0f, -0.4f), // 逆向きでも同じ
                             new Vector3(0.6f, 0f, 0.05f),  // 細すぎ
                             new Vector3(0.05f, 0f, 0.05f), // 指の震え
                         })
                {
                    var end = start + Quaternion.Euler(0f, yaw, 0f) * local;
                    var zone = HazardZone.FromCorners("z", HazardZone.StoveKind, start, end, yaw, 0f);

                    Assert.AreEqual(
                        zone != null, HazardZone.IsLargeEnough(start, end, yaw),
                        $"yaw {yaw}・{local} で判定が食い違います。");
                }
            }
        }
    }
}
