namespace StockTracker.MangoScraper.Services;

// InStock: true/false/bilinmiyorsa null (Unknown'a çevrilir). Mango, Zara/Bershka'nın aksine ne online ne
// mağaza sorgusunda sayısal bir miktar vermiyor — Quantity bu markada HER ZAMAN null. Bunun yerine
// API'nin KENDİSİ doğrudan bir "son ürün" bayrağı veriyor (canlı doğrulandı — online PDP'de `lastUnits`,
// mağaza yanıtında `sizesIds[].isLastUnit`) — IsLastUnit, Bershka/Zara'daki gibi bizim türettiğimiz değil,
// API'den doğrudan gelen bir değer.
public record StockCheckResult(bool InStock, int? Quantity, bool? IsLastUnit);

public interface IMangoStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu. Mango'nun mağaza stok API'si mağaza ID'siyle değil
    // enlem/boylam ile çalıştığı için (bkz. MangoStockApiClient üstündeki not) storeLatitude/storeLongitude
    // zorunlu — StoreReference'tan gelen mağazanın KENDİ koordinatları.
    Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, double storeLatitude, double storeLongitude, CancellationToken cancellationToken);
}
