using System.Text.Json;

namespace StockTracker.Billing.Services;

public interface IAppleWebhookProcessor
{
    Task<bool> ProcessAsync(string signedPayload, CancellationToken cancellationToken);
}

// App Store Server Notifications V2 — gövde tek alanlı: {"signedPayload": "<JWS>"}.
// JWS'in kendisi (AppleJwsVerifier ile doğrulanır) notificationType/subtype/notificationUUID ve içinde
// bir başka JWS olan data.signedTransactionInfo'yu taşır (aynı formatta, tekrar doğrulanması gerekir).
public class AppleWebhookProcessor : IAppleWebhookProcessor
{
    // Apple'ın resmi notificationType/subtype değerleri (bkz. App Store Server Notifications V2 referansı).
    // Eşlenmeyen/ilgisiz tipler (PRICE_INCREASE, RENEWAL_EXTENDED, TEST vb.) durum değiştirmez — null döner.
    private static SubscriptionStatusMapping? MapNotification(string notificationType, string? subtype) => notificationType switch
    {
        "SUBSCRIBED" or "DID_RENEW" => new(Entities.SubscriptionStatus.Active),
        "DID_CHANGE_RENEWAL_STATUS" => null, // otomatik yenileme açık/kapalı — mevcut erişimi değiştirmez
        "DID_FAIL_TO_RENEW" => subtype == "GRACE_PERIOD"
            ? new(Entities.SubscriptionStatus.GracePeriod)
            : new(Entities.SubscriptionStatus.Expired),
        "GRACE_PERIOD_EXPIRED" or "EXPIRED" => new(Entities.SubscriptionStatus.Expired),
        "REFUND" or "REVOKE" => new(Entities.SubscriptionStatus.Refunded),
        _ => null
    };

    private readonly IAppleJwsVerifier _jwsVerifier;
    private readonly IPaymentEventProcessor _paymentEventProcessor;
    private readonly ILogger<AppleWebhookProcessor> _logger;

    public AppleWebhookProcessor(IAppleJwsVerifier jwsVerifier, IPaymentEventProcessor paymentEventProcessor, ILogger<AppleWebhookProcessor> logger)
    {
        _jwsVerifier = jwsVerifier;
        _paymentEventProcessor = paymentEventProcessor;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(string signedPayload, CancellationToken cancellationToken)
    {
        if (!_jwsVerifier.TryVerifyAndDecode(signedPayload, out var notification))
        {
            _logger.LogWarning("Apple webhook — dış zarf (signedPayload) imza doğrulaması başarısız, istek reddedildi.");
            return false;
        }

        var notificationType = notification.GetProperty("notificationType").GetString()!;
        var subtype = notification.TryGetProperty("subtype", out var subtypeProp) ? subtypeProp.GetString() : null;
        var notificationUuid = notification.GetProperty("notificationUUID").GetString()!;

        if (!notification.TryGetProperty("data", out var data) || !data.TryGetProperty("signedTransactionInfo", out var signedTransactionInfoProp))
        {
            _logger.LogWarning("Apple webhook — data.signedTransactionInfo bulunamadı, işlenemiyor.");
            return false;
        }

        if (!_jwsVerifier.TryVerifyAndDecode(signedTransactionInfoProp.GetString()!, out var transaction))
        {
            _logger.LogWarning("Apple webhook — iç zarf (signedTransactionInfo) imza doğrulaması başarısız.");
            return false;
        }

        var originalTransactionId = transaction.GetProperty("originalTransactionId").GetString()!;
        DateTimeOffset? expiresAt = transaction.TryGetProperty("expiresDate", out var expiresProp)
            ? DateTimeOffset.FromUnixTimeMilliseconds(expiresProp.GetInt64())
            : null;

        var mapping = MapNotification(notificationType, subtype);
        if (mapping is null)
        {
            _logger.LogInformation("Apple webhook — {NotificationType}/{Subtype} durum değişikliği gerektirmiyor, atlandı.", notificationType, subtype);
            return true;
        }

        await _paymentEventProcessor.ProcessAsync(
            Entities.Platform.Apple,
            eventId: notificationUuid,
            eventType: subtype is null ? notificationType : $"{notificationType}.{subtype}",
            rawPayload: signedPayload,
            transactionIdentifier: originalTransactionId,
            newStatus: mapping.Status,
            currentPeriodEnd: expiresAt,
            cancellationToken);

        return true;
    }

    private record SubscriptionStatusMapping(Entities.SubscriptionStatus Status);
}
