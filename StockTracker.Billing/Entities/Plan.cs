namespace StockTracker.Billing.Entities;

// Price burada tutulmaz — gerçek fiyat App Store Connect/Play Console'da tanımlanır (bkz.
// .claude/ARCHITECTURE.md > Billing). AppStoreProductId/PlayStoreProductId, ilgili store'da gerçek
// ürün oluşturulana kadar null kalır.
public class Plan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int MaxTrackedProducts { get; set; }
    public int CheckFrequencyMinutes { get; set; }
    public string? AppStoreProductId { get; set; }
    public string? PlayStoreProductId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
