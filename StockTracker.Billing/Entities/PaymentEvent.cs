namespace StockTracker.Billing.Entities;

// Idempotency anahtarı: (Provider, EventId) unique — aynı webhook event'i iki kez teslim edilirse
// (Apple/Google'ın kendi at-least-once garantisi) ikinci deneme bu tabloya çarpar, tekrar işlenmez.
// SubscriptionId nullable: event, henüz POST /billing/verify-purchase ile bir UserId'ye bağlanmamış bir
// abonelikten geliyorsa (ör. webhook, kullanıcı uygulamayı tekrar açıp doğrulama yapmadan önce gelirse)
// null kalır — event yine de denetim/idempotency amacıyla kaydedilir.
public class PaymentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SubscriptionId { get; set; }
    public Platform Provider { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
