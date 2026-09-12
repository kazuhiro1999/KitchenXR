using System;

namespace KitchenXR.Platform
{
    /// <summary>
    /// いま調理モードか配置モードかを持つだけの口。
    /// 実際に Ray Interactor を止める配線は Presentation 側（XRI は機種非依存の
    /// ツールキットそのものなので Platform に閉じ込める対象ではない）。
    /// </summary>
    public interface IHandInputPolicy
    {
        HandInputMode CurrentMode { get; }

        void SetMode(HandInputMode mode);

        /// <summary>モードが変わるたびに呼ばれる（Presentation が Interactor の有効/無効を切り替える契機）。</summary>
        event Action<HandInputMode> ModeChanged;
    }
}
