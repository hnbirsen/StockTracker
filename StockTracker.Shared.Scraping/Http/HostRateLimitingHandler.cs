using System.Collections.Concurrent;

namespace StockTracker.Shared.Scraping.Http;

// Host başına token-bucket rate limiting — ScraperEtiquetteHandler'daki istekler-arası rastgele gecikme
// tek başına yeterli değil (bkz. .claude/ROADMAP.md Faz 2.6): o sadece art arda İKİ isteği yavaşlatıyor,
// dakika/saat başına toplam istek BÜTÇESİ tanımlamıyor. Bu handler, aynı process içindeki TÜM istekleri
// (birden fazla eşzamanlı stok kontrolü olsa bile) hedef host başına bir üst sınıra tabi tutar.
public class HostRateLimitingHandler : DelegatingHandler
{
    private static readonly ConcurrentDictionary<string, TokenBucket> BucketsByHost = new();

    private readonly int _requestsPerMinute;

    public HostRateLimitingHandler(int requestsPerMinute = 60)
    {
        _requestsPerMinute = requestsPerMinute;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? "unknown";
        var bucket = BucketsByHost.GetOrAdd(host, _ => new TokenBucket(_requestsPerMinute));

        await bucket.WaitForTokenAsync(cancellationToken);

        return await base.SendAsync(request, cancellationToken);
    }

    // Test/tanılama amaçlı: process boyunca biriken bucket'ları sıfırlar.
    internal static void ResetForTests() => BucketsByHost.Clear();

    private sealed class TokenBucket
    {
        private readonly int _capacity;
        private readonly TimeSpan _refillInterval;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private double _tokens;
        private DateTime _lastRefillUtc;

        public TokenBucket(int requestsPerMinute)
        {
            _capacity = Math.Max(1, requestsPerMinute);
            _refillInterval = TimeSpan.FromMinutes(1) / _capacity;
            _tokens = _capacity;
            _lastRefillUtc = DateTime.UtcNow;
        }

        public async Task WaitForTokenAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                TimeSpan waitFor;
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    Refill();
                    if (_tokens >= 1)
                    {
                        _tokens -= 1;
                        return;
                    }

                    waitFor = _refillInterval;
                }
                finally
                {
                    _lock.Release();
                }

                await Task.Delay(waitFor, cancellationToken);
            }
        }

        private void Refill()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRefillUtc;
            var tokensToAdd = elapsed.TotalMilliseconds / _refillInterval.TotalMilliseconds;
            if (tokensToAdd <= 0) return;

            _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
            _lastRefillUtc = now;
        }
    }
}
