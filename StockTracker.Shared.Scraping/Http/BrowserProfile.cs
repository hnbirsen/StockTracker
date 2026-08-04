namespace StockTracker.Shared.Scraping.Http;

// Gerçek bir tarayıcının BİRLİKTE göndereceği header seti — User-Agent tek başına rotasyonlanırsa,
// UA "Chrome" derken sec-ch-ua Client Hint header'larının hiç gelmemesi (ya da Firefox/Safari UA'sıyla
// Chromium'a özgü sec-ch-ua göndermek) motor/tarayıcı tutarsızlığı yüzünden başlı başına bir bot sinyali
// olur (bkz. PlaywrightPdpFetcher üstündeki benzer not — UA/motor uyuşmazlığı). Bu yüzden UA ile
// SecChUa* header'ları birlikte, tek bir "profil" olarak seçilip rotasyonlanır.
public record BrowserProfile(
    string UserAgent,
    string AcceptLanguage,
    string? SecChUa,
    string? SecChUaPlatform,
    string? SecChUaMobile);

public static class BrowserProfiles
{
    private const string TurkishAcceptLanguage = "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7";

    // Yalnızca Chromium tabanlı tarayıcılar sec-ch-ua* Client Hint header'larını gönderir — Firefox ve
    // Safari göndermez. Bu yüzden o profillerde bu alanlar bilinçli olarak null.
    public static readonly IReadOnlyList<BrowserProfile> All =
    [
        new(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            TurkishAcceptLanguage,
            "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"",
            "\"Windows\"", "?0"),
        new(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            TurkishAcceptLanguage,
            "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"",
            "\"Linux\"", "?0"),
        new(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
            TurkishAcceptLanguage,
            null, null, null),
        new(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0",
            TurkishAcceptLanguage,
            null, null, null),
    ];

    public static BrowserProfile Random() => All[System.Random.Shared.Next(All.Count)];
}
