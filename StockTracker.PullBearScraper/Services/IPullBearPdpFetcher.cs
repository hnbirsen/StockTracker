namespace StockTracker.PullBearScraper.Services;

// Pull&Bear ürün sayfaları (PDP) Zara/Bershka/Massimo Dutti gibi Akamai Bot Manager'ın arkasında — canlı
// doğrulandı: düz `curl` (gerçekçi UA ile bile) sayfanın kendisi yerine bir `bm-verify` sorgu parametresiyle
// 5 saniyelik bir JS-yönlendirme (challenge) sayfası döndürüyor. Gerçek Chrome kanalı üzerinden Playwright
// gerekiyor (bkz. PlaywrightPullBearFetcher).
//
// Massimo Dutti ile TAMAMEN AYNI PLATFORM: Pull&Bear de aynı `product-modular` custom element'ini ve
// `__product` JS özelliğini kullanıyor (canlı doğrulandı — ikisi de aynı "MD Front" tarzı Inditex
// alt-yapısını paylaşıyor). Mağaza stok API'si (`api/storefront/1/stores/.../products/.../available-sizes`)
// AYRI ve KORUMASIZ (canlı doğrulandı: düz `curl` ile 200 + gerçek veri, gerçek sayısal stok adediyle). Bu
// yüzden mağaza sorgusu bu fetcher'da YOK — Massimo Dutti'deki gibi ayrı, düz bir HttpClient ile (bkz.
// PullBearStockApiClient.CheckStoreStockAsync) çağrılıyor. Bu API'yi çağırmak için PDP'den iki değer
// gerekiyor: seçili rengin `catentryId`'si ve hedef bedenin `mastersSizeId`'si — bu yüzden aşağıdaki
// SizeEntry ikisini de taşıyor.
public interface IPullBearPdpFetcher
{
    // Ürün sayfasındaki `product-modular` custom element'inin `__product.detail.colors[].sizes[]`
    // dizisinden okunan, düzleştirilmiş (flat) bir List&lt;SizeEntry&gt; (Name, ColorId, CatEntryId,
    // MastersSizeId, IsBuyable, BackSoon) JSON'u döner. Başarısız olursa (timeout, Akamai challenge sayfası,
    // veri bulunamaması) null döner.
    Task<string?> FetchProductDataJsonAsync(string productUrl, CancellationToken cancellationToken);
}
