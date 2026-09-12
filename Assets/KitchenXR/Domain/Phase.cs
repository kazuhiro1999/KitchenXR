namespace KitchenXR.Domain
{
    /// <summary>
    /// 工程を束ねる区切り（契約 JSON の phases[]）。進捗%の分母になる。
    /// </summary>
    public sealed class Phase
    {
        public string Id { get; }
        public string Title { get; }

        public Phase(string id, string title)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
        }
    }
}
