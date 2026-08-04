using MassTransit;
using StackExchange.Redis;
using StockTracker.BershkaScraper.Consumers;
using StockTracker.BershkaScraper.Services;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;
using StockTracker.Shared.Scraping.Health;
using StockTracker.Shared.Scraping.Http;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

// Stok API'si (api.inditex.com) — bkz. BershkaStockApiClient üstündeki açıklama. Ürün sayfaları
// (www.bershka.com) artık düz HttpClient ile değil, Playwright ile çekiliyor (bkz. IBershkaPdpFetcher) —
// Akamai Bot Manager JS interstitial'ı düz HTTP isteklerini geçirmiyor.
var stockApiBaseUrl = Environment.GetEnvironmentVariable("BERSHKA_STOCK_API_BASE_URL")
    ?? builder.Configuration["Bershka:StockApiBaseUrl"]
    ?? throw new InvalidOperationException("Bershka:StockApiBaseUrl bulunamadı.");

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis connection string bulunamadı.");

var redisOptions = ConfigurationOptions.Parse(redisConnection);
redisOptions.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisOptions));

// Faz 2.5 — scraper sağlık/izlenebilirlik katmanı. Marka scraper'ları arasında paylaşılan bir kütüphane
// (StockTracker.Shared.Scraping): her deneme (Playwright PDP çekimi, stok API çağrısı) zaten paylaşılan
// Redis'te scraper adına göre namespace'lenmiş bir capped-list'e loglanır — bkz. IScraperHealthLogService
// üstündeki not (neden ayrı bir Postgres DB yerine Redis tercih edildi).
builder.Services.AddSingleton<IScraperHealthLogService, ScraperHealthLogService>();

// Playwright/Chromium process'i pahalı olduğu için process başına bir kez başlatılıp yeniden kullanılır —
// bkz. PlaywrightPdpFetcher üstündeki açıklama. Sonuçlar Redis'te önbelleğe alındığı için (BershkaStockApiClient)
// bu zaten çoğu istekte hiç tetiklenmiyor.
builder.Services.AddSingleton<IBershkaPdpFetcher, PlaywrightPdpFetcher>();

builder.Services.AddTransient<ScraperEtiquetteHandler>();
builder.Services.AddTransient(_ => new HostRateLimitingHandler(requestsPerMinute: 60));

// Bershka'ya giden HTTP client aynı etiket/dayanıklılık/hız-sınırlama politikalarından geçer (Faz 2.6,
// paylaşılan StockTracker.Shared.Scraping kütüphanesi — bkz. ScraperEtiquetteHandler, HostRateLimitingHandler,
// ScraperResiliencePolicies üstündeki notlar ve .claude/SECURITY.md):
//   1. HostRateLimitingHandler — host başına dakikada en fazla 60 istek (token bucket).
//   2. ScraperEtiquetteHandler — gerçekçi, tutarlı header profili + istekler arası rastgele gecikme.
//   3. AddScraperResilience — 429 (Retry-After'a uyarak) + 5xx retry; ayrıca 5xx için normal, 403
//      (bot-tespiti sinyali) için daha agresif ayrı birer circuit breaker.
IHttpClientBuilder ApplyResiliencePolicies(IHttpClientBuilder httpClientBuilder) => httpClientBuilder
    .AddHttpMessageHandler<HostRateLimitingHandler>()
    .AddHttpMessageHandler<ScraperEtiquetteHandler>()
    .AddScraperResilience();

ApplyResiliencePolicies(builder.Services.AddHttpClient<IBershkaStockApiClient, BershkaStockApiClient>(client =>
{
    client.BaseAddress = new Uri(stockApiBaseUrl);
}));

builder.Services.AddScoped<IBershkaStockCheckService, BershkaStockCheckService>();

builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<CheckStockCommandConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(QueueNaming.StockCheckQueue("bershka"), e =>
        {
            e.ConfigureConsumer<CheckStockCommandConsumer>(context);
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("OK"));

// Son N denemedeki başarı oranı + HTTP durum kodu dağılımı (bkz. .claude/ROADMAP.md Faz 2.5).
// `alertTriggered`, örneklem yeterince büyükken (>=10) başarı oranı eşiğin (%70) altına düştüğünde true olur
// — ayrıca bu durumda ScraperHealthLogService zaten bir Warning logu basıyor.
app.MapGet("/health/scraper-stats", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var stats = await healthLog.GetStatsAsync("bershka", lastN ?? 100);
    return Results.Ok(stats);
});

// Son N başarısız denemeyi, hangi ürün URL'i/mağaza/partnumber üzerinde olduğuyla birlikte döner —
// "hangi üründe hata alındı" sorusuna Redis'e elle bakmadan cevap vermek için (bkz. .claude/ROADMAP.md Faz 2.5).
app.MapGet("/health/scraper-failures", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var failures = await healthLog.GetRecentFailuresAsync("bershka", lastN ?? 20);
    return Results.Ok(failures);
});

app.Run("http://0.0.0.0:5009");
