using System;
using System.Collections.Generic;
using KitchenXR.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// タイマーパネル（設計 §9）。工程に timer_sec があれば「開始」を出す。複数同時に縦へ積む。
    /// 時計は Time.unscaledTime を CookTimer に渡すだけ——時間の計算そのものは Domain の仕事。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TimerPanel : MonoBehaviour
    {
        private readonly ClickDebounce _debounce = new ClickDebounce();
        private readonly Dictionary<int, Label> _valueLabelByStep = new Dictionary<int, Label>();
        private readonly HashSet<int> _notifiedElapsed = new HashSet<int>();

        private ScrollView _scroll;
        private CookSession _session;

        /// <summary>工程のタイマーを開始してほしい（設計 §3 の timer_sec を持つ工程）。</summary>
        public event Action<int> TimerStartRequested;

        /// <summary>工程のタイマーを止めてほしい（手動停止・時間切れの両方でこれを送る。設計 §5）。</summary>
        public event Action<int> TimerStopRequested;

        private void Awake()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _scroll = root.Q<ScrollView>("timerScroll");
        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            var now = Time.unscaledTime;
            foreach (var timer in _session.AllTimers)
            {
                if (!_valueLabelByStep.TryGetValue(timer.StepIndex, out var label))
                {
                    continue;
                }

                UpdateValueLabel(label, timer, now);

                if (timer.IsRunning && timer.IsElapsed(now) && _notifiedElapsed.Add(timer.StepIndex))
                {
                    TimerStopRequested?.Invoke(timer.StepIndex);
                }
            }
        }

        public void Refresh(CookSession session)
        {
            _session = session;
            _scroll.Clear();
            _valueLabelByStep.Clear();

            var current = session.CurrentStep;
            var shown = new HashSet<int>();

            foreach (var timer in session.AllTimers)
            {
                AddTimerCard(session, timer.StepIndex, timer);
                shown.Add(timer.StepIndex);
                if (!timer.IsRunning)
                {
                    _notifiedElapsed.Remove(timer.StepIndex); // 止まっているものは次に開始したらまた通知してよい。
                }
            }

            // 今の工程にタイマーがあり、まだ始めていなければ「開始」だけの札を足す。
            if (current?.TimerSec != null && !shown.Contains(current.Index))
            {
                AddTimerCard(session, current.Index, null);
            }
        }

        private void AddTimerCard(CookSession session, int stepIndex, CookTimer timer)
        {
            var step = FindStep(session, stepIndex);
            var card = new VisualElement();
            card.AddToClassList("timer-card");

            var title = new Label(step?.Title ?? $"工程{stepIndex}");
            title.AddToClassList("caption");
            title.AddToClassList("timer-card__title");
            card.Add(title);

            var row = new VisualElement();
            row.AddToClassList("timer-card__row");

            var valueLabel = new Label();
            valueLabel.AddToClassList("timer-value");
            row.Add(valueLabel);
            _valueLabelByStep[stepIndex] = valueLabel;

            if (timer == null || !timer.IsRunning)
            {
                // 未開始、または停止済み（時間切れ含む）はどちらも「開始（やり直し）」を出す。
                var startButton = new Button { text = "開始" };
                startButton.AddToClassList("kitchen-button");
                startButton.AddToClassList("kitchen-button--primary");
                startButton.clicked += () =>
                {
                    if (_debounce.TryAccept($"timer-start-{stepIndex}"))
                    {
                        TimerStartRequested?.Invoke(stepIndex);
                    }
                };
                row.Add(startButton);
                valueLabel.text = timer != null
                    ? FormatSeconds(timer.Remaining(Time.unscaledTime))
                    : FormatSeconds(step?.TimerSec ?? 0);
            }
            else
            {
                var stopButton = new Button { text = "停止" };
                stopButton.AddToClassList("kitchen-button");
                stopButton.AddToClassList("kitchen-button--secondary");
                stopButton.clicked += () =>
                {
                    if (_debounce.TryAccept($"timer-stop-{stepIndex}"))
                    {
                        TimerStopRequested?.Invoke(stepIndex);
                    }
                };
                row.Add(stopButton);
                UpdateValueLabel(valueLabel, timer, Time.unscaledTime);
            }

            card.Add(row);
            _scroll.Add(card);
        }

        private static void UpdateValueLabel(Label label, CookTimer timer, float now)
        {
            label.text = FormatSeconds(timer.Remaining(now));
            label.RemoveFromClassList("timer-value--running");
            label.RemoveFromClassList("timer-value--elapsed");

            if (timer.IsElapsed(now))
            {
                label.AddToClassList("timer-value--elapsed");
            }
            else if (timer.IsRunning)
            {
                label.AddToClassList("timer-value--running");
            }
        }

        private static Step FindStep(CookSession session, int stepIndex)
        {
            foreach (var step in session.Recipe.Steps)
            {
                if (step.Index == stepIndex)
                {
                    return step;
                }
            }

            return null;
        }

        private static string FormatSeconds(double seconds)
        {
            var total = Mathf.Max(0, Mathf.RoundToInt((float)seconds));
            var minutes = total / 60;
            var secs = total % 60;
            return $"{minutes:00}:{secs:00}";
        }
    }
}
