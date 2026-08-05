namespace StockTracker.HmScraper.Services;

// InStock: true/false/bilinmiyorsa null (Unknown'a çevrilir). Quantity HER ZAMAN null — H&M'in mağaza
// API'sinin verdiği `avaiQty` alanı CANLI VERİYLE doğrulanan bir bulguya göre gerçek bir sayı DEĞİL,
// kabaca gruplanmış bir değer (yalnızca 0, 1000, 2000 ya da 3000 gözlemlendi — asla ara bir değer değil).
// "Stokta 1000 adet var" gibi bir bildirim metni kullanıcıyı yanıltır, bu yüzden bilinçli olarak
// StockResultEvent'e taşınmıyor. IsLastUnit, H&M'in kendi "trafik ışığı" bayrağından (`traffLightInd`:
// R/Y/G) doğrudan geliyor — Y ("birkaç tane kaldı") ise true.
public record StockCheckResult(bool InStock, int? Quantity, bool? IsLastUnit);

public interface IHmStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu. H&M'in mağaza stok API'si mağaza ID'siyle değil
    // enlem/boylam ile çalıştığı için (Mango'yla aynı model) storeLatitude/storeLongitude zorunlu.
    Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, double storeLatitude, double storeLongitude, string productUrl, CancellationToken cancellationToken);
}
