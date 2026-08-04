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
