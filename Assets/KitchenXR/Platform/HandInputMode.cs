namespace KitchenXR.Platform
{
    /// <summary>
    /// 手の入力の2モード（設計 §4.4）。既定は CookingMode。
    /// </summary>
    public enum HandInputMode
    {
        /// <summary>調理モード（既定）。Poke（触る）だけ。Ray とピンチは無効。</summary>
        CookingMode,

        /// <summary>配置モード。Ray ＋ Grab でパネルを掴んで動かす。</summary>
        PlacementMode,
    }
}
