namespace StockTracker.BershkaScraper.Services;

// Bershka ürün sayfaları (PDP) Akamai Bot Manager'ın arkasında — düz bir HttpClient bunu asla geçemez
// (Faz 2.4'te doğrulandı). Ayrıca statik HTML'i regex ile taramak da güvenilir değil: sayfayı oluşturan
// bundler bazı ürünlerde tekrar eden string'leri ("in_stock", beden adları, partnumber'lar) paylaşılan
// değişkenlere çıkarıyor (ör. stock:am yerine stock:"in_stock") — gerçek değer statik metinde hiç
// yazılı olmuyor, sadece JS çalıştırıldığında değişken çözümleniyor. Bu, ürüne/sayfaya göre değişen,
// dışarıdan öngörülemeyen bir davranış (gerçek veriyle kanıtlandı — bkz. .claude/ARCHITECTURE.md >
// Bershka Scraper > Playwright).
//
// Bu yüzden PlaywrightPdpFetcher artık statik HTML döndürmüyor — sayfa yüklendikten sonra Vue component
// ağacını (`document.getElementById('__nuxt').__vue__`) tarayıp, verinin tutulduğu component'in `$data`/
// `$props`'undan ZATEN ÇÖZÜMLENMİŞ (gerçek) beden listesini okuyor. Vue'nun reaktif durumu her zaman gerçek
// JS değerini tutar — kaynak kodun minify sırasında string'i değişkene çıkarıp çıkarmaması bunu etkilemez.
//
// Bu arayüz, testlerin gerçek bir Chromium'a ihtiyaç duymadan BershkaStockApiClient'ı test edebilmesi
// için ayrı tutuldu.
public interface IBershkaPdpFetcher
{
    // Dönen JSON, List&lt;SizeEntry&gt; (Name, Stock, PartNumber, MastersSizeId) şeklinde ayrıştırılabilir bir
    // dizi — BershkaStockApiClient'ın Redis'te önbelleğe aldığı formatla birebir aynı. Başarısız olursa
    // (timeout, sayfa engellenmesi, veri bulunamaması) null döner — çağıran taraf bunu Unknown'a çevirir.
    Task<string?> FetchProductSizesJsonAsync(string productUrl, CancellationToken cancellationToken);
}
