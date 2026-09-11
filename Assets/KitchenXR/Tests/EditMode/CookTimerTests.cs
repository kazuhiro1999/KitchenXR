using KitchenXR.Domain;
using NUnit.Framework;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>CookTimer 単体。時計は外から渡すので固定した秒数で試験できる。</summary>
    public class CookTimerTests
    {
        [Test]
        public void 開始前はRunningでなく残りは満タン()
        {
            var timer = new CookTimer(stepIndex: 1, durationSec: 180);

            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(0, timer.Elapsed(1000));
        }

        [Test]
        public void 開始すると経過と残りが計算される()
        {
            var timer = new CookTimer(1, 180);

            timer.Start(nowSeconds: 1000);

            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(0, timer.Elapsed(1000));
            Assert.AreEqual(180, timer.Remaining(1000));
            Assert.AreEqual(60, timer.Elapsed(1060));
            Assert.AreEqual(120, timer.Remaining(1060));
        }

        [Test]
        public void 残りは負にならない()
        {
            var timer = new CookTimer(1, 10);

            timer.Start(0);

            Assert.AreEqual(0, timer.Remaining(999));
        }

        [Test]
        public void IsElapsedは期限を過ぎたらtrue()
        {
            var timer = new CookTimer(1, 10);
            timer.Start(0);

            Assert.IsFalse(timer.IsElapsed(9));
            Assert.IsTrue(timer.IsElapsed(10));
            Assert.IsTrue(timer.IsElapsed(11));
        }

        [Test]
        public void Stopで止められる()
        {
            var timer = new CookTimer(1, 10);
            timer.Start(0);

            timer.Stop();

            Assert.IsFalse(timer.IsRunning);
        }

        [Test]
        public void Startをやり直すと開始時刻が更新される()
        {
            var timer = new CookTimer(1, 10);
            timer.Start(0);
            timer.Stop();

            timer.Start(100);

            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(10, timer.Remaining(100));
        }
    }
}
