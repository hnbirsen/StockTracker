namespace StockTracker.StradivariusScraper.Services;

// InStock: true/false/bilinmiyorsa null (Unknown'a çevrilir). Quantity: mağaza bazlı kontrolde API'nin
// KENDİ verdiği gerçek sayısal stok adedi (`stock`) — canlı kullanıcı trafiğiyle doğrulandı
// (`skus-availability-in-stores` yanıtı). Online kontrolde her zaman null (yalnızca `disabled` boolean var,
// sayı yok). IsLastUnit: online kontrolde SSR'daki "Stok tükenmek üzere" (LowStock) metninden; mağaza
// kontrolünde Quantity biliniyorsa `Quantity == 1`'den türetiliyor (Zara/Massimo Dutti'deki gibi, API'de
// ayrı bir "son ürün" bayrağı yok).
public record StockCheckResult(bool InStock, int? Quantity, bool? IsLastUnit);

public interface IStradivariusStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu. `brandSpecificStoreId`, Stradivarius'un kendi
    // `itxrest/2/bam/store/{storeId}/physical-store` mağaza bulucu API'sinin döndürdüğü GERÇEK sayısal
    // mağaza ID'si (ör. "16879" — City's Kozyatağı) — Zara/Massimo Dutti/Pull&Bear'daki gibi.
    Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken);
}
