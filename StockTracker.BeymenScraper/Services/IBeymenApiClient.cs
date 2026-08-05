namespace StockTracker.BeymenScraper.Services;

// InStock: true/false/bilinmiyorsa null (Unknown'a çevrilir). Quantity: online kontrolde API'nin kendi verdiği
// gerçek sayısal stok adedi (`sizes[].stockQuantity`) — mağaza kontrolünde API sayısal miktar vermiyor
// (yalnızca `IsAboutToRunOut` boolean bayrağı var), bu yüzden mağaza kontrolünde her zaman null. IsLastUnit:
// online kontrolde Quantity==1'den türetiliyor (Zara'daki gibi); mağaza kontrolünde ise API'nin kendi
// `IsAboutToRunOut` bayrağından geliyor (TAM "son ürün" anlamına gelmiyor, "azalıyor/tükenmek üzere" eşik
// sinyali — en yakın mevcut sinyal olduğu için kullanılıyor, bkz. BeymenApiClient üstündeki not).
public record StockCheckResult(bool InStock, int? Quantity, bool? IsLastUnit);

public interface IBeymenApiClient
{
    // Ürünün genel online stok durumu (fiziksel mağaza bilgisi olmadan). Beymen'de productCode tek başına
    // yeterli — Playwright/PDP fetch'e hiç gerek yok (bkz. BeymenApiClient üstündeki not).
    Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, CancellationToken cancellationToken);

    // Belirli bir fiziksel mağazadaki stok durumu. brandSpecificStoreId = Beymen mağazasının kendi adı
    // (ör. "Beymen Suadiye") — bu API'de sayısal bir mağaza ID'si yok, mağazalar isimleriyle döndürülüyor.
    Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, CancellationToken cancellationToken);
}
