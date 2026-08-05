namespace StockTracker.MangoScraper.Services;

public interface IMangoStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    Task<bool?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu. Mango'nun mağaza stok API'si mağaza ID'siyle değil
    // enlem/boylam ile çalıştığı için (bkz. MangoStockApiClient üstündeki not) storeLatitude/storeLongitude
    // zorunlu — StoreReference'tan gelen mağazanın KENDİ koordinatları.
    Task<bool?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, double storeLatitude, double storeLongitude, CancellationToken cancellationToken);
}
