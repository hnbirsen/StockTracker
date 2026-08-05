namespace StockTracker.ZaraScraper.Services;

// InStock: true/false/bilinmiyorsa null (Unknown'a çevrilir). Quantity: Zara'nın mağaza stok API'sinin
// verdiği kesin stok adedi (`sizesAvailability[].stock`) — online kontrolde her zaman null (yalnızca
// "in_stock"/"low_on_stock" gibi string enum var, sayı yok). IsLastUnit: Quantity biliniyorsa
// `Quantity == 1` olarak türetiliyor — Zara'nın API'sinde ayrı bir "son ürün" bayrağı yok.
public record StockCheckResult(bool InStock, int? Quantity, bool? IsLastUnit);

public interface IZaraStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    // productUrl: ProductBrandMap'ten gelen tam ürün sayfası URL'i — gerçek beden/renk/availability
    // bilgisini bu sayfadan okuyoruz (bkz. ZaraStockApiClient üstündeki yorum).
    Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu (brandSpecificStoreId = Store Reference'tan gelen,
    // Zara'nın kendi physicalStoreId'si — ör. Kentpark/Ankara için "251").
    Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken);
}
