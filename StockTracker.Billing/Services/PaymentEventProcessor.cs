using Microsoft.EntityFrameworkCore;
using StockTracker.Billing.Data;
using StockTracker.Billing.Entities;

namespace StockTracker.Billing.Services;

public interface IPaymentEventProcessor
{
    // false dönerse event zaten işlenmiş demektir (idempotent atlama) — çağıran taraf bunu normal kabul eder.
    Task<bool> ProcessAsync(
        Platform provider,
        string eventId,
        string eventType,
        string rawPayload,
        string transactionIdentifier,
        SubscriptionStatus newStatus,
        DateTimeOffset? currentPeriodEnd,
        CancellationToken cancellationToken);
}

// Apple webhook, Google webhook ve (dolaylı olarak) POST /billing/verify-purchase'ın ortak çekirdeği:
// event'i idempotent şekilde kaydeder, eşleşen UserSubscription'ı günceller, kullanıcının Plan'ını
// (Free <-> Premium) abonelik durumuna göre senkronize eder.
public class PaymentEventProcessor : IPaymentEventProcessor
{
    private readonly BillingDbContext _db;
    private readonly IUserPlanService _userPlanService;
    private readonly ILogger<PaymentEventProcessor> _logger;

    public PaymentEventProcessor(BillingDbContext db, IUserPlanService userPlanService, ILogger<PaymentEventProcessor> logger)
    {
        _db = db;
        _userPlanService = userPlanService;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(
        Platform provider,
        string eventId,
        string eventType,
        string rawPayload,
        string transactionIdentifier,
        SubscriptionStatus newStatus,
        DateTimeOffset? currentPeriodEnd,
        CancellationToken cancellationToken)
    {
        var alreadyProcessed = await _db.PaymentEvents.AnyAsync(e => e.Provider == provider && e.EventId == eventId, cancellationToken);
        if (alreadyProcessed)
        {
            _logger.LogInformation("PaymentEvent zaten işlenmiş (idempotent atlama) — Provider: {Provider}, EventId: {EventId}", provider, eventId);
            return false;
        }

        // Apple: transactionIdentifier = originalTransactionId (StoreTransactionId). Google: purchaseToken.
        var subscription = provider == Platform.Apple
            ? await _db.UserSubscriptions.FirstOrDefaultAsync(s => s.Platform == provider && s.StoreTransactionId == transactionIdentifier, cancellationToken)
            : await _db.UserSubscriptions.FirstOrDefaultAsync(s => s.Platform == provider && s.PurchaseToken == transactionIdentifier, cancellationToken);

        _db.PaymentEvents.Add(new PaymentEvent
        {
            SubscriptionId = subscription?.Id,
            Provider = provider,
            EventId = eventId,
            EventType = eventType,
            RawPayload = rawPayload
        });

        if (subscription is null)
        {
            // Bu transaction/token için henüz POST /billing/verify-purchase ile bir UserId eşleşmesi
            // kurulmamış — event yine de idempotency/denetim amacıyla kaydedildi, ama bir kullanıcının
            // planını güncelleyecek bir bilgimiz yok.
            _logger.LogWarning(
                "PaymentEvent, bilinen bir UserSubscription'a eşleşmedi — Provider: {Provider}, transactionIdentifier: {TransactionIdentifier}",
                provider, transactionIdentifier);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        subscription.Status = newStatus;
        subscription.CurrentPeriodEnd = currentPeriodEnd?.UtcDateTime;
        subscription.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var isEntitled = newStatus is SubscriptionStatus.Active or SubscriptionStatus.GracePeriod;
        await _userPlanService.SetPlanAsync(subscription.UserId, isEntitled ? BillingDbContext.PremiumPlanId : BillingDbContext.FreePlanId);

        _logger.LogInformation(
            "UserSubscription güncellendi — UserId: {UserId}, Status: {Status}, Plan: {Plan}",
            subscription.UserId, newStatus, isEntitled ? "Premium" : "Free");

        return true;
    }
}
