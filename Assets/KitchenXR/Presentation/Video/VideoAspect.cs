namespace KitchenXR.Presentation.Video
{
    /// <summary>動画の板の縦横（設計 §6「16:9 と 9:16（ショーツ）の切り替え」）。</summary>
    public enum VideoAspect
    {
        /// <summary>16:9。普通の動画。既定。</summary>
        Landscape,

        /// <summary>9:16。ショーツ。</summary>
        Portrait,
    }
}
