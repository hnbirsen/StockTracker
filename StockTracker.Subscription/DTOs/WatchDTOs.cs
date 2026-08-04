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
