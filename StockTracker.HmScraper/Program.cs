using MassTransit;
using StackExchange.Redis;
using StockTracker.HmScraper.Consumers;
using StockTracker.HmScraper.Services;
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

builder.Services.AddSingleton<IScraperHealthLogService, ScraperHealthLogService>();

// H&M, Zara gibi Akamai korumalı (PDP + mağaza stok API'si ikisi de) — bu yüzden Mango'nun aksine
// Playwright/gerçek Chrome kanalı gerekiyor (bkz. .claude/ARCHITECTURE.md > H&M Scraper).
builder.Services.AddSingleton<IHmPdpFetcher, PlaywrightHmFetcher>();

builder.Services.AddScoped<IHmStockApiClient, HmStockApiClient>();
builder.Services.AddScoped<IHmStockCheckService, HmStockCheckService>();

builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<CheckStockCommandConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(QueueNaming.StockCheckQueue("hm"), e =>
        {
            e.ConfigureConsumer<CheckStockCommandConsumer>(context);
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("OK"));

app.MapGet("/health/scraper-stats", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var stats = await healthLog.GetStatsAsync("hm", lastN ?? 100);
    return Results.Ok(stats);
});

app.MapGet("/health/scraper-failures", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var failures = await healthLog.GetRecentFailuresAsync("hm", lastN ?? 20);
    return Results.Ok(failures);
});

app.Run("http://0.0.0.0:5012");
