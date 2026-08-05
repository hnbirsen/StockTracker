namespace StockTracker.PullBearScraper.Services;

// InStock: true/false/bilinmiyorsa null (Unknown'a çevrilir). Quantity: mağaza bazlı kontrolde API'nin
// KENDİ verdiği gerçek sayısal stok adedi (`sizesAvailability[].stock`) — Massimo Dutti/Zara/Bershka'yla aynı
// gerekçe; online kontrolde her zaman null (yalnızca `isBuyable` boolean var, sayı yok). IsLastUnit: Quantity
// biliniyorsa `Quantity == 1` olarak türetiliyor (Zara/Massimo Dutti'deki gibi, API'de ayrı bir "son ürün"
// bayrağı yok).
public record StockCheckResult(bool InStock, int? Quantity, bool? IsLastUnit);

public interface IPullBearStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu. Massimo Dutti'deki gibi enlem/boylam GEREKMİYOR —
    // Pull&Bear'ın gerçek mağaza stok API'si (`api/storefront/1/stores/.../products/.../available-sizes`)
    // doğrudan mağaza ID'si (Zara/Massimo Dutti'deki gibi) ve ürünün `catEntryId`/`mastersSizeId` değerlerini
    // alıyor; bu ikisi yalnızca PDP'den (productUrl üzerinden) çözülebiliyor.
    Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken);
}
