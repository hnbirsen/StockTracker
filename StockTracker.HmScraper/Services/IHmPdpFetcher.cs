namespace StockTracker.HmScraper.Services;

// H&M ürün sayfası (PDP) Akamai Bot Manager'ın arkasında — canlı doğrulandı: `curl` gerçekçi User-Agent'la
// bile Zara'yla BİREBİR AYNI "Access Denied" (403) sayfasını dönüyor, bu yüzden Playwright (gerçek Chrome
// kanalı) gerekiyor.
//
// ⚠️ DÜZELTİLMİŞ MİMARİ (kullanıcının paylaştığı gerçek `curl` istekleriyle bulundu — Stradivarius'taki
// collaborative-debugging emsaliyle aynı yöntem): online stok ARTIK bu fetcher'dan DEĞİL, tamamen AYRI ve
// KORUMASIZ bir domain'den (`ofg.hm.com`) geliyor (bkz. `HmStockApiClient`) — `curl`/`fetch` ile hiçbir
// çerez/oturum olmadan (`credentials: 'omit'`) bile 200 dönüyor, Akamai'nin `www2.hm.com` üzerindeki
// korumasının dışında. Bu fetcher'ın TEK görevi artık PDP'nin `__NEXT_DATA__`'sındaki
// `aemData.productArticleDetails.variations[articleCode].sizes[]` alanından okunan BEDEN ADI ↔ KOD
// eşlemesini (`sizeCode`=SKU'nun son 3 hanesi, `size`=3 haneli beden kodu, `name`="XS"/"32"/...) sağlamak —
// bu eşleme ürün başına neredeyse hiç değişmediği için (ürün sayfasının kalıcı içeriği, stok gibi anlık
// değişmiyor) uzun süreli (24 saat) önbelleğe alınabiliyor (bkz. `HmStockApiClient`), Playwright kullanım
// sıklığını "her stok kontrolünde bir kez" yerine "ürün başına bir kez"e indiriyor.
public interface IHmPdpFetcher
{
    // Dönen JSON, List&lt;SizeEntry&gt; (Name, SizeCode) şeklinde ayrıştırılabilir bir dizi. Başarısız
    // olursa (Akamai/Playwright hatası, veri bulunamadı) null döner.
    Task<string?> FetchProductDataJsonAsync(string productUrl, CancellationToken cancellationToken);

    // Mağaza stok endpoint'ini (`/tr_tr/sis/tr/{productId}/{artId}?latitude=...&longitude=...`), productUrl'e
    // yapılan bir navigasyonla kurulan (Akamai'yi geçmiş) oturumun çerezleriyle, sayfa içinden çağırır. Bu
    // endpoint AYNI `www2.hm.com` domain'inde olduğu için (Akamai'nin ana korumasının kapsamında,
    // `ofg.hm.com`'un AKSİNE) — gerçek bir tarayıcıdan çerezsiz çalıştığı doğrulanmış olsa da (Akamai'nin
    // asıl ayırt ettiği şey oturum/çerez değil istemci TLS/tarayıcı parmak izi), bir .NET `HttpClient`'ın bu
    // parmak izini taklit edemeyeceği varsayılarak temkinli davranılıyor — Playwright ÜZERİNDEN çağrılmaya
    // devam ediyor. Zara'nın aksine bu endpoint belirli bir mağaza ID'siyle değil enlem/boylam ile
    // "yakındaki mağazalar" sorgusu yapıyor (Mango'yla aynı model) — dönen ham JSON'da TÜM yakın mağazalar
    // (stoksuz olanlar dahil, seyrek/sparse DEĞİL) `traffLightInd` (R/Y/G) ile birlikte yer alıyor. Ham JSON
    // metnini döner ya da başarısızlıkta null.
    Task<string?> FetchStoreAvailabilityJsonAsync(string productUrl, string productId, string artId, double latitude, double longitude, CancellationToken cancellationToken);
}
