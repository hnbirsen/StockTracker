namespace StockTracker.MaviScraper.Services;

// InStock: true/false/bilinmiyorsa null (Unknown'a çevrilir). Quantity: online kontrolde PDP'nin kendi
// verdiği gerçek stok adedi (`stockLevel`) — mağaza kontrolünde ise Mavi'nin mağaza API'si sayısal bir
// adet VERMİYOR (yalnızca "bu barkod bu mağazada var/yok" — Beymen/Zara/Mango'daki sparse-yanıt deseniyle
// aynı), bu yüzden mağaza kontrolünde her zaman null. IsLastUnit: online kontrolde Quantity'den
// (`Quantity == 1`) türetiliyor (Bershka'daki gerekçenin aynısı) — mağaza kontrolünde miktar bilgisi
// hiç olmadığı için her zaman null.
public record StockCheckResult(bool InStock, int? Quantity, bool? IsLastUnit);

public interface IMaviStockApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan).
    // productUrl: ProductBrandMap'ten gelen tam ürün sayfası URL'i — gerçek barkod/stok bilgisini bu
    // sayfadan okuyoruz (bkz. MaviStockApiClient üstündeki yorum).
    Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu — Mavi'nin mağaza API'si enlem/boylam gerektirdiği için
    // (Mango/H&M'deki gibi "yakındaki mağazalar" modeli) storeLatitude/storeLongitude de veriliyor.
    Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, double storeLatitude, double storeLongitude, string productUrl, CancellationToken cancellationToken);
}
