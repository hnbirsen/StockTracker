namespace StockTracker.Shared.Contracts.Messages.V1;

public enum StockStatus
{
    InStock,
    OutOfStock,
    Unknown
}

// v1 sözleşmesi: breaking değişiklik gerekirse yeni tip V2 namespace'inde eklenir, bu tip değişmez.
// StoreId, ilgili CheckStockCommand'daki gibi nullable — online-only kontrollerde null'dur.
public record StockResultEvent(
    Guid CommandId,
    string ProductCode,
    Guid BrandId,
    string Size,
    Guid? StoreId,
    StockStatus Status,
    DateTime CheckedAt,
    string? ScraperSource = null
);
