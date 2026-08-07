using MassTransit;
using StackExchange.Redis;
using StockTracker.OyshoScraper.Consumers;
using StockTracker.OyshoScraper.Services;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;
using StockTracker.Shared.Scraping.Health;
using StockTracker.Shared.Scraping.Http;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

// Stok API'si (api.inditex.com) — Bershka ile BİREBİR AYNI arka uç, bkz. OyshoStockApiClient üstündeki
// açıklama. Ürün sayfaları (www.oysho.com) düz HttpClient ile değil, Playwright ile çekiliyor
// (bkz. IOyshoPdpFetcher) — Akamai Bot Manager JS interstitial'ı düz HTTP isteklerini geçirmiyor.
var stockApiBaseUrl = Environment.GetEnvironmentVariable("OYSHO_STOCK_API_BASE_URL")
    ?? builder.Configuration["Oysho:StockApiBaseUrl"]
    ?? throw new InvalidOperationException("Oysho:StockApiBaseUrl bulunamadı.");

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis connection string bulunamadı.");

var redisOptions = ConfigurationOptions.Parse(redisConnection);
redisOptions.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisOptions));

// Faz 2.5 — scraper sağlık/izlenebilirlik katmanı (bkz. diğer scraper'lardaki aynı kayıt).
builder.Services.AddSingleton<IScraperHealthLogService, ScraperHealthLogService>();

// Playwright/Chromium process'i pahalı olduğu için process başına bir kez başlatılıp yeniden kullanılır —
// bkz. PlaywrightOyshoFetcher üstündeki açıklama. Sonuçlar Redis'te önbelleğe alındığı için
// (OyshoStockApiClient) bu zaten çoğu istekte hiç tetiklenmiyor.
builder.Services.AddSingleton<IOyshoPdpFetcher, PlaywrightOyshoFetcher>();

builder.Services.AddTransient<ScraperEtiquetteHandler>();
builder.Services.AddTransient(_ => new HostRateLimitingHandler(requestsPerMinute: 60));

// Oysho'ya giden HTTP client aynı etiket/dayanıklılık/hız-sınırlama politikalarından geçer (Faz 2.6,
// paylaşılan StockTracker.Shared.Scraping kütüphanesi — bkz. diğer scraper'lardaki aynı kayıt).
IHttpClientBuilder ApplyResiliencePolicies(IHttpClientBuilder httpClientBuilder) => httpClientBuilder
    .AddHttpMessageHandler<HostRateLimitingHandler>()
    .AddHttpMessageHandler<ScraperEtiquetteHandler>()
    .AddScraperResilience();

ApplyResiliencePolicies(builder.Services.AddHttpClient<IOyshoStockApiClient, OyshoStockApiClient>(client =>
{
    client.BaseAddress = new Uri(stockApiBaseUrl);
}));

builder.Services.AddScoped<IOyshoStockCheckService, OyshoStockCheckService>();

builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<CheckStockCommandConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(QueueNaming.StockCheckQueue("oysho"), e =>
        {
            e.ConfigureConsumer<CheckStockCommandConsumer>(context);
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("OK"));

app.MapGet("/health/scraper-stats", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var stats = await healthLog.GetStatsAsync("oysho", lastN ?? 100);
    return Results.Ok(stats);
});

app.MapGet("/health/scraper-failures", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var failures = await healthLog.GetRecentFailuresAsync("oysho", lastN ?? 20);
    return Results.Ok(failures);
});

app.Run("http://0.0.0.0:5017");
