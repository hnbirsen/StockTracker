namespace StockTracker.HmScraper.Services;

// H&M ürün sayfaları (PDP) VE mağaza stok API'si ikisi de Akamai Bot Manager'ın arkasında — canlı
// doğrulandı: `curl` gerçekçi User-Agent'la bile ikisinde de Zara'yla BİREBİR AYNI "Access Denied" (403)
// sayfasını dönüyor. Bu yüzden Zara'daki gibi Playwright (gerçek Chrome kanalı) gerekiyor; Mango'nun
// aksine düz HttpClient KULLANILAMIYOR.
//
// PDP verisi klasik Next.js Pages Router `__NEXT_DATA__` global'inde (Mango'nun App Router RSC akışından
// çok daha basit) — `props.pageProps.productPageProps.ssrAvailability` içinde HER BEDEN için doğrudan
// stok bilgisi var: `availability` (stokta olan tam 13 haneli SKU'ların dizisi) ve `fewPieceLeft` (bunun
// alt kümesi — "az kaldı" uyarısı, canlı doğrulandı). Beden adı/kodu eşlemesi ise
// `aemData.productArticleDetails.variations[articleCode].sizes[]` içinde (`sizeCode`=SKU'nun son 3
// hanesi, `size`=3 haneli beden kodu, `name`="XS"/"S"/...).
public interface IHmPdpFetcher
{
    // Dönen JSON, List&lt;SizeEntry&gt; (Name, SizeCode, Available, FewPieceLeft) şeklinde ayrıştırılabilir
    // bir dizi. Başarısız olursa (Akamai/Playwright hatası, veri bulunamadı) null döner.
    Task<string?> FetchProductDataJsonAsync(string productUrl, CancellationToken cancellationToken);

    // Mağaza stok endpoint'ini (`/tr_tr/sis/tr/{productId}/{artId}?latitude=...&longitude=...`), productUrl'e
    // yapılan bir navigasyonla kurulan (Akamai'yi geçmiş) oturumun çerezleriyle, sayfa içinden çağırır.
    // Zara'nın aksine bu endpoint belirli bir mağaza ID'siyle değil enlem/boylam ile "yakındaki mağazalar"
    // sorgusu yapıyor (Mango'yla aynı model) — dönen ham JSON'da TÜM yakın mağazalar (stoksuz olanlar dahil,
    // seyrek/sparse DEĞİL) `traffLightInd` (R/Y/G) ile birlikte yer alıyor. Ham JSON metnini döner ya da
    // başarısızlıkta null.
    Task<string?> FetchStoreAvailabilityJsonAsync(string productUrl, string productId, string artId, double latitude, double longitude, CancellationToken cancellationToken);
}
