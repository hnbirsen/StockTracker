using MassTransit;
using StackExchange.Redis;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;
using StockTracker.Shared.Scraping.Health;
using StockTracker.ZaraScraper.Consumers;
using StockTracker.ZaraScraper.Services;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis connection string bulunamadı.");

var redisOptions = ConfigurationOptions.Parse(redisConnection);
redisOptions.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisOptions));

// Faz 2.5 — scraper sağlık/izlenebilirlik katmanı, marka scraper'ları arasında paylaşılan
// (bkz. StockTracker.BershkaScraper/Program.cs üstündeki aynı yorum — burada da birebir aynı gerekçe).
builder.Services.AddSingleton<IScraperHealthLogService, ScraperHealthLogService>();

// Zara'da Bershka'nın aksine ayrı, Akamai'siz bir stok API'si YOK — hem online hem mağaza stoğu tek bir
// Playwright oturumu üzerinden okunuyor (bkz. IZaraPdpFetcher üstündeki not). Bu yüzden burada Bershka'daki
// gibi ayrı bir HttpClient + ScraperEtiquetteHandler/HostRateLimitingHandler/AddScraperResilience pipeline'ı
// KURULMUYOR — hız sınırlaması PlaywrightZaraFetcher'ın kendi içinde (mağaza sorguları arası minimum
// bekleme + tekil eşzamanlılık) uygulanıyor, çünkü bunlar HttpClient üzerinden değil page.EvaluateAsync
// üzerinden atılan isteklerdir.
builder.Services.AddSingleton<IZaraPdpFetcher, PlaywrightZaraFetcher>();

builder.Services.AddScoped<IZaraStockApiClient, ZaraStockApiClient>();
builder.Services.AddScoped<IZaraStockCheckService, ZaraStockCheckService>();

builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<CheckStockCommandConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(QueueNaming.StockCheckQueue("zara"), e =>
        {
            e.ConfigureConsumer<CheckStockCommandConsumer>(context);
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("OK"));

// Son N denemedeki başarı oranı + HTTP durum kodu dağılımı (bkz. .claude/ROADMAP.md Faz 2.5, Bershka ile
// aynı endpoint şekli).
app.MapGet("/health/scraper-stats", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var stats = await healthLog.GetStatsAsync("zara", lastN ?? 100);
    return Results.Ok(stats);
});

app.MapGet("/health/scraper-failures", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var failures = await healthLog.GetRecentFailuresAsync("zara", lastN ?? 20);
    return Results.Ok(failures);
});

app.Run("http://0.0.0.0:5010");
