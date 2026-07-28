namespace StockTracker.Shared.Contracts.Messages.V1;

// v1 sözleşmesi: breaking değişiklik gerekirse yeni tip V2 namespace'inde eklenir, bu tip değişmez.
public record CheckStockCommand(
    Guid CommandId,
    string ProductCode,
    Guid BrandId,
    string BrandName,
    string Size,
    Guid StoreId,
    DateTime RequestedAt
);
