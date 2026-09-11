namespace KitchenXR.Domain
{
    /// <summary>
    /// phase 1つぶんの進捗（レシピパネル上部の点列・設計 §9 用）。
    /// 全体の <see cref="CookSession.Progress"/>（工程単位）とは丸め方を分けている
    /// （設計 §4.4「phase単位の丸めは PhaseProgress で別に」）。
    /// </summary>
    public readonly struct PhaseProgress
    {
        public string PhaseId { get; }
        public string Title { get; }
        public int StepsDone { get; }
        public int StepsTotal { get; }
        public bool IsCurrent { get; }

        public PhaseProgress(string phaseId, string title, int stepsDone, int stepsTotal, bool isCurrent)
        {
            PhaseId = phaseId;
            Title = title;
            StepsDone = stepsDone;
            StepsTotal = stepsTotal;
            IsCurrent = isCurrent;
        }

        public bool IsComplete => StepsTotal > 0 && StepsDone >= StepsTotal;
    }
}
