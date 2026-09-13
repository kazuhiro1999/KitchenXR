namespace KitchenXR.Presentation.Hazard
{
    /// <summary>領域への近さの3段。</summary>
    public enum HazardAlertLevel
    {
        /// <summary>離れている（線は出さない。配置モードの間だけ薄く出す）。</summary>
        Off,

        /// <summary>頭 60cm・手 40cm。床の線を琥珀で出す。</summary>
        Watch,

        /// <summary>手 20cm。線を赤へ変えて短い音を1つ。</summary>
        Near,
    }

    /// <summary>
    /// 領域1つぶんの近さの判定。閾値は3段で、境界で線が点滅しないよう
    /// <see cref="HysteresisMeters"/> だけ「戻りにくく」する（今の段より上へ行くには素の閾値、
    /// 下へ落ちるには閾値 ＋ 5cm 離れることが要る）。
    ///
    /// 場面に触らない値の型。EditMode で 3 段とヒステリシスを検算する。
    /// </summary>
    public sealed class HazardAlertState
    {
        /// <summary>手がこれより近ければ赤（m）。</summary>
        public const float HandNearMeters = 0.20f;

        /// <summary>手がこれより近ければ琥珀（m）。</summary>
        public const float HandWatchMeters = 0.40f;

        /// <summary>頭がこれより近ければ琥珀（m）。</summary>
        public const float HeadWatchMeters = 0.60f;

        /// <summary>段が下がるときだけ足す余裕（m）。点滅の歯止め。</summary>
        public const float HysteresisMeters = 0.05f;

        /// <summary>音を鳴らす間隔の下限（秒）。近づいている間ずっと鳴らない。</summary>
        public const float SoundCooldownSeconds = 5f;

        public HazardAlertLevel Level { get; private set; } = HazardAlertLevel.Off;

        /// <summary>直前の評価で <see cref="HazardAlertLevel.Near"/> へ上がったか（音の契機）。</summary>
        public bool EnteredNear { get; private set; }

        /// <summary>
        /// 手と頭の距離から段を決める。手が両手あるときは近い側を渡す。
        /// 領域が無い側は <see cref="float.PositiveInfinity"/> でよい。
        /// </summary>
        public HazardAlertLevel Evaluate(float handDistance, float headDistance)
        {
            var before = Level;
            var slack = HysteresisMeters;

            // 「今の段を保つための閾値」は素の閾値より 5cm 広い。上がるときは素の閾値。
            var nearLimit = before == HazardAlertLevel.Near ? HandNearMeters + slack : HandNearMeters;
            var watchHand = before >= HazardAlertLevel.Watch ? HandWatchMeters + slack : HandWatchMeters;
            var watchHead = before >= HazardAlertLevel.Watch ? HeadWatchMeters + slack : HeadWatchMeters;

            if (handDistance <= nearLimit)
            {
                Level = HazardAlertLevel.Near;
            }
            else if (handDistance <= watchHand || headDistance <= watchHead)
            {
                Level = HazardAlertLevel.Watch;
            }
            else
            {
                Level = HazardAlertLevel.Off;
            }

            EnteredNear = Level == HazardAlertLevel.Near && before != HazardAlertLevel.Near;
            return Level;
        }

        /// <summary>覚えている段を捨てる（領域を作り直した・消した）。</summary>
        public void Reset()
        {
            Level = HazardAlertLevel.Off;
            EnteredNear = false;
        }
    }
}
