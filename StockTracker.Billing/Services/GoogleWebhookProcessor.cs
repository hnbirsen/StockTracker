using System.Text.Json;
using StockTracker.Billing.Entities;

namespace StockTracker.Billing.Services;

public enum GoogleWebhookResult
{
    Unauthorized,
    InvalidPayload,
    Skipped,
    Processed
}

public interface IGoogleWebhookProcessor
{
    Task<GoogleWebhookResult> ProcessAsync(string? bearerToken, string requestBody, CancellationToken cancellationToken);
}

// Real-time Developer Notifications — Cloud Pub/Sub push formatında gelir:
// {"message": {"data": "<base64 JSON>", "messageId": "...", "publishTime": "..."}, "subscription": "..."}
// data, base64 çözüldükten sonra {packageName, eventTimeMillis, subscriptionNotification: {notificationType, purchaseToken, subscriptionId}} içerir.
public class GoogleWebhookProcessor : IGoogleWebhookProcessor
{
    // Google Real-time Developer Notifications resmi notificationType kodları.
    private static readonly Dictionary<int, SubscriptionStatus> NotificationTypeMap = new()
    {
        [1] = SubscriptionStatus.Active,      // SUBSCRIPTION_RECOVERED
        [2] = SubscriptionStatus.Active,      // SUBSCRIPTION_RENEWED
        [3] = SubscriptionStatus.Cancelled,   // SUBSCRIPTION_CANCELED
        [4] = SubscriptionStatus.Active,      // SUBSCRIPTION_PURCHASED
        [5] = SubscriptionStatus.GracePeriod, // SUBSCRIPTION_ON_HOLD
        [6] = SubscriptionStatus.GracePeriod, // SUBSCRIPTION_IN_GRACE_PERIOD
        [7] = SubscriptionStatus.Active,      // SUBSCRIPTION_RESTARTED
        [10] = SubscriptionStatus.Cancelled,  // SUBSCRIPTION_PAUSED
        [12] = SubscriptionStatus.Refunded,   // SUBSCRIPTION_REVOKED
        [13] = SubscriptionStatus.Expired     // SUBSCRIPTION_EXPIRED
        // 8 (PRICE_CHANGE_CONFIRMED), 9 (DEFERRED), 11 (PAUSE_SCHEDULE_CHANGED), 19/20 vb. — durum
        // değiştirmeyen/ilgisiz bildirimler, haritada yok → Skipped.
    };

    private readonly IGoogleOidcTokenValidator _tokenValidator;
    private readonly IGooglePlayDeveloperClient _playClient;
    private readonly IPaymentEventProcessor _paymentEventProcessor;
    private readonly ILogger<GoogleWebhookProcessor> _logger;

    public GoogleWebhookProcessor(
        IGoogleOidcTokenValidator tokenValidator,
        IGooglePlayDeveloperClient playClient,
        IPaymentEventProcessor paymentEventProcessor,
        ILogger<GoogleWebhookProcessor> logger)
    {
        _tokenValidator = tokenValidator;
        _playClient = playClient;
        _paymentEventProcessor = paymentEventProcessor;
        _logger = logger;
    }

    public async Task<GoogleWebhookResult> ProcessAsync(string? bearerToken, string requestBody, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken) || !await _tokenValidator.ValidateAsync(bearerToken, cancellationToken))
        {
            _logger.LogWarning("Google webhook — OIDC bearer token doğrulaması başarısız, istek reddedildi.");
            return GoogleWebhookResult.Unauthorized;
        }

        JsonElement envelope;
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            envelope = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            _logger.LogWarning("Google webhook — geçersiz JSON gövdesi.");
            return GoogleWebhookResult.InvalidPayload;
        }

        if (!envelope.TryGetProperty("message", out var message) || !message.TryGetProperty("data", out var dataProp))
        {
            _logger.LogWarning("Google webhook — Pub/Sub zarfında message.data bulunamadı.");
            return GoogleWebhookResult.InvalidPayload;
        }

        var messageId = message.TryGetProperty("messageId", out var messageIdProp) ? messageIdProp.GetString() : null;
        if (string.IsNullOrEmpty(messageId))
        {
            _logger.LogWarning("Google webhook — Pub/Sub zarfında messageId bulunamadı, idempotency anahtarı üretilemiyor.");
            return GoogleWebhookResult.InvalidPayload;
        }

        var dataJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(dataProp.GetString()!));
        using var dataDoc = JsonDocument.Parse(dataJson);
        var data = dataDoc.RootElement;

        if (!data.TryGetProperty("subscriptionNotification", out var subNotification))
        {
            // testNotification / oneTimeProductNotification — bu servis yalnızca abonelikleri işliyor.
            _logger.LogInformation("Google webhook — subscriptionNotification içermiyor, atlandı.");
            return GoogleWebhookResult.Skipped;
        }

        var notificationType = subNotification.GetProperty("notificationType").GetInt32();
        var purchaseToken = subNotification.GetProperty("purchaseToken").GetString()!;
        var subscriptionId = subNotification.GetProperty("subscriptionId").GetString()!;

        if (!NotificationTypeMap.TryGetValue(notificationType, out var newStatus))
        {
            _logger.LogInformation("Google webhook — notificationType {NotificationType} durum değişikliği gerektirmiyor, atlandı.", notificationType);
            return GoogleWebhookResult.Skipped;
        }

        // RTDN mesajının kendisi son kullanım tarihini taşımıyor — güncel değeri almak için Play Developer
        // API'sine best-effort bir sorgu yapılır (yapılandırılmamışsa null kalır, durum yine de güncellenir).
        var subscriptionInfo = await _playClient.GetSubscriptionAsync(subscriptionId, purchaseToken, cancellationToken);

        await _paymentEventProcessor.ProcessAsync(
            Platform.Google,
            eventId: messageId,
            eventType: notificationType.ToString(),
            rawPayload: requestBody,
            transactionIdentifier: purchaseToken,
            newStatus: newStatus,
            currentPeriodEnd: subscriptionInfo?.ExpiryTime,
            cancellationToken);

        return GoogleWebhookResult.Processed;
    }
}
