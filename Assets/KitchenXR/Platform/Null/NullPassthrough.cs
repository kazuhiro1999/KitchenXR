namespace KitchenXR.Platform.Null
{
    /// <summary>
    /// パススルーの状態を持つだけで、実機のカメラには触らない。MR テンプレートの AR Session が
    /// 既定でパススルーを出しているので、「口だけ」の段階ではこれで足りる。
    /// </summary>
    public sealed class NullPassthrough : IPassthroughControl
    {
        public bool IsEnabled { get; private set; } = true;

        public void Enable() => IsEnabled = true;

        public void Disable() => IsEnabled = false;
    }
}
