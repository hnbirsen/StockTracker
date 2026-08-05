namespace StockTracker.BershkaScraper.Services;

// InStock: true/false/bilinmiyorsa null (Unknown'a çevrilir). Quantity: gerçek API'nin verdiği kesin
// stok adedi (biliniyorsa) — Bershka'nın mağaza stok API'si `quantity` alanı veriyor (bkz.
// BershkaStockApiClient üstündeki yorum), online kontrolde her zaman null (yalnızca "in_stock"/
// "coming_soon"/"out_of_stock" string'i var, sayı yok). IsLastUnit: Quantity biliniyorsa `Quantity == 1`
// olarak türetiliyor — Bershka'nın kendi API'sinde ayrı bir "son ürün" bayrağı yok, bu doğrudan miktardan
// çıkarılan bir sonuç (Mango'daki gibi API'nin kendi verdiği bir bayrak değil).
public record StockCheckResult(bool InStock, int? Quantity, bool? IsLastUnit);

public interface IBershkaStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    // productUrl: ProductBrandMap'ten gelen tam ürün sayfası URL'i — gerçek part-number/campaign/beden
    // bilgisini bu sayfadan okuyoruz (bkz. BershkaStockApiClient üstündeki yorum).
    Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu (brandSpecificStoreId = Store Reference'tan gelen mağaza kodu).
    Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken);
}
