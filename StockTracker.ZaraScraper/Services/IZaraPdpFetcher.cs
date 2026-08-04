namespace StockTracker.ZaraScraper.Services;

// Zara ürün sayfaları (PDP) da Bershka gibi Akamai Bot Manager'ın arkasında — düz bir HttpClient (curl ile
// canlı doğrulandı: gerçekçi UA ile bile 403) hiçbir Zara endpoint'ini (ne PDP HTML'i ne de
// store-product-availability AJAX endpoint'i) geçemiyor. Gerçek Chrome kanalı üzerinden Playwright gerekiyor
// (bkz. PlaywrightZaraFetcher üstündeki not).
//
// Bershka'dan FARKI: Zara'nın PDP verisi bir Vue component ağacında değil, sunucu tarafında render edilen
// `window.zara.viewPayload` global değişkeninde SSR olarak gömülü (`product.detail.colors[].sizes[]`,
// her bedende doğrudan bir `availability` alanı) — component ağacı taramaya gerek yok, tek bir
// `page.EvaluateAsync` ile okunabiliyor.
//
// Mağaza bazlı stok için Bershka'nın aksine (ayrı, Akamai'siz api.inditex.com stok API'si) Zara'nın
// store-product-availability endpoint'i AYNI www.zara.com domain'inde ve AYNI ŞEKİLDE Akamai korumalı —
// bu yüzden düz HttpClient ile çağrılamıyor, PDP sayfasına yapılan Playwright navigasyonuyla kurulan
// (Akamai'yi geçmiş) tarayıcı oturumunun çerezleriyle, sayfa içinden (`page.EvaluateAsync` + `fetch`) çağrılması
// gerekiyor. Ayrıca CANLI VERİYLE DOĞRULANAN kritik bulgu: bu endpoint hıza dayalı (velocity-based) bir
// Akamai bloklamasına sahip — birkaç saniye içinde art arda birden fazla istek atıldığında (halihazırda
// çalışan bir sorgu bile dahil) TÜM oturum 403'e düşüyor. Bu yüzden PlaywrightZaraFetcher, mağaza sorguları
// arasına kasıtlı bir minimum bekleme süresi + tekil eşzamanlılık (semaphore) uyguluyor.
public interface IZaraPdpFetcher
{
    // Dönen JSON, List&lt;SizeEntry&gt; (Name, Availability, ColorId, Sku) şeklinde ayrıştırılabilir bir dizi.
    // Başarısız olursa (timeout, sayfa engellenmesi, veri bulunamaması) null döner.
    Task<string?> FetchProductDataJsonAsync(string productUrl, CancellationToken cancellationToken);

    // store-product-availability AJAX endpoint'ini, productUrl'e yapılan bir navigasyonla kurulan (Akamai'yi
    // geçmiş) oturumun çerezleriyle, sayfa içinden çağırır. productId = ürün URL'indeki "v1" sorgu parametresi
    // (ürünün kendi `product.id` alanından FARKLI — bkz. ZaraStockApiClient üstündeki not, gerçek veriyle
    // doğrulandı). Ham JSON metnini döner (ör. {"productId":...,"sizesAvailableAndLocationsByPhysicalStores":[...]})
    // ya da başarısızlıkta null.
    Task<string?> FetchStoreAvailabilityJsonAsync(string productUrl, string productId, string physicalStoreId, CancellationToken cancellationToken);
}
