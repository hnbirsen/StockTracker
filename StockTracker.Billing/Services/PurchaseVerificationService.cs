using Microsoft.EntityFrameworkCore;
using StockTracker.Billing.Data;
using StockTracker.Billing.DTOs;
using StockTracker.Billing.Entities;

namespace StockTracker.Billing.Services;

public record VerifyPurchaseResult(bool Success, string? FailureReason, UserSubscriptionDto? Subscription);

public interface IPurchaseVerificationService
{
    Task<VerifyPurchaseResult> VerifyAndRecordAsync(VerifyPurchaseRequest request, CancellationToken cancellationToken);
}

// POST /billing/verify-purchase'ın çekirdeği — mobil client'ın App Store/Play Store'da tamamladığı bir
// satın almayı ilgili store'un server-to-server API'sine karşı doğrular, doğrulanırsa kullanıcıyı hemen
// Premium'a yükseltir (webhook'u beklemeden — kullanıcı deneyimi için satın alma anında erişim açılmalı;
// webhook'lar (Faz 4.2, PaymentEventProcessor) sonraki yaşam döngüsü değişikliklerini işler).
//
// Not: Plan seçimi şu an her zaman Premium — Plans.AppStoreProductId/PlayStoreProductId gerçek store
// ürünleri oluşturulana kadar null olduğundan (bkz. .claude/ARCHITECTURE.md > Billing), productId'den
// plan'a güvenilir bir eşleme henüz mümkün değil. Bu MVP'nin iki-planlı (Free/Premium) yapısıyla tutarlı.
public class PurchaseVerificationService : IPurchaseVerificationService
{
    private readonly BillingDbContext _db;
    private readonly IAppleAppStoreServerClient _appleClient;
    private readonly IGooglePlayDeveloperClient _googleClient;
    private readonly IUserPlanService _userPlanService;
    private readonly ILogger<PurchaseVerificationService> _logger;

    public PurchaseVerificationService(
        BillingDbContext db,
        IAppleAppStoreServerClient appleClient,
        IGooglePlayDeveloperClient googleClient,
        IUserPlanService userPlanService,
        ILogger<PurchaseVerificationService> logger)
    {
        _db = db;
        _appleClient = appleClient;
        _googleClient = googleClient;
        _userPlanService = userPlanService;
        _logger = logger;
    }

    public async Task<VerifyPurchaseResult> VerifyAndRecordAsync(VerifyPurchaseRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Platform>(request.Platform, ignoreCase: true, out var platform))
            return new VerifyPurchaseResult(false, "geçersiz platform (Apple veya Google olmalı)", null);

        string storeTransactionId = string.Empty;
        string? purchaseToken = null;
        SubscriptionStatus status;
        DateTimeOffset? currentPeriodEnd;

        if (platform == Platform.Apple)
        {
            var info = await _appleClient.GetTransactionInfoAsync(request.TransactionIdOrToken, cancellationToken);
            if (info is null)
                return new VerifyPurchaseResult(false, "Apple doğrulaması başarısız veya yapılandırılmamış", null);

            storeTransactionId = info.OriginalTransactionId;
            currentPeriodEnd = info.ExpiresAt;
            status = currentPeriodEnd is not null && currentPeriodEnd > DateTimeOffset.UtcNow ? SubscriptionStatus.Active : SubscriptionStatus.Expired;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.SubscriptionOrProductId))
                return new VerifyPurchaseResult(false, "Google için subscriptionOrProductId zorunlu", null);

            var info = await _googleClient.GetSubscriptionAsync(request.SubscriptionOrProductId, request.TransactionIdOrToken, cancellationToken);
            if (info is null)
                return new VerifyPurchaseResult(false, "Google doğrulaması başarısız veya yapılandırılmamış", null);

            purchaseToken = request.TransactionIdOrToken;
            currentPeriodEnd = info.ExpiryTime;
            status = currentPeriodEnd is not null && currentPeriodEnd > DateTimeOffset.UtcNow ? SubscriptionStatus.Active : SubscriptionStatus.Expired;
        }

        var subscription = await _db.UserSubscriptions.FirstOrDefaultAsync(s => s.UserId == request.UserId, cancellationToken);
        if (subscription is null)
        {
            subscription = new UserSubscription { UserId = request.UserId, PlanId = BillingDbContext.PremiumPlanId };
            _db.UserSubscriptions.Add(subscription);
        }

        subscription.PlanId = BillingDbContext.PremiumPlanId;
        subscription.Platform = platform;
        subscription.StoreTransactionId = platform == Platform.Apple ? storeTransactionId : subscription.StoreTransactionId;
        subscription.PurchaseToken = platform == Platform.Google ? purchaseToken : subscription.PurchaseToken;
        subscription.Status = status;
        subscription.CurrentPeriodEnd = currentPeriodEnd?.UtcDateTime;
        subscription.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var isEntitled = status is SubscriptionStatus.Active or SubscriptionStatus.GracePeriod;
        await _userPlanService.SetPlanAsync(request.UserId, isEntitled ? BillingDbContext.PremiumPlanId : BillingDbContext.FreePlanId);

        _logger.LogInformation("Satın alma doğrulandı — UserId: {UserId}, Platform: {Platform}, Status: {Status}", request.UserId, platform, status);

        return new VerifyPurchaseResult(true, null, new UserSubscriptionDto(request.UserId, platform.ToString(), status.ToString(), subscription.CurrentPeriodEnd));
    }
}
