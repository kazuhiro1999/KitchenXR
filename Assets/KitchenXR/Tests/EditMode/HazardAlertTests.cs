using KitchenXR.Presentation.Hazard;
using NUnit.Framework;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// 3段の閾値（手 20cm／手 40cm／頭 60cm）と、境界の点滅を止める 5cm のヒステリシス。
    /// 実機では「線が出たり消えたりする」として現れるので、机の上で縛る。
    /// </summary>
    public sealed class HazardAlertTests
    {
        private const float Far = float.PositiveInfinity;

        [Test]
        public void 手が20cmで赤になる()
        {
            var state = new HazardAlertState();

            Assert.AreEqual(HazardAlertLevel.Near, state.Evaluate(0.20f, Far));
            Assert.IsTrue(state.EnteredNear, "段が上がった1回だけ音を鳴らす契機になるはずです。");
        }

        [Test]
        public void 手が40cmで琥珀になる()
        {
            var state = new HazardAlertState();

            Assert.AreEqual(HazardAlertLevel.Watch, state.Evaluate(0.40f, Far));
            Assert.IsFalse(state.EnteredNear, "琥珀では音を鳴らさない。");
        }

        [Test]
        public void 頭が60cmでも琥珀になる()
        {
            var state = new HazardAlertState();

            Assert.AreEqual(HazardAlertLevel.Watch, state.Evaluate(Far, 0.60f),
                "手が遠くても、頭が近ければ領域を知らせる。");
        }

        [Test]
        public void 離れていれば何も出さない()
        {
            var state = new HazardAlertState();

            Assert.AreEqual(HazardAlertLevel.Off, state.Evaluate(0.5f, 0.8f));
        }

        [Test]
        public void 赤から戻るには5cm余分に離れる()
        {
            var state = new HazardAlertState();
            state.Evaluate(0.19f, Far);
            Assert.AreEqual(HazardAlertLevel.Near, state.Level);

            // 素の閾値（20cm）をわずかに越えただけでは赤のまま。
            Assert.AreEqual(HazardAlertLevel.Near, state.Evaluate(0.23f, Far),
                "境界の上下 1cm で赤と琥珀が入れ替わると点滅する。");

            // 25cm を越えたら琥珀へ。
            Assert.AreEqual(HazardAlertLevel.Watch, state.Evaluate(0.26f, Far));
        }

        [Test]
        public void 琥珀から戻るにも5cm余分に離れる()
        {
            var state = new HazardAlertState();
            state.Evaluate(0.39f, Far);
            Assert.AreEqual(HazardAlertLevel.Watch, state.Level);

            Assert.AreEqual(HazardAlertLevel.Watch, state.Evaluate(0.43f, Far));
            Assert.AreEqual(HazardAlertLevel.Off, state.Evaluate(0.46f, Far));
        }

        [Test]
        public void 頭の側の琥珀にもヒステリシスが効く()
        {
            var state = new HazardAlertState();
            state.Evaluate(Far, 0.59f);

            Assert.AreEqual(HazardAlertLevel.Watch, state.Evaluate(Far, 0.63f));
            Assert.AreEqual(HazardAlertLevel.Off, state.Evaluate(Far, 0.66f));
        }

        [Test]
        public void 段を上がるときは素の閾値のまま()
        {
            var state = new HazardAlertState();

            // 離れている → 41cm ではまだ何も出ない（＋5cm は下がるときだけ）。
            Assert.AreEqual(HazardAlertLevel.Off, state.Evaluate(0.41f, Far));
            Assert.AreEqual(HazardAlertLevel.Watch, state.Evaluate(0.39f, Far));

            // 琥珀 → 21cm ではまだ赤にならない。
            Assert.AreEqual(HazardAlertLevel.Watch, state.Evaluate(0.21f, Far));
            Assert.AreEqual(HazardAlertLevel.Near, state.Evaluate(0.19f, Far));
        }

        [Test]
        public void 近づいている間は音の契機が1度だけ立つ()
        {
            var state = new HazardAlertState();

            state.Evaluate(0.15f, Far);
            Assert.IsTrue(state.EnteredNear);

            state.Evaluate(0.10f, Far);
            Assert.IsFalse(state.EnteredNear, "赤のまま近づいても2度目は鳴らさない。");

            state.Evaluate(0.8f, Far);
            state.Evaluate(0.10f, Far);
            Assert.IsTrue(state.EnteredNear, "離れて入り直せばまた鳴る（間隔の下限は HazardSound 側）。");
        }

        [Test]
        public void 作り直したら段を忘れる()
        {
            var state = new HazardAlertState();
            state.Evaluate(0.1f, Far);

            state.Reset();

            Assert.AreEqual(HazardAlertLevel.Off, state.Level);
            Assert.IsFalse(state.EnteredNear);
        }
    }
}
