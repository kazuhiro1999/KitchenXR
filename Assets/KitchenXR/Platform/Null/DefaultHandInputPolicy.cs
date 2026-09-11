using System;

namespace KitchenXR.Platform.Null
{
    /// <summary>
    /// 機種非依存の既定実装。状態を持って通知するだけで、実際に Interactor を止める配線は
    /// Presentation 側（<c>CookingModeInputGate</c> 等）が <see cref="ModeChanged"/> を見て行う。
    /// </summary>
    public sealed class DefaultHandInputPolicy : IHandInputPolicy
    {
        // 起動時・配置を「保存」した後は調理モードが既定（設計 §4.4）。
        public HandInputMode CurrentMode { get; private set; } = HandInputMode.CookingMode;

        public event Action<HandInputMode> ModeChanged;

        public void SetMode(HandInputMode mode)
        {
            if (CurrentMode == mode)
            {
                return;
            }

            CurrentMode = mode;
            ModeChanged?.Invoke(mode);
        }
    }
}
