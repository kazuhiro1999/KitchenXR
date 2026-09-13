using System.Collections.Generic;

namespace KitchenXR.Domain
{
    /// <summary>
    /// 工程1つ（契約 JSON の steps[]）。「1動作1工程」の単位。
    /// 短さ（title ≤ 12・instruction ≤ 100）はサーバで保証する前提なので、
    /// クライアント側では検算も切り詰めもしない。
    /// </summary>
    public sealed class Step
    {
        /// <summary>1始まりの通し番号。契約 JSON の index と同じ。</summary>
        public int Index { get; }
        public string PhaseId { get; }
        public string Title { get; }
        public string Instruction { get; }

        /// <summary>出典の画像 URL。無ければ null（材料名の札で代える）。</summary>
        public string Image { get; }
        public IReadOnlyList<string> IngredientsUsed { get; }

        /// <summary>この工程のタイマー既定秒数。無ければ null。</summary>
        public int? TimerSec { get; }
        public CompletionType Completion { get; }

        /// <summary>折りたたみのコツ。既定は隠す。</summary>
        public IReadOnlyList<string> Tips { get; }

        public Step(
            int index,
            string phaseId,
            string title,
            string instruction,
            string image,
            IReadOnlyList<string> ingredientsUsed,
            int? timerSec,
            CompletionType completion,
            IReadOnlyList<string> tips)
        {
            Index = index;
            PhaseId = phaseId ?? string.Empty;
            Title = title ?? string.Empty;
            Instruction = instruction ?? string.Empty;
            Image = image;
            IngredientsUsed = ingredientsUsed ?? System.Array.Empty<string>();
            TimerSec = timerSec;
            Completion = completion;
            Tips = tips ?? System.Array.Empty<string>();
        }
    }
}
