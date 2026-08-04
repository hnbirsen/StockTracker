namespace StockTracker.Billing.Entities;

// Kullanıcı başına tek satır (UserId unique) — MVP kapsamında bir kullanıcının aynı anda birden fazla
// aktif aboneliği desteklenmiyor (ör. hem Apple hem Google'dan aktif abonelik). Gerçek dünyada nadir bir
// senaryo; gerekirse ileride UserId+Platform kompozit anahtara genişletilebilir.
public class UserSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid PlanId { get; set; }
    public Platform Platform { get; set; }

    // Apple: originalTransactionId (aboneliğin ömrü boyunca sabit kalan kimlik).
    // Google: purchaseToken (yenilemede değişebilir — webhook'tan gelen güncel token'la üzerine yazılır).
    public string? StoreTransactionId { get; set; }
    public string? PurchaseToken { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Unknown;
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
