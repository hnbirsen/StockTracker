namespace StockTracker.MassimoDuttiScraper.Services;

// Massimo Dutti ürün sayfaları (PDP) Zara/Bershka gibi Akamai Bot Manager'ın arkasında — canlı doğrulandı:
// düz `curl` (gerçekçi UA ile bile) sayfanın kendisi yerine bir `bm-verify` sorgu parametresiyle 5 saniyelik
// bir JS-yönlendirme (challenge) sayfası döndürüyor (`<meta http-equiv="refresh" ...>`), gerçek HTML/veri asla
// gelmiyor. Gerçek Chrome kanalı üzerinden Playwright gerekiyor (bkz. PlaywrightMassimoDuttiFetcher).
//
// Zara'dan FARKI: Zara'da online VE mağaza stoğu ikisi de aynı Akamai korumasına tabi (tek Playwright oturumu
// yeterli). Massimo Dutti'de SADECE ürün sayfası (ve `itxrest/2/catalog/.../detail` API'si) Akamai korumalı —
// mağaza stok API'si (`api/storefront/1/stores/.../products/.../available-sizes`) AYRI ve KORUMASIZ (canlı
// doğrulandı: düz `curl` ile 200 + gerçek veri, gerçek sayısal stok adediyle). Bu yüzden mağaza sorgusu bu
// fetcher'da YOK — Bershka'daki gibi ayrı, düz bir HttpClient ile (bkz. MassimoDuttiStockApiClient.CheckStoreStockAsync)
// çağrılıyor. Bu API'yi çağırmak için PDP'den iki değer gerekiyor: seçili rengin `catentryId`'si (ürün URL'indeki
// `pelement` ile aynı) ve hedef bedenin `mastersSizeId`'si — bu yüzden aşağıdaki SizeEntry ikisini de taşıyor.
public interface IMassimoDuttiPdpFetcher
{
    // Ürün sayfasındaki `#mdfrontw-state` script'inin (Angular SSR state) `ITX_GET_PRODUCT_DETAIL_KEY.colors[].sizes[]`
    // dizisinden okunan, düzleştirilmiş (flat) bir List&lt;SizeEntry&gt; (Name, ColorId, CatEntryId, MastersSizeId,
    // IsBuyable, BackSoon) JSON'u döner. Başarısız olursa (timeout, Akamai challenge sayfası, veri bulunamaması)
    // null döner.
    Task<string?> FetchProductDataJsonAsync(string productUrl, CancellationToken cancellationToken);
}
