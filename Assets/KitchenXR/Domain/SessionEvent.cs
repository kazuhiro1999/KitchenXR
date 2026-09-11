namespace KitchenXR.Domain
{
    /// <summary>
    /// <see cref="CookSession.Apply"/> に渡すイベント。v0 は Next／Prev／タイマーの4種だけ。
    /// v2 の認識イベント（Observation）もここに増える想定（設計 §5）だが、
    /// 「認識器は工程を進めない。観測を送るだけ」なので Apply の外形は変えずに済む。
    /// </summary>
    public abstract class SessionEvent
    {
        private SessionEvent()
        {
        }

        /// <summary>「次へ」。連打の抑止（600ms）は Presentation の仕事——Domain は純粋に保つ。</summary>
        public sealed class NextRequested : SessionEvent
        {
            public static readonly NextRequested Instance = new NextRequested();
        }

        /// <summary>「戻る」。常に受け付ける（誤タッチの取り消し。設計 §5）。</summary>
        public sealed class PrevRequested : SessionEvent
        {
            public static readonly PrevRequested Instance = new PrevRequested();
        }

        /// <summary>工程 <paramref name="StepIndex"/> のタイマーを開始する。</summary>
        public sealed class TimerStarted : SessionEvent
        {
            public int StepIndex { get; }
            public double NowSeconds { get; }

            public TimerStarted(int stepIndex, double nowSeconds)
            {
                StepIndex = stepIndex;
                NowSeconds = nowSeconds;
            }
        }

        /// <summary>工程 <paramref name="StepIndex"/> のタイマーが時間切れになった。</summary>
        public sealed class TimerElapsed : SessionEvent
        {
            public int StepIndex { get; }

            public TimerElapsed(int stepIndex)
            {
                StepIndex = stepIndex;
            }
        }
    }
}
