namespace StockTracker.MaviScraper.Services;

// Mavi ürün sayfaları (PDP) VE mağaza stok API'si Cloudflare'in arkasında — canlı doğrulandı: düz `curl`
// (gerçekçi UA'yla bile) gerçek içerik yerine bir "Attention Required! | Cloudflare" JS-yönlendirme
// (challenge) sayfası döndürüyor. Gerçek Chrome kanalı şart.
//
// Mavi, SAP Hybris (Accelerator) tabanlı bir platform — diğer hiçbir markayla (hepsi Inditex/H&M) alt-yapı
// paylaşmıyor. Online stok verisi Next.js/Nuxt gibi bir state script'inde DEĞİL, PDP'nin SSR HTML'ine
// gömülü düz bir global JS değişkeninde (`sizeVariantJson`) geliyor — her eleman `{id (barkod), size,
// length, stockLevel, stockLevelStatus}` şeklinde. `id` alanı hem online stok kontrolü için gerçek stok
// adedini (`stockLevel`) taşıyor HEM DE mağaza stok API'sinin (`FetchStoreAvailabilityJsonAsync`) tek
// parametresi olan gerçek barkodun ta kendisi — ayrı bir "barkod çözme" adımına gerek yok.
public interface IMaviPdpFetcher
{
    // Dönen JSON, List&lt;SizeEntry&gt; (Size, Length, Barcode, StockLevel, StockLevelStatus) şeklinde
    // ayrıştırılabilir bir dizi. Başarısız olursa (timeout, sayfa engellenmesi, veri bulunamaması) null döner.
    Task<string?> FetchProductSizesJsonAsync(string productUrl, CancellationToken cancellationToken);

    // `/magazalar/get-stores-by-location` endpoint'ini, productUrl'e yapılan bir navigasyonla kurulan
    // (Cloudflare'i geçmiş) oturumun çerezleriyle, sayfa içinden (`page.EvaluateAsync` + `fetch`) çağırır —
    // AYNI domain'de olduğu için düz bir HttpClient ile çağrılamıyor (canlı doğrulandı: 403). Ham JSON
    // metnini döner (ör. {"allStoreData":[{"pagination":{...},"results":[{"storeId":...}]}]}) ya da
    // başarısızlıkta null.
    Task<string?> FetchStoreAvailabilityJsonAsync(string productUrl, string barcode, double latitude, double longitude, CancellationToken cancellationToken);
}
