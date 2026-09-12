namespace KitchenXR.Domain
{
    /// <summary>
    /// 工程がどう完了するか（契約 JSON の completion）。
    /// 今は全部 Manual。列だけ先に持っておき、将来の自動進行に備える。
    /// </summary>
    public enum CompletionType
    {
        /// <summary>「次へ」を押すまで進まない。今はこれだけ使う。</summary>
        Manual,

        /// <summary>認識器の観測が Stable になったら自動で進む（将来）。</summary>
        Auto,

        /// <summary>認識器は「済んだようです」と提案するだけ。確定は人（将来）。</summary>
        Confirm,
    }
}
