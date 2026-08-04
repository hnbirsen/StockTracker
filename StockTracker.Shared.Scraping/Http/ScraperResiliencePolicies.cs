using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace StockTracker.Shared.Scraping.Http;

// Marka scraper'ları arasında paylaşılan dayanıklılık politikaları (Faz 2.6). Eskiden her scraper kendi
// `AddTransientHttpErrorPolicy` zincirini yazıyordu (yalnızca 5xx/408/network hatalarını kapsıyordu,
// 429'u KAPSAMIYORDU — Polly.Extensions.Http'nin `HandleTransientHttpError()` predicate'i 429'u dahil
// etmez). Burada iki ayrı, bilinçli olarak FARKLI davranan katman var:
//   1. Retry-After'a uyan 429/5xx retry politikası.
//   2. Bot-tespiti sinyali olan 403'e özel, normal 5xx devre kesicisinden daha AGRESİF (daha az hata
//      toleransı, daha uzun bekleme) ayrı bir circuit breaker — 403'ün "sunucu geçici arızalı" değil
//      "bizi bot olarak işaretledi" anlamına gelme ihtimali yüksek olduğu için, aynı hızda tekrar
//      denemek durumu daha da kötüleştirebilir (bkz. .claude/ARCHITECTURE.md > Ölçeklenme Riski).
public static class ScraperResiliencePolicies
{
    public static IHttpClientBuilder AddScraperResilience(
        this IHttpClientBuilder builder,
        int retryCount = 3,
        int transientCircuitBreakerThreshold = 5,
        TimeSpan? transientCircuitBreakerDuration = null,
        int botDetectionCircuitBreakerThreshold = 2,
        TimeSpan? botDetectionCircuitBreakerDuration = null) => builder
        .AddPolicyHandler(RetryWithRetryAfterAwareness(retryCount))
        .AddPolicyHandler(TransientErrorCircuitBreaker(
            transientCircuitBreakerThreshold, transientCircuitBreakerDuration ?? TimeSpan.FromSeconds(30)))
        .AddPolicyHandler(BotDetectionCircuitBreaker(
            botDetectionCircuitBreakerThreshold, botDetectionCircuitBreakerDuration ?? TimeSpan.FromMinutes(2)));

    // 5xx/408/network hatalarına ek olarak 429'u da kapsar. Sunucu `Retry-After` header'ı verdiyse (saniye
    // veya HTTP-date formatında) ona uyulur — vermediyse exponential backoff'a düşülür.
    public static IAsyncPolicy<HttpResponseMessage> RetryWithRetryAfterAwareness(int retryCount = 3) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(retryCount, (retryAttempt, outcome, _) => ComputeRetryDelay(retryAttempt, outcome.Result),
                onRetryAsync: (_, _, _, _) => Task.CompletedTask);

    // Ayrı bir metoda çıkarıldı ki gerçek zamanlı bekleme yapmadan (yavaş/kırılgan testlerden kaçınarak)
    // birim testle doğrulanabilsin.
    public static TimeSpan ComputeRetryDelay(int retryAttempt, HttpResponseMessage? response) =>
        response?.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));

    public static IAsyncPolicy<HttpResponseMessage> TransientErrorCircuitBreaker(int threshold, TimeSpan duration) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(threshold, duration);

    public static IAsyncPolicy<HttpResponseMessage> BotDetectionCircuitBreaker(int threshold, TimeSpan duration) =>
        Policy<HttpResponseMessage>
            .HandleResult(response => response.StatusCode == HttpStatusCode.Forbidden)
            .CircuitBreakerAsync(threshold, duration);
}
