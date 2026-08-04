namespace StockTracker.Billing.DTOs;

public record PlanDto(
    Guid Id,
    string Name,
    int MaxTrackedProducts,
    int CheckFrequencyMinutes,
    string? AppStoreProductId,
    string? PlayStoreProductId
);

public record UserPlanDto(
    Guid UserId,
    PlanDto Plan,
    DateTime AssignedAt
);

// platform: "Apple" | "Google". transactionIdOrToken: Apple için transactionId (App Store Server API
// GET /inApps/v1/transactions/{id}'ye gönderilir), Google için purchaseToken.
// subscriptionOrProductId: Google'da zorunlu (Play Developer API subscriptionId parametresi gerektirir),
// Apple'da kullanılmaz (transactionId tek başına yeterli).
public record VerifyPurchaseRequest(
    Guid UserId,
    string Platform,
    string TransactionIdOrToken,
    string? SubscriptionOrProductId
);

public record UserSubscriptionDto(
    Guid UserId,
    string Platform,
    string Status,
    DateTime? CurrentPeriodEnd
);

// Faz 4.3 — Subscription Service'in yeni bir UserWatch oluşturmadan önce sorduğu limit bilgisi.
// Kullanıcının henüz bir UserPlan satırı yoksa (ör. UserRegisteredEvent henüz işlenmedi) Free plan
// limitlerine düşülür — hiçbir zaman 404/hata döndürmez, her zaman uygulanabilir bir limit verir.
public record UserLimitsDto(
    Guid UserId,
    string PlanName,
    int MaxTrackedProducts,
    int CheckFrequencyMinutes
);
