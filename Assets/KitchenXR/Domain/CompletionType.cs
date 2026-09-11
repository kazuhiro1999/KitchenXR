namespace KitchenXR.Domain
{
    /// <summary>
    /// 工程がどう完了するか（契約 JSON §3 の completion）。
    /// v0 は全部 Manual。列だけ先に持っておき、v2 の自動進行に備える。
    /// </summary>
    public enum CompletionType
    {
        /// <summary>「次へ」を主人が押すまで進まない。v0 はこれだけ使う。</summary>
        Manual,

        /// <summary>認識器の観測が Stable になったら自動で進む（v2）。</summary>
        Auto,

        /// <summary>認識器は「済んだようです」と提案するだけ。確定は主人（v2）。</summary>
        Confirm,
    }
}
