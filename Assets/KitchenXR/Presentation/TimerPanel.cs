using System.Collections.Generic;
using KitchenXR.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// タイマーパネル（調理中は常時使える）。
    ///
    /// 上の作り口で長さを決めて「開始」。動いているものは下に縦へ積む（同時に3つまで）。
    /// 工程に <c>timer_sec</c> があれば、その工程に入ったときに既定値として入る。
    /// 終わったら音（その場で作った短い合成音）と板の点滅で知らせる。
    ///
    /// 時間の計算そのものは Domain の <see cref="CookTimer"/> の仕事——
    /// ここは時計（<c>Time.unscaledTime</c>）を渡して結果を映すだけ。
    /// 「停止」は Domain に一時停止の概念が無いので、止めた瞬間の残りを Presentation が覚えておき、
    /// 「再開」でその残りを持つ <see cref="CookTimer"/> を作り直す。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TimerPanel : MonoBehaviour
    {
        /// <summary>同時に持てるタイマーの数。</summary>
        public const int MaxTimers = 3;

        private const double MinSeconds = 30;
        private const double MaxSeconds = 60 * 60;
        private const double DefaultSeconds = 180;

        /// <summary>鳴り続ける上限。触られなくてもいつかは静かになる。</summary>
        private const float AlarmSeconds = 60f;

        private readonly ClickDebounce _debounce = new ClickDebounce();
        private readonly List<TimerEntry> _entries = new List<TimerEntry>();

        private VisualElement _root;
        private Label _setterLabel;
        private Label _setterValue;
        private Button _startButton;
        private ScrollView _scroll;

        private AudioSource _audioSource;
        private AudioClip _chime;

        private double _pendingSeconds = DefaultSeconds;
        private string _pendingLabel = "タイマー";
        private int _lastStepIndex = int.MinValue;
        private bool _needsRebuild;

        private void Awake()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            _setterLabel = _root.Q<Label>("setterLabel");
            _setterValue = _root.Q<Label>("setterValue");
            _startButton = _root.Q<Button>("startButton");
            _scroll = _root.Q<ScrollView>("timerScroll");

            PokePress.BindButton(_startButton, _debounce, "timer-start", StartPending);
            BindSetterButton("minusButton", "timer-minus", () => AddPendingSeconds(-30));
            BindSetterButton("plusButton", "timer-plus", () => AddPendingSeconds(30));
            BindSetterButton("preset1Button", "timer-preset-1", () => SetPendingSeconds(60));
            BindSetterButton("preset3Button", "timer-preset-3", () => SetPendingSeconds(180));
            BindSetterButton("preset5Button", "timer-preset-5", () => SetPendingSeconds(300));
            BindSetterButton("preset10Button", "timer-preset-10", () => SetPendingSeconds(600));

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 台所のどこにいても聞こえるように（板の位置に縛らない）。
            _chime = CreateChime();

            RefreshSetter();
        }

        private void BindSetterButton(string name, string key, System.Action action)
        {
            var button = _root.Q<Button>(name);
            PokePress.BindButton(button, _debounce, key, action);
        }

        private void Update()
        {
            var now = Time.unscaledTime;

            foreach (var entry in _entries)
            {
                UpdateEntryValue(entry, now);

                if (entry.Timer != null && entry.Timer.IsRunning && entry.Timer.IsElapsed(now) && !entry.Alarmed)
                {
                    entry.Alarmed = true;
                    entry.AlarmStartedAt = now;
                    PlayChime();
                    _needsRebuild = true; // 「停止」を「消す」に差し替える。
                }
            }

            UpdateBlink(now);

            if (_needsRebuild)
            {
                _needsRebuild = false;
                RebuildCards();
            }
        }

        /// <summary>工程が変わったら timer_sec を既定値に入れる。</summary>
        public void Refresh(CookSession session)
        {
            var current = session.CurrentStep;
            var stepIndex = current?.Index ?? int.MinValue;

            if (stepIndex == _lastStepIndex)
            {
                return;
            }

            _lastStepIndex = stepIndex;

            if (current?.TimerSec != null)
            {
                _pendingSeconds = Mathf.Clamp((float)current.TimerSec.Value, (float)MinSeconds, (float)MaxSeconds);
                _pendingLabel = string.IsNullOrEmpty(current.Title) ? "タイマー" : current.Title;
            }
            else
            {
                _pendingLabel = "タイマー";
            }

            RefreshSetter();
        }

        // ------------------------------------------------------------------ 作り口

        private void SetPendingSeconds(double seconds)
        {
            _pendingSeconds = System.Math.Min(System.Math.Max(seconds, MinSeconds), MaxSeconds);
            RefreshSetter();
        }

        private void AddPendingSeconds(double delta) => SetPendingSeconds(_pendingSeconds + delta);

        private void RefreshSetter()
        {
            if (_setterValue == null)
            {
                return;
            }

            _setterValue.text = FormatSeconds(_pendingSeconds);
            _setterLabel.text = _entries.Count >= MaxTimers ? $"{_pendingLabel}（上限{MaxTimers}）" : _pendingLabel;
            _startButton.SetEnabled(_entries.Count < MaxTimers);
        }

        private void StartPending()
        {
            if (_entries.Count >= MaxTimers)
            {
                return;
            }

            var entry = new TimerEntry
            {
                Label = _pendingLabel,
                DurationSec = _pendingSeconds,
                Timer = new CookTimer(_lastStepIndex, _pendingSeconds),
            };

            entry.Timer.Start(Time.unscaledTime);
            _entries.Add(entry);
            _needsRebuild = true;
            RefreshSetter();
        }

        // ------------------------------------------------------------------ 札

        private void RebuildCards()
        {
            _scroll.Clear();

            foreach (var entry in _entries)
            {
                _scroll.Add(BuildCard(entry));
            }

            RefreshSetter();
        }

        private VisualElement BuildCard(TimerEntry entry)
        {
            var card = new VisualElement();
            card.AddToClassList("timer-card");
            entry.Card = card;

            var title = new Label(entry.Label);
            title.AddToClassList("caption");
            title.AddToClassList("timer-card__title");
            card.Add(title);

            var row = new VisualElement();
            row.AddToClassList("timer-row");

            var value = new Label();
            value.AddToClassList("timer-value");
            entry.Value = value;
            row.Add(value);

            var running = IsRunning(entry, Time.unscaledTime);
            var toggleButton = new Button { text = running ? "停止" : "再開" };
            toggleButton.AddToClassList("kitchen-button");
            toggleButton.AddToClassList("kitchen-button--secondary");
            PokePress.BindButton(toggleButton, _debounce, $"timer-toggle-{entry.Id}",
                () => { Acknowledge(entry); ToggleEntry(entry); });
            row.Add(toggleButton);

            var resetButton = new Button { text = "リセット" };
            resetButton.AddToClassList("kitchen-button");
            resetButton.AddToClassList("kitchen-button--secondary");
            PokePress.BindButton(resetButton, _debounce, $"timer-reset-{entry.Id}",
                () => { Acknowledge(entry); ResetEntry(entry); });
            row.Add(resetButton);

            var removeButton = new Button { text = "消す" };
            removeButton.AddToClassList("kitchen-button");
            removeButton.AddToClassList("kitchen-button--secondary");
            PokePress.BindButton(removeButton, _debounce, $"timer-remove-{entry.Id}",
                () => { Acknowledge(entry); RemoveEntry(entry); });
            row.Add(removeButton);

            card.Add(row);

            UpdateEntryValue(entry, Time.unscaledTime);
            return card;
        }

        private void ToggleEntry(TimerEntry entry)
        {
            var now = Time.unscaledTime;
            if (IsRunning(entry, now))
            {
                entry.FrozenRemaining = entry.Timer.Remaining(now);
                entry.Timer.Stop();
            }
            else
            {
                var remaining = entry.Timer == null || entry.Alarmed ? entry.DurationSec : entry.FrozenRemaining;
                if (remaining <= 0)
                {
                    remaining = entry.DurationSec;
                }

                entry.Alarmed = false;
                entry.Timer = new CookTimer(_lastStepIndex, remaining);
                entry.Timer.Start(now);
            }

            _needsRebuild = true;
        }

        private void ResetEntry(TimerEntry entry)
        {
            entry.Timer = null;
            entry.FrozenRemaining = entry.DurationSec;
            entry.Alarmed = false;
            _needsRebuild = true;
        }

        private void RemoveEntry(TimerEntry entry)
        {
            _entries.Remove(entry);
            _needsRebuild = true;
        }

        private static void Acknowledge(TimerEntry entry) => entry.AlarmAcknowledged = true;

        private static bool IsRunning(TimerEntry entry, float now) =>
            entry.Timer != null && entry.Timer.IsRunning && !entry.Timer.IsElapsed(now);

        private void UpdateEntryValue(TimerEntry entry, float now)
        {
            if (entry.Value == null)
            {
                return;
            }

            double remaining;
            if (entry.Timer == null)
            {
                remaining = entry.DurationSec;
            }
            else if (entry.Timer.IsRunning)
            {
                remaining = entry.Timer.Remaining(now);
            }
            else
            {
                remaining = entry.FrozenRemaining; // 止めた瞬間の残り（Domain は一時停止を持たない）。
            }

            entry.Value.text = FormatSeconds(remaining);
            entry.Value.RemoveFromClassList("timer-value--running");
            entry.Value.RemoveFromClassList("timer-value--elapsed");

            if (entry.Alarmed || remaining <= 0)
            {
                entry.Value.AddToClassList("timer-value--elapsed");
            }
            else if (entry.Timer != null && entry.Timer.IsRunning)
            {
                entry.Value.AddToClassList("timer-value--running");
            }
        }

        // ------------------------------------------------------------------ 知らせ（音と点滅）

        private void UpdateBlink(float now)
        {
            var blinking = false;
            foreach (var entry in _entries)
            {
                var alarming = entry.Alarmed && !entry.AlarmAcknowledged && now - entry.AlarmStartedAt < AlarmSeconds;
                var on = alarming && Mathf.Repeat(now, 0.8f) < 0.4f;
                blinking |= on;

                if (entry.Card != null)
                {
                    SetClass(entry.Card, "timer-card--alarm", on);
                }
            }

            SetClass(_root, "root-panel--alarm", blinking);
        }

        private void PlayChime()
        {
            if (_audioSource != null && _chime != null)
            {
                _audioSource.PlayOneShot(_chime);
            }
        }

        /// <summary>
        /// 短い知らせの音をその場で作る（外部の音源は持ち込まない）。
        /// 880Hz の点を3つ、指数で減衰させる——調理中の音に紛れにくい高さにしてある。
        /// </summary>
        private static AudioClip CreateChime()
        {
            const int sampleRate = 44100;
            const float beepSeconds = 0.14f;
            const float gapSeconds = 0.10f;
            const int beeps = 3;
            const float frequency = 880f;

            var totalSamples = Mathf.RoundToInt((beepSeconds + gapSeconds) * beeps * sampleRate);
            var data = new float[totalSamples];
            var beepSamples = Mathf.RoundToInt(beepSeconds * sampleRate);
            var strideSamples = Mathf.RoundToInt((beepSeconds + gapSeconds) * sampleRate);

            for (var beep = 0; beep < beeps; beep++)
            {
                var start = beep * strideSamples;
                for (var i = 0; i < beepSamples && start + i < totalSamples; i++)
                {
                    var t = i / (float)sampleRate;
                    var envelope = Mathf.Exp(-12f * t); // 立ち上がりだけ鋭く、すぐ減衰させる。
                    data[start + i] = 0.45f * envelope * Mathf.Sin(2f * Mathf.PI * frequency * t);
                }
            }

            var clip = AudioClip.Create("KitchenXR Timer Chime", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void SetClass(VisualElement element, string className, bool on)
        {
            if (element == null)
            {
                return;
            }

            if (on)
            {
                element.AddToClassList(className);
            }
            else
            {
                element.RemoveFromClassList(className);
            }
        }

        private static string FormatSeconds(double seconds)
        {
            var total = Mathf.Max(0, Mathf.RoundToInt((float)seconds));
            var minutes = total / 60;
            var secs = total % 60;
            return $"{minutes:00}:{secs:00}";
        }

        /// <summary>板が持つタイマー1つ。時間の計算は <see cref="CookTimer"/>、見た目と一時停止はここ。</summary>
        private sealed class TimerEntry
        {
            private static int s_nextId;

            public readonly int Id = ++s_nextId;

            public string Label;
            public double DurationSec;
            public CookTimer Timer;
            public double FrozenRemaining;
            public bool Alarmed;
            public bool AlarmAcknowledged;
            public float AlarmStartedAt;

            public VisualElement Card;
            public Label Value;
        }
    }
}
