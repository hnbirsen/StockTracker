using MassTransit;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;
using StockTracker.BershkaScraper.Consumers;
using StockTracker.BershkaScraper.Http;
using StockTracker.BershkaScraper.Services;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;
using StockTracker.Shared.Scraping.Health;

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

// Bershka'ya giden HTTP client aynı etiket/dayanıklılık politikalarından geçer — bkz. ScraperEtiquetteHandler
// ve .claude/SECURITY.md.
IHttpClientBuilder ApplyResiliencePolicies(IHttpClientBuilder httpClientBuilder) => httpClientBuilder
    .AddHttpMessageHandler<ScraperEtiquetteHandler>()
    // Geçici (transient) HTTP hatalarında (5xx, timeout, network) 3 kez exponential backoff ile tekrar dener.
    .AddTransientHttpErrorPolicy(policy => policy.WaitAndRetryAsync(
        3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))
    // Art arda 5 hata sonrası devreyi 30 saniye açar — Bershka geçici olarak engellediğinde bu servis
    // yormadan bekler (her marka ayrı servis/kuyruk olduğu için diğer markaları etkilemez).
    .AddTransientHttpErrorPolicy(policy => policy.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

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
