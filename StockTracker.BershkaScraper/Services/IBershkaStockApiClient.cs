namespace StockTracker.BershkaScraper.Services;

public interface IBershkaStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    // productUrl: ProductBrandMap'ten gelen tam ürün sayfası URL'i — gerçek part-number/campaign/beden
    // bilgisini bu sayfadan okuyoruz (bkz. BershkaStockApiClient üstündeki yorum).
    Task<bool?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu (brandSpecificStoreId = Store Reference'tan gelen mağaza kodu).
    Task<bool?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken);
}
