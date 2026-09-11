namespace KitchenXR.Platform.Null
{
    /// <summary>
    /// パススルーの状態を持つだけで、実機のカメラには触らない。MR テンプレートの AR Session が
    /// 既定でパススルーを出しているので、P0/P1 の「口だけ」段階ではこれで足りる。
    /// </summary>
    public sealed class NullPassthrough : IPassthroughControl
    {
        public bool IsEnabled { get; private set; } = true;

        public void Enable() => IsEnabled = true;

        public void Disable() => IsEnabled = false;
    }
}
