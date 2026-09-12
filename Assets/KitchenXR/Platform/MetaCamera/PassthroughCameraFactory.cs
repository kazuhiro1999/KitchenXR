using KitchenXR.Platform.Null;

namespace KitchenXR.Platform.MetaCamera
{
    /// <summary>
    /// どのカメラを挿すかを決める唯一の場所（<c>AnchorStoreFactory</c> と同じ役回り）。
    /// Editor は必ず受け皿へ落ちる——XR Simulator はカメラ非対応で、
    /// 実機でだけ動くものを Editor でも「理由が札に出る」形にしておくため。
    /// </summary>
    public static class PassthroughCameraFactory
    {
        public static IPassthroughCamera Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new MetaOpenXRPassthroughCamera();
#else
            return new NullPassthroughCamera();
#endif
        }
    }
}
