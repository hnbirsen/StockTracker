namespace StockTracker.OyshoScraper.Services;

// Oysho ürün sayfaları (PDP) Akamai Bot Manager'ın arkasında — canlı doğrulandı: düz `curl` (gerçekçi
// UA'yla bile) gerçek içerik yerine bir `bm-verify` sorgu parametresiyle JS-yönlendirme (challenge) sayfası
// döndürüyor (Zara/Massimo Dutti/Pull&Bear ile birebir aynı format). Gerçek Chrome kanalı şart.
//
// Bershka'nın Vue component ağacının aksine, Oysho SUNUCU TARAFINDA render edilmiş bir Angular
// uygulaması — tüm ürün verisi (renkler/bedenler/partnumber/stok) doğrudan `#oyshoServer-state` script
// etiketinin içinde geçerli JSON olarak geliyor (`PRODUCT_HYDRATION_KEY.product`). Bershka'daki gibi
// hydration/component-ağacı taraması GEREKMİYOR — sayfa yüklenip script etiketi okunması yeterli.
//
// Bu arayüz, testlerin gerçek bir Chromium'a ihtiyaç duymadan OyshoStockApiClient'ı test edebilmesi
// için ayrı tutuldu.
public interface IOyshoPdpFetcher
{
    // Dönen JSON, List&lt;SizeEntry&gt; (Name, Availability, HasFewUnits, PartNumber, MasterSizeId, ColorId)
    // şeklinde ayrıştırılabilir bir dizi — OyshoStockApiClient'ın Redis'te önbelleğe aldığı formatla
    // birebir aynı. Başarısız olursa (timeout, sayfa engellenmesi, veri bulunamaması) null döner —
    // çağıran taraf bunu Unknown'a çevirir.
    Task<string?> FetchProductSizesJsonAsync(string productUrl, CancellationToken cancellationToken);
}
