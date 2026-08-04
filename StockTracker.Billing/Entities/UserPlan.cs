namespace StockTracker.Billing.Entities;

// Kullanıcı başına tek aktif plan — UserId unique. Plan değişikliği (upgrade/downgrade, Faz 4.2/4.3)
// bu satırı günceller, yeni satır açmaz; geçmiş gerekirse PaymentEvents'ten (Faz 4.2) türetilir.
public class UserPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
