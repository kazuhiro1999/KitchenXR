namespace KitchenXR.Domain
{
    /// <summary>
    /// 材料1点（契約 JSON の ingredients[]）。分量は自由文字列のまま持つ（正規化はサーバ側の仕事）。
    /// </summary>
    public sealed class Ingredient
    {
        public string Name { get; }
        public string Qty { get; }
        public string Unit { get; }
        public string Prep { get; }
        public string Group { get; }

        public Ingredient(string name, string qty, string unit, string prep, string group)
        {
            Name = name ?? string.Empty;
            Qty = qty ?? string.Empty;
            Unit = unit ?? string.Empty;
            Prep = prep ?? string.Empty;
            Group = group ?? string.Empty;
        }
    }
}
