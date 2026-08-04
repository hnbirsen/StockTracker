using StockTracker.Shared.Contracts.Messages.V1;

namespace StockTracker.Notification.Entities;

// Notification Service'in "yok -> var" geçişini tespit edebilmesi için tuttuğu kendi, bağımsız durumu.
// Subscription Service'in WatchGroup.LastKnownStatus'una bilerek bağımlı değil: her iki servis de aynı
// StockResultEvent'i (fanout) bağımsız kopyalar olarak tüketiyor — Subscription'ın durumu event'i işlerken
// zaten YENİ değere güncellenmiş olabileceğinden (aynı anda tüketilen iki bağımsız consumer), "önceki durum
// neydi" sorusunun cevabı yalnızca event'in kendi tarihçesinden, servisin kendi hafızasından gelebilir
// (bkz. .claude/ARCHITECTURE.md — Notification Service, neden Subscription'a sorulmadığı).
public class WatchGroupNotificationState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductCode { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public Guid? StoreId { get; set; }
    public StockStatus? LastKnownStatus { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
