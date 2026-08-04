namespace StockTracker.Notification.Services;

public interface IUserDeviceTokenProvider
{
    Task<string?> GetDeviceTokenAsync(Guid userId);
}

// Placeholder — cihaz push token'ı kaydeden hiçbir mekanizma henüz yok (mobil uygulama Faz 5.4'te
// geliyor: "FCM push notification token kaydı"). Bu implementasyon her zaman null döner, böylece push
// kanalı bu fazda sessizce atlanır (bkz. NotificationProcessingService); Faz 5.4'te gerçek bir kaynağa
// (ör. Identity Service'te kullanıcı başına saklanan token) bağlanan bir implementasyonla değiştirilecek.
public class NoOpDeviceTokenProvider : IUserDeviceTokenProvider
{
    public Task<string?> GetDeviceTokenAsync(Guid userId) => Task.FromResult<string?>(null);
}
