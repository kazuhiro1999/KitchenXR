namespace KitchenXR.Platform
{
    /// <summary>
    /// パススルー（MR のカメラ透過）の口だけ。MR テンプレートは既定でパススルーが有効なので、
    /// 今は <see cref="Null.NullPassthrough"/>（状態を持つだけで実機能には触らない）で足りる。
    /// </summary>
    public interface IPassthroughControl
    {
        bool IsEnabled { get; }
        void Enable();
        void Disable();
    }
}
