namespace StockTracker.Shared.Contracts.Messages.V1;

// v1 sözleşmesi: breaking değişiklik gerekirse yeni tip V2 namespace'inde eklenir, bu tip değişmez.
// StoreId, Search Orchestrator'ın Store Reference Service'ten çözdüğü gerçek mağaza ID'sidir;
// o il/ilçe için kayıtlı mağaza yoksa null kalır ve scraper sadece online stok kontrolü yapar.
// BrandSpecificStoreId, Store Reference'tan gelen ve scraper'ın markanın kendi API'sinde kullandığı koddur
// (Search Orchestrator ekstra round-trip'e gerek kalmadan bunu Store Reference'tan alıp buraya taşır).
// City/District her durumda ham metin olarak taşınır.
public record CheckStockCommand(
    Guid CommandId,
    string ProductCode,
    Guid BrandId,
    string BrandName,
    string Size,
    Guid? StoreId,
    string? BrandSpecificStoreId,
    string? City,
    string? District,
    DateTime RequestedAt
);
