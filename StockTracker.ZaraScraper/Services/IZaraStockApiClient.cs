namespace StockTracker.ZaraScraper.Services;

public interface IZaraStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    // productUrl: ProductBrandMap'ten gelen tam ürün sayfası URL'i — gerçek beden/renk/availability
    // bilgisini bu sayfadan okuyoruz (bkz. ZaraStockApiClient üstündeki yorum).
    Task<bool?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu (brandSpecificStoreId = Store Reference'tan gelen,
    // Zara'nın kendi physicalStoreId'si — ör. Kentpark/Ankara için "251").
    Task<bool?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken);
}
