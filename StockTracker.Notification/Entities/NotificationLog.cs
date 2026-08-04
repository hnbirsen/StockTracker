namespace StockTracker.Notification.Entities;

// Idempotency anahtarı (CommandId, UserId, Channel) — aynı StockResultEvent iki kez tüketilirse
// (MassTransit at-least-once teslimat) ikinci deneme bu tablodaki unique index'e çarpar ve gönderim
// tekrarlanmaz (bkz. .claude/ARCHITECTURE.md — Notification Service).
public class NotificationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public Guid? StoreId { get; set; }
    public NotificationChannel Channel { get; set; }
    public Guid CommandId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
