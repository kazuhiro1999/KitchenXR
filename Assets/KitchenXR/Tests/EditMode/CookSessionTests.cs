using System.Collections.Generic;
using KitchenXR.Domain;
using NUnit.Framework;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>
    /// CookSession の状態機械（設計 §5）: Next/Prev・進捗・境界（最初で Prev・最後で Next）・タイマー。
    /// </summary>
    public class CookSessionTests
    {
        private static Recipe MakeRecipe(int stepCount = 3, int? timerSecOnFirstStep = null)
        {
            var phases = new List<Phase> { new Phase("p1", "下ごしらえ") };
            var steps = new List<Step>();
            for (var i = 1; i <= stepCount; i++)
            {
                steps.Add(new Step(
                    i, "p1", $"工程{i}", $"説明{i}", null,
                    new List<string>(),
                    i == 1 ? timerSecOnFirstStep : null,
                    CompletionType.Manual,
                    new List<string>()));
            }

            return new Recipe("R", "試験レシピ", null, null, 1, 1,
                new List<Ingredient>(), new List<string>(), phases, steps);
        }

        [Test]
        public void 開始直後は1工程目が現在で完了していない()
        {
            var session = new CookSession(MakeRecipe());

            Assert.AreEqual(1, session.Current);
            Assert.IsFalse(session.IsComplete);
            Assert.AreEqual("工程1", session.CurrentStep.Title);
            Assert.AreEqual("工程2", session.NextStep.Title);
        }

        [Test]
        public void Nextで工程が進む()
        {
            var session = new CookSession(MakeRecipe());

            session.Apply(SessionEvent.NextRequested.Instance);

            Assert.AreEqual(2, session.Current);
            Assert.AreEqual("工程2", session.CurrentStep.Title);
        }

        [Test]
        public void 最初の工程でPrevしても1工程目のまま()
        {
            var session = new CookSession(MakeRecipe());

            session.Apply(SessionEvent.PrevRequested.Instance);

            Assert.AreEqual(1, session.Current);
            Assert.IsFalse(session.IsComplete);
        }

        [Test]
        public void 最後の工程でNextするとIsCompleteになる()
        {
            var session = new CookSession(MakeRecipe(3));

            session.Apply(SessionEvent.NextRequested.Instance); // 1 -> 2
            session.Apply(SessionEvent.NextRequested.Instance); // 2 -> 3
            session.Apply(SessionEvent.NextRequested.Instance); // 3 -> 完了

            Assert.IsTrue(session.IsComplete);
            Assert.IsNull(session.CurrentStep);
            Assert.IsNull(session.NextStep);
        }

        [Test]
        public void 完了後にNextしても何も変わらない()
        {
            var session = new CookSession(MakeRecipe(1));

            session.Apply(SessionEvent.NextRequested.Instance); // 完了
            session.Apply(SessionEvent.NextRequested.Instance); // 何もしない

            Assert.IsTrue(session.IsComplete);
            Assert.AreEqual(1, session.Current);
        }

        [Test]
        public void 完了後にPrevすると完了が取り消される()
        {
            var session = new CookSession(MakeRecipe(2));

            session.Apply(SessionEvent.NextRequested.Instance); // 1 -> 2
            session.Apply(SessionEvent.NextRequested.Instance); // 2 -> 完了
            session.Apply(SessionEvent.PrevRequested.Instance); // 完了取り消し

            Assert.IsFalse(session.IsComplete);
            Assert.AreEqual(2, session.Current);
            Assert.AreEqual("工程2", session.CurrentStep.Title);
        }

        [Test]
        public void 戻るは常に可能で連続で押しても1未満にはならない()
        {
            var session = new CookSession(MakeRecipe(3));

            session.Apply(SessionEvent.PrevRequested.Instance);
            session.Apply(SessionEvent.PrevRequested.Instance);
            session.Apply(SessionEvent.PrevRequested.Instance);

            Assert.AreEqual(1, session.Current);
        }

        [Test]
        public void Progressは済んだ工程割る全工程()
        {
            var session = new CookSession(MakeRecipe(4));

            Assert.AreEqual(0.0, session.Progress, 1e-9);

            session.Apply(SessionEvent.NextRequested.Instance); // 済み1
            Assert.AreEqual(0.25, session.Progress, 1e-9);

            session.Apply(SessionEvent.NextRequested.Instance); // 済み2
            session.Apply(SessionEvent.NextRequested.Instance); // 済み3
            session.Apply(SessionEvent.NextRequested.Instance); // 完了 = 4/4
            Assert.AreEqual(1.0, session.Progress, 1e-9);
        }

        [Test]
        public void PhaseProgressListは現在のphaseを示す()
        {
            var phases = new List<Phase> { new Phase("prep", "下ごしらえ"), new Phase("cook", "焼く") };
            var steps = new List<Step>
            {
                new Step(1, "prep", "a", "a", null, new List<string>(), null, CompletionType.Manual, new List<string>()),
                new Step(2, "prep", "b", "b", null, new List<string>(), null, CompletionType.Manual, new List<string>()),
                new Step(3, "cook", "c", "c", null, new List<string>(), null, CompletionType.Manual, new List<string>()),
            };
            var recipe = new Recipe("R", "T", null, null, 1, 1, new List<Ingredient>(), new List<string>(), phases, steps);
            var session = new CookSession(recipe);

            session.Apply(SessionEvent.NextRequested.Instance); // 1 -> 2（prep 内）

            var progress = session.PhaseProgressList();

            Assert.AreEqual(2, progress.Count);
            Assert.AreEqual(1, progress[0].StepsDone); // prep: 1 済み / 2
            Assert.AreEqual(2, progress[0].StepsTotal);
            Assert.IsTrue(progress[0].IsCurrent);
            Assert.AreEqual(0, progress[1].StepsDone); // cook: 未着手
            Assert.IsFalse(progress[1].IsCurrent);
        }

        [Test]
        public void タイマーは開始でRunningになりElapsedで止まる()
        {
            var session = new CookSession(MakeRecipe(1, timerSecOnFirstStep: 60));

            session.Apply(new SessionEvent.TimerStarted(1, nowSeconds: 100));
            var timer = session.GetTimer(1);

            Assert.IsNotNull(timer);
            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(60, timer.Remaining(100), 1e-9);
            Assert.AreEqual(30, timer.Remaining(130), 1e-9);
            Assert.AreEqual(0, timer.Remaining(200), 1e-9); // 負にならない
            Assert.IsTrue(timer.IsElapsed(160));

            session.Apply(new SessionEvent.TimerElapsed(1));

            Assert.IsFalse(timer.IsRunning);
        }

        [Test]
        public void タイマーが無い工程でTimerStartedを送っても何も作られない()
        {
            var session = new CookSession(MakeRecipe(1, timerSecOnFirstStep: null));

            session.Apply(new SessionEvent.TimerStarted(1, nowSeconds: 0));

            Assert.IsNull(session.GetTimer(1));
        }
    }
}
