namespace StockTracker.Shared.Contracts.Messages.V1;

// v1 sözleşmesi: breaking değişiklik gerekirse yeni tip V2 namespace'inde eklenir, bu tip değişmez.
// StoreId, Store Reference Service (Faz 2.3) devreye girene kadar null'dur — City/District ham metin
// olarak taşınır; scraper o zamana kadar sadece online stok kontrolü yapar.
public record CheckStockCommand(
    Guid CommandId,
    string ProductCode,
    Guid BrandId,
    string BrandName,
    string Size,
    Guid? StoreId,
    string? City,
    string? District,
    DateTime RequestedAt
);
