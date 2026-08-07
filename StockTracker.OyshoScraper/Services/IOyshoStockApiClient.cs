namespace StockTracker.OyshoScraper.Services;

// InStock: true/false/bilinmiyorsa null (Unknown'a çevrilir). Quantity: gerçek API'nin verdiği kesin
// stok adedi (mağaza sorgusunda biliniyor — bkz. OyshoStockApiClient üstündeki yorum), online kontrolde
// her zaman null (yalnızca "in_stock"/"coming_soon"/"out_of_stock" string'i var, sayı yok). IsLastUnit:
// mağaza sorgusunda Quantity'den (`Quantity == 1`) türetiliyor; online kontrolde PDP'nin KENDİ verdiği
// `hasFewUnits` bayrağından DOĞRUDAN geliyor (Mango'nun `lastUnits`'iyle aynı desen — bizim çıkardığımız
// bir sonuç değil, API'nin kendi sinyali).
public record StockCheckResult(bool InStock, int? Quantity, bool? IsLastUnit);

public interface IOyshoStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    // productUrl: ProductBrandMap'ten gelen tam ürün sayfası URL'i — gerçek part-number/campaign/beden
    // bilgisini bu sayfadan okuyoruz (bkz. OyshoStockApiClient üstündeki yorum).
    Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu (brandSpecificStoreId = Store Reference'tan gelen mağaza kodu).
    Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken);
}
