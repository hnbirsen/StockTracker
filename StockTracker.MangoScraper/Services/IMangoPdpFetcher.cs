namespace StockTracker.MangoScraper.Services;

// Mango'nun ürün sayfaları (PDP) — Bershka/Zara'nın aksine — HİÇBİR bot korumasının (Akamai vb.) arkasında
// DEĞİL: gerçekçi bir User-Agent + Accept-Language ile düz bir `curl`/HttpClient isteği bile tam SSR HTML'i
// döndürüyor (Faz 6.1'de canlı doğrulandı — varsayılan `curl` 403 alıyor, ama bu yalnızca eksik header'lardan
// kaynaklanıyor, JS çalıştırma/gerçek tarayıcı gerektiren bir zorunluluk değil). Bu yüzden Bershka/Zara'nın
// aksine burada Playwright/gerçek Chrome YOK — düz, dayanıklılık politikalı bir `HttpClient` yeterli.
//
// PDP verisi Next.js App Router'ın React Server Components ("RSC") akışıyla geliyor: `window.__NEXT_DATA__`
// gibi klasik bir global YOK, bunun yerine `self.__next_f.push([N, "büyük, çift-escape'lenmiş JSON string"])`
// şeklinde `<script>` etiketlerine gömülü parçalar var. Ürün verisini taşıyan parçanın içinde
// `"colors":[{"id":"77","label":"Şarap Rengi",...,"sizes":[{"id":"19","label":"XS",...,"available":true,
// "warehouses":[...]}]}]` şeklinde, HER BEDEN İÇİN doğrudan bir `available` boolean alanı var — Bershka'nın
// hesaplanmış partnumber'larına ya da Zara'nın "in_stock"/"low_on_stock" gibi string enum'larına benzer bir
// yorumlamaya gerek yok, doğrudan true/false.
public interface IMangoPdpFetcher
{
    // Dönen JSON, List&lt;SizeEntry&gt; (Name, Available, ColorId) şeklinde ayrıştırılabilir bir dizi.
    // Başarısız olursa (ağ hatası, sayfa değişmiş, veri bulunamadı) null döner.
    Task<string?> FetchProductDataJsonAsync(string productUrl, CancellationToken cancellationToken);
}
