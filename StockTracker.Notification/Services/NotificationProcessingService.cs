using Microsoft.EntityFrameworkCore;
using StockTracker.Notification.Data;
using StockTracker.Notification.Entities;
using StockTracker.Shared.Contracts.Messages.V1;

namespace StockTracker.Notification.Services;

public interface INotificationProcessingService
{
    Task ProcessAsync(StockResultEvent stockResultEvent, CancellationToken cancellationToken);
}

// Faz 3.3 — StockResultEvent'i işleyip yalnızca "yok -> var" geçişinde, o ürün/beden/mağazayı takip eden
// kullanıcılara bildirim gönderir. Önceki durumun nereden geldiği ve idempotency için bkz.
// WatchGroupNotificationState / NotificationLog üstündeki açıklamalar ve .claude/ARCHITECTURE.md.
public class NotificationProcessingService : INotificationProcessingService
{
    private readonly NotificationDbContext _db;
    private readonly ISubscriptionServiceClient _subscriptionClient;
    private readonly IIdentityServiceClient _identityClient;
    private readonly IEmailSender _emailSender;
    private readonly IPushSender _pushSender;
    private readonly IUserDeviceTokenProvider _deviceTokenProvider;
    private readonly ILogger<NotificationProcessingService> _logger;

    public NotificationProcessingService(
        NotificationDbContext db,
        ISubscriptionServiceClient subscriptionClient,
        IIdentityServiceClient identityClient,
        IEmailSender emailSender,
        IPushSender pushSender,
        IUserDeviceTokenProvider deviceTokenProvider,
        ILogger<NotificationProcessingService> logger)
    {
        _db = db;
        _subscriptionClient = subscriptionClient;
        _identityClient = identityClient;
        _emailSender = emailSender;
        _pushSender = pushSender;
        _deviceTokenProvider = deviceTokenProvider;
        _logger = logger;
    }

    public async Task ProcessAsync(StockResultEvent stockResultEvent, CancellationToken cancellationToken)
    {
        var previousStatus = await UpdateStateAndGetPreviousStatusAsync(stockResultEvent, cancellationToken);

        var isRestock = previousStatus == StockStatus.OutOfStock && stockResultEvent.Status == StockStatus.InStock;
        if (!isRestock)
        {
            _logger.LogInformation(
                "StockResultEvent bildirim gerektirmiyor — ProductCode: {ProductCode}, önceki: {Previous}, yeni: {New}",
                stockResultEvent.ProductCode, previousStatus, stockResultEvent.Status);
            return;
        }

        var watcherUserIds = await _subscriptionClient.GetWatcherUserIdsAsync(
            stockResultEvent.ProductCode, stockResultEvent.Size, stockResultEvent.StoreId);

        if (watcherUserIds.Count == 0)
        {
            _logger.LogInformation(
                "Restock tespit edildi ama takip eden kullanıcı yok — ProductCode: {ProductCode}",
                stockResultEvent.ProductCode);
            return;
        }

        var subject = "Ürün tekrar stokta!";
        var body = $"Takip ettiğiniz ürün ({stockResultEvent.ProductCode}, beden {stockResultEvent.Size}) tekrar stokta.{BuildStockDetailSuffix(stockResultEvent)}";

        foreach (var userId in watcherUserIds)
        {
            await TryNotifyAsync(userId, NotificationChannel.Email, stockResultEvent, cancellationToken,
                () => SendEmailAsync(userId, subject, body, cancellationToken));

            await TryNotifyAsync(userId, NotificationChannel.Push, stockResultEvent, cancellationToken,
                () => SendPushAsync(userId, subject, body, cancellationToken));
        }
    }

    // Faz 6.1 — kullanıcı talebiyle eklendi: "eğer bu bilgiye erişmişsek mutlaka notification'da kullanıcıya
    // belirtelim." Scraper'lar StockResultEvent'e Quantity/IsLastUnit'i yalnızca GERÇEKTEN bildiklerinde
    // dolduruyor (bkz. Shared.Contracts.Messages.V1.StockResultEvent üstündeki yorum — markalar arası veri
    // şekli farklı: Bershka/Zara sayısal miktar veriyor, Mango API'nin kendi "son ürün" bayrağını). İkisi de
    // null'sa (ör. online kontrol, ya da markanın API'si hiç bu veriyi vermiyorsa) hiçbir ek metin
    // eklenmiyor — "bilmiyoruz" ile "bolca stok var" birbirine karıştırılmamalı.
    private static string BuildStockDetailSuffix(StockResultEvent stockResultEvent)
    {
        if (stockResultEvent.IsLastUnit == true)
        {
            return stockResultEvent.Quantity.HasValue
                ? $" Dikkat: yalnızca {stockResultEvent.Quantity} adet kaldı — mağazaya gidene kadar tükenebilir!"
                : " Dikkat: bu son ürün olabilir — mağazaya gidene kadar tükenebilir!";
        }

        if (stockResultEvent.Quantity is int quantity)
        {
            return $" Stokta {quantity} adet var.";
        }

        return string.Empty;
    }

    private async Task<StockStatus?> UpdateStateAndGetPreviousStatusAsync(StockResultEvent stockResultEvent, CancellationToken cancellationToken)
    {
        var state = await _db.WatchGroupNotificationStates.FirstOrDefaultAsync(s =>
            s.ProductCode == stockResultEvent.ProductCode &&
            s.Size == stockResultEvent.Size &&
            s.StoreId == stockResultEvent.StoreId,
            cancellationToken);

        var previousStatus = state?.LastKnownStatus;

        if (state is null)
        {
            state = new WatchGroupNotificationState
            {
                ProductCode = stockResultEvent.ProductCode,
                Size = stockResultEvent.Size,
                StoreId = stockResultEvent.StoreId
            };
            _db.WatchGroupNotificationStates.Add(state);
        }

        state.LastKnownStatus = stockResultEvent.Status;
        state.UpdatedAt = stockResultEvent.CheckedAt;
        await _db.SaveChangesAsync(cancellationToken);

        return previousStatus;
    }

    private async Task<(bool? Success, string? SkipReason)> SendEmailAsync(Guid userId, string subject, string body, CancellationToken cancellationToken)
    {
        var email = await _identityClient.GetUserEmailAsync(userId);
        if (email is null)
            return (null, "kullanıcının email adresi çözülemedi");

        var success = await _emailSender.SendAsync(email, subject, body, cancellationToken);
        return (success, null);
    }

    private async Task<(bool? Success, string? SkipReason)> SendPushAsync(Guid userId, string title, string body, CancellationToken cancellationToken)
    {
        var deviceToken = await _deviceTokenProvider.GetDeviceTokenAsync(userId);
        if (deviceToken is null)
            return (null, "kullanıcının kayıtlı push token'ı yok");

        var success = await _pushSender.SendAsync(deviceToken, title, body, cancellationToken);
        return (success, null);
    }

    private async Task TryNotifyAsync(
        Guid userId,
        NotificationChannel channel,
        StockResultEvent stockResultEvent,
        CancellationToken cancellationToken,
        Func<Task<(bool? Success, string? SkipReason)>> send)
    {
        var alreadySent = await _db.NotificationLogs.AnyAsync(n =>
            n.CommandId == stockResultEvent.CommandId && n.UserId == userId && n.Channel == channel,
            cancellationToken);

        if (alreadySent)
        {
            _logger.LogInformation(
                "Bildirim zaten gönderilmiş (idempotent atlama) — CommandId: {CommandId}, UserId: {UserId}, Channel: {Channel}",
                stockResultEvent.CommandId, userId, channel);
            return;
        }

        var (success, skipReason) = await send();

        if (success is null)
        {
            _logger.LogInformation(
                "Bildirim atlandı — UserId: {UserId}, Channel: {Channel}, sebep: {SkipReason}",
                userId, channel, skipReason);
            return;
        }

        _db.NotificationLogs.Add(new NotificationLog
        {
            UserId = userId,
            ProductCode = stockResultEvent.ProductCode,
            Size = stockResultEvent.Size,
            StoreId = stockResultEvent.StoreId,
            Channel = channel,
            CommandId = stockResultEvent.CommandId,
            Success = success.Value,
            SentAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
