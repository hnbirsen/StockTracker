using StockTracker.Shared.Contracts.Messages.V1;

namespace StockTracker.Subscription.Entities;

// Aynı ürün+beden+mağaza kombinasyonunu takip eden tüm kullanıcılar bu tek kayda bağlanır (dedup — bkz. UserWatch).
public class WatchGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductCode { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;

    // Store Reference Service'ten çözülen gerçek mağaza ID'si; null ise online-only takip (CheckStockCommand.StoreId ile aynı sözleşme).
    public Guid? StoreId { get; set; }

    public DateTime? LastCheckedAt { get; set; }
    public StockStatus? LastKnownStatus { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
