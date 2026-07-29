namespace StockTracker.BershkaScraper.Http;

// Bershka'ya giden her isteğe rastgele bir User-Agent atar ve istekler arasına küçük bir gecikme
// koyar — tek bir sabit imza/sabit hız botlarca kolayca tespit edilir (bkz. .claude/SECURITY.md).
public class ScraperEtiquetteHandler : DelegatingHandler
{
    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0"
    ];

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

        request.Headers.Remove("User-Agent");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgents[Random.Shared.Next(UserAgents.Length)]);

        return await base.SendAsync(request, cancellationToken);
    }
}
