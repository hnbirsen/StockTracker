namespace StockTracker.Shared.Scraping.Http;

// Marka scraper'ları arasında paylaşılan "nezaket" katmanı (Faz 2.6'da StockTracker.Shared.Scraping'e
// taşındı — eskiden yalnızca Bershka Scraper'da vardı). Hedef siteye giden her isteğe gerçek bir tarayıcının
// BİRLİKTE göndereceği tutarlı bir header profili (UA + Accept-Language + sec-ch-ua*, bkz. BrowserProfile)
// atar ve istekler arasına küçük bir gecikme koyar — tek bir sabit imza/sabit hız botlarca kolayca
// tespit edilir (bkz. .claude/SECURITY.md).
public class ScraperEtiquetteHandler : DelegatingHandler
{
    private readonly TimeSpan _minDelay;
    private readonly TimeSpan _maxDelay;

    public ScraperEtiquetteHandler(TimeSpan? minDelay = null, TimeSpan? maxDelay = null)
    {
        _minDelay = minDelay ?? TimeSpan.FromMilliseconds(300);
        _maxDelay = maxDelay ?? TimeSpan.FromMilliseconds(1200);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var delayMs = Random.Shared.Next((int)_minDelay.TotalMilliseconds, (int)_maxDelay.TotalMilliseconds);
        await Task.Delay(delayMs, cancellationToken);

        ApplyBrowserProfile(request, BrowserProfiles.Random());

        return await base.SendAsync(request, cancellationToken);
    }

    private static void ApplyBrowserProfile(HttpRequestMessage request, BrowserProfile profile)
    {
        request.Headers.Remove("User-Agent");
        request.Headers.TryAddWithoutValidation("User-Agent", profile.UserAgent);

        request.Headers.Remove("Accept-Language");
        request.Headers.TryAddWithoutValidation("Accept-Language", profile.AcceptLanguage);

        // Yalnızca Chromium tabanlı profillerde dolu (bkz. BrowserProfiles notu) — Firefox/Safari UA'sıyla
        // sec-ch-ua göndermek, motor/tarayıcı tutarsızlığı yüzünden başlı başına bir bot sinyali olurdu.
        request.Headers.Remove("sec-ch-ua");
        request.Headers.Remove("sec-ch-ua-platform");
        request.Headers.Remove("sec-ch-ua-mobile");
        if (profile.SecChUa is not null)
        {
            request.Headers.TryAddWithoutValidation("sec-ch-ua", profile.SecChUa);
            request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", profile.SecChUaPlatform);
            request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", profile.SecChUaMobile);
        }
    }
}
