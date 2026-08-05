namespace StockTracker.StradivariusScraper.Services;

// Stradivarius ürün sayfaları (PDP) diğer Inditex markaları gibi Akamai Bot Manager'ın arkasında (canlı
// doğrulandı: aynı `bm-verify` yönlendirme deseni) — Playwright + gerçek Chrome kanalı şart (bkz.
// PlaywrightStradivariusFetcher).
public interface IStradivariusPdpFetcher
{
    // Online stok: SUNUCU TARAFINDA RENDER EDİLMİŞ (SSR) HTML'den doğrudan okunur — `button[data-testid=
    // "size-item"]` her biri kendi `data-sku`'sunu (`p[data-testid="size-name"][data-sku]`) taşır, `disabled`
    // niteliği stok dışı durumu, buton metnindeki "tükenmek üzere" ifadesi düşük stok uyarısını gösteriyor
    // (canlı doğrulandı). Bu yüzden online kontrol için ek bir API çağrısı GEREKMİYOR — sabit bir
    // DOMContentLoaded beklemesi yeterli.
    //
    // AYNI sayfa ziyaretinde, mağaza stok API'sinin (`IStradivariusStockApiClient`) ihtiyaç duyduğu misafir
    // oturum `access_token` çerezi de okunup dönüyor — bu token, sitenin kendi guest-session akışıyla her
    // yeni tarayıcı bağlamında (context) otomatik olarak verilen, ~24 saat geçerli bir JWT (kullanıcının
    // paylaştığı gerçek ağ trafiğinden doğrulandı: `exp`/`iat` farkı ≈24 saat). Ayrı bir login/kimlik
    // doğrulama akışı GEREKMİYOR.
    //
    // Dönen JSON: {AccessToken, Sizes: List&lt;SizeEntry&gt; (Name, Sku, Disabled, LowStock)}. Başarısız
    // olursa (timeout, Akamai challenge, beden bulunamadı) null döner.
    Task<string?> FetchOnlineSizesJsonAsync(string productUrl, CancellationToken cancellationToken);
}
