using System.Collections.Generic;
using System.Linq;

namespace KitchenXR.Domain
{
    /// <summary>
    /// 工程の状態機械（設計 §5）。Unity 非依存の純粋な C#。
    /// v0 は NextRequested／PrevRequested／タイマーの4イベントだけを扱う。
    /// 同じイベントの連打抑止（600ms）は Presentation の仕事——ここでは扱わない。
    /// </summary>
    public sealed class CookSession
    {
        private readonly Dictionary<int, CookTimer> _timers = new Dictionary<int, CookTimer>();

        public Recipe Recipe { get; }

        /// <summary>1始まりの現在工程番号。範囲は 1..Steps.Count（工程が無ければ 0）。</summary>
        public int Current { get; private set; }

        /// <summary>最後の工程で Next を受けて完了した状態か。</summary>
        public bool IsComplete { get; private set; }

        public CookSession(Recipe recipe)
        {
            Recipe = recipe;
            Current = recipe.Steps.Count > 0 ? 1 : 0;
            IsComplete = false;
        }

        /// <summary>今の工程。完了後や工程が無いレシピでは null。</summary>
        public Step CurrentStep => IsComplete || Current <= 0 ? null : Recipe.Steps[Current - 1];

        /// <summary>次の工程（見出しだけ薄く出す用。設計 §9）。無ければ null。</summary>
        public Step NextStep => !IsComplete && Current > 0 && Current < Recipe.Steps.Count
            ? Recipe.Steps[Current]
            : null;

        /// <summary>済んだ工程 / 全工程。完了時は 1.0。工程が無ければ 0。</summary>
        public double Progress
        {
            get
            {
                var total = Recipe.Steps.Count;
                if (total == 0)
                {
                    return 0;
                }

                var done = IsComplete ? total : Current - 1;
                return (double)done / total;
            }
        }

        /// <summary>
        /// 途中起動の復帰（設計 §5・ROADMAP P5）。manor の <c>cook-sessions/current</c> か、
        /// オフラインなら手元の控え（<c>last_session.json</c>）から戻した工程番号を入れる。
        ///
        /// 範囲外は 1..<c>Steps.Count</c> に丸める——サーバの数を無条件に信じない
        /// （レシピが manor 側で編集されて工程が減っていることがある）。
        /// </summary>
        public void SeekTo(int stepIndex)
        {
            var total = Recipe.Steps.Count;
            if (total == 0)
            {
                Current = 0;
                IsComplete = false;
                return;
            }

            IsComplete = false;
            Current = stepIndex < 1 ? 1 : stepIndex > total ? total : stepIndex;
        }

        public void Apply(SessionEvent sessionEvent)
        {
            switch (sessionEvent)
            {
                case SessionEvent.NextRequested _:
                    ApplyNext();
                    break;
                case SessionEvent.PrevRequested _:
                    ApplyPrev();
                    break;
                case SessionEvent.TimerStarted started:
                    ApplyTimerStarted(started);
                    break;
                case SessionEvent.TimerElapsed elapsed:
                    ApplyTimerElapsed(elapsed);
                    break;
            }
        }

        private void ApplyNext()
        {
            if (Recipe.Steps.Count == 0 || IsComplete)
            {
                // 完了後の Next は無視する。最後まで進めた後の誤操作を増やさない。
                return;
            }

            if (Current >= Recipe.Steps.Count)
            {
                IsComplete = true;
                return;
            }

            Current++;
        }

        private void ApplyPrev()
        {
            // 戻るは常に可能（誤タッチの取り消し。設計 §5・§7）。
            if (IsComplete)
            {
                IsComplete = false;
                return;
            }

            if (Current > 1)
            {
                Current--;
            }
        }

        private void ApplyTimerStarted(SessionEvent.TimerStarted started)
        {
            var step = Recipe.Steps.FirstOrDefault(s => s.Index == started.StepIndex);
            if (step?.TimerSec == null)
            {
                // タイマーを持たない工程には何もしない。
                return;
            }

            if (!_timers.TryGetValue(started.StepIndex, out var timer))
            {
                timer = new CookTimer(started.StepIndex, step.TimerSec.Value);
                _timers[started.StepIndex] = timer;
            }

            timer.Start(started.NowSeconds);
        }

        private void ApplyTimerElapsed(SessionEvent.TimerElapsed elapsed)
        {
            if (_timers.TryGetValue(elapsed.StepIndex, out var timer))
            {
                timer.Stop();
            }
        }

        /// <summary>工程 index に紐づくタイマー。開始していなければ null。</summary>
        public CookTimer GetTimer(int stepIndex) => _timers.TryGetValue(stepIndex, out var timer) ? timer : null;

        /// <summary>いま動いている（Stop されていない）タイマーをすべて返す。複数同時が前提（設計 §9）。</summary>
        public IReadOnlyList<CookTimer> ActiveTimers => _timers.Values.Where(t => t.IsRunning).ToList();

        /// <summary>このセッションで一度でも開始したタイマーをすべて返す（停止済み・時間切れも含む）。</summary>
        public IReadOnlyList<CookTimer> AllTimers => _timers.Values.OrderBy(t => t.StepIndex).ToList();

        /// <summary>
        /// phase 単位に丸めた進捗（レシピパネル上部の点列用）。
        /// 全体の <see cref="Progress"/>（工程単位の割合）とは別の丸め方を持つ（設計 §4.4）。
        /// </summary>
        public IReadOnlyList<PhaseProgress> PhaseProgressList()
        {
            var result = new List<PhaseProgress>();
            var currentPhaseId = CurrentStep?.PhaseId;

            foreach (var phase in Recipe.Phases)
            {
                var stepsInPhase = Recipe.Steps.Where(s => s.PhaseId == phase.Id).ToList();
                var total = stepsInPhase.Count;
                var done = stepsInPhase.Count(s => IsStepDone(s.Index));
                var isCurrent = !IsComplete && phase.Id == currentPhaseId;

                result.Add(new PhaseProgress(phase.Id, phase.Title, done, total, isCurrent));
            }

            return result;
        }

        private bool IsStepDone(int stepIndex)
        {
            if (IsComplete)
            {
                return true;
            }

            // Current より手前の工程だけが「済んだ」。今の工程はまだ済んでいない。
            return stepIndex < Current;
        }
    }
}
