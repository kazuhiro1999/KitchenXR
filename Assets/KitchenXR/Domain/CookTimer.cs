namespace KitchenXR.Domain
{
    /// <summary>
    /// 工程1つに紐づくタイマー。時計は外から渡す（<c>nowSeconds</c>）ので、
    /// 試験では時刻を固定できる。複数同時に持てるよう <see cref="CookSession"/> 側で束ねる。
    /// </summary>
    public sealed class CookTimer
    {
        public int StepIndex { get; }
        public double DurationSec { get; }

        private double? _startedAtSec;
        private bool _stopped;

        public CookTimer(int stepIndex, double durationSec)
        {
            StepIndex = stepIndex;
            DurationSec = durationSec;
        }

        public bool IsRunning => _startedAtSec.HasValue && !_stopped;

        /// <summary>nowSeconds を開始時刻として計測を始める（やり直しも可）。</summary>
        public void Start(double nowSeconds)
        {
            _startedAtSec = nowSeconds;
            _stopped = false;
        }

        public void Stop()
        {
            _stopped = true;
        }

        /// <summary>開始からの経過秒。開始前なら 0。</summary>
        public double Elapsed(double nowSeconds)
        {
            if (!_startedAtSec.HasValue)
            {
                return 0;
            }

            var elapsed = nowSeconds - _startedAtSec.Value;
            return elapsed < 0 ? 0 : elapsed;
        }

        /// <summary>残り秒（0 未満にはならない）。</summary>
        public double Remaining(double nowSeconds)
        {
            var remaining = DurationSec - Elapsed(nowSeconds);
            return remaining < 0 ? 0 : remaining;
        }

        public bool IsElapsed(double nowSeconds) => _startedAtSec.HasValue && Elapsed(nowSeconds) >= DurationSec;
    }
}
