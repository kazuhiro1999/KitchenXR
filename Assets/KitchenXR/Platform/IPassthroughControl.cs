namespace KitchenXR.Platform
{
    /// <summary>
    /// パススルー（MR のカメラ透過）の口だけ（設計 §4.3）。
    /// MR テンプレートは既定でパススルーが有効なので、P0/P1 では
    /// <see cref="Null.NullPassthrough"/>（状態を持つだけで実機能には触らない）で足りる。
    /// 実際に ARCameraManager を叩く実装は P2 の ArFoundation/ に置く。
    /// </summary>
    public interface IPassthroughControl
    {
        bool IsEnabled { get; }
        void Enable();
        void Disable();
    }
}
