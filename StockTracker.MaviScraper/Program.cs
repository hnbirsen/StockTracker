using MassTransit;
using StackExchange.Redis;
using StockTracker.MaviScraper.Consumers;
using StockTracker.MaviScraper.Services;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;
using StockTracker.Shared.Scraping.Health;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis connection string bulunamadı.");

var redisOptions = ConfigurationOptions.Parse(redisConnection);
redisOptions.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisOptions));

// Faz 2.5 — scraper sağlık/izlenebilirlik katmanı, marka scraper'ları arasında paylaşılan.
builder.Services.AddSingleton<IScraperHealthLogService, ScraperHealthLogService>();

// Mavi'de Zara'daki gibi ayrı, korumasız bir stok API'si YOK — hem online hem mağaza stoğu (mağaza sorgusu
// AYNI Cloudflare korumalı domain'de olduğu için) tek bir Playwright oturumu üzerinden okunuyor
// (bkz. IMaviPdpFetcher üstündeki not). Bu yüzden Bershka'daki gibi ayrı bir HttpClient + resilience
// pipeline'ı KURULMUYOR.
builder.Services.AddSingleton<IMaviPdpFetcher, PlaywrightMaviFetcher>();

builder.Services.AddScoped<IMaviStockApiClient, MaviStockApiClient>();
builder.Services.AddScoped<IMaviStockCheckService, MaviStockCheckService>();

builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<CheckStockCommandConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(QueueNaming.StockCheckQueue("mavi"), e =>
        {
            e.ConfigureConsumer<CheckStockCommandConsumer>(context);
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("OK"));

app.MapGet("/health/scraper-stats", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var stats = await healthLog.GetStatsAsync("mavi", lastN ?? 100);
    return Results.Ok(stats);
});

app.MapGet("/health/scraper-failures", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var failures = await healthLog.GetRecentFailuresAsync("mavi", lastN ?? 20);
    return Results.Ok(failures);
});

app.Run("http://0.0.0.0:5018");
