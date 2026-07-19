public class WatchGroup : BaseEntity
{
    public string ProductCode { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int StoreId { get; set; }
    public DateTime LastCheckedAt { get; set; }
    public int LastKnownStatus { get; set; } /// TODO: Change to enum
}