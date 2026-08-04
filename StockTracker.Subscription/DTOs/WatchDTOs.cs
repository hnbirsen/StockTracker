using StockTracker.Shared.Contracts.Messages.V1;

namespace StockTracker.Subscription.DTOs;

public record CreateWatchRequest(Guid UserId, string ProductCode, string Size, Guid? StoreId);

public record WatchDto(
    Guid UserWatchId,
    Guid WatchGroupId,
    string ProductCode,
    string Size,
    Guid? StoreId,
    StockStatus? LastKnownStatus,
    DateTime? LastCheckedAt,
    DateTime CreatedAt
);

// Faz 4.3 — dedup nedeniyle her zaman bir WatchDto döner değil; plan limiti aşıldığında Watch=null,
// ErrorCode="WATCH_LIMIT_EXCEEDED" ile başarısız sonuç döner.
public record CreateWatchResult(bool Success, WatchDto? Watch, string? ErrorCode, string? ErrorMessage);
