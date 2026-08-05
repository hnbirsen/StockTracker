using MassTransit;
using StackExchange.Redis;
using StockTracker.BeymenScraper.Consumers;
using StockTracker.BeymenScraper.Services;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;
using StockTracker.Shared.Scraping.Health;
using StockTracker.Shared.Scraping.Http;

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

// Beymen'in ana web sitesi Incapsula korumalı, ama bu scraper'ın kullandığı gerçek stok API'leri
// (`sf-api/api/product/.../productsummary`, `api/store/getstorestock/...`) AYRI ve KORUMASIZ (canlı
// doğrulandı) — Mango'daki gibi Playwright HİÇ YOK, düz dayanıklılık politikalı bir HttpClient yeterli
// (bkz. BeymenApiClient üstündeki not).
builder.Services.AddTransient<ScraperEtiquetteHandler>();
builder.Services.AddTransient(_ => new HostRateLimitingHandler(requestsPerMinute: 60));

IHttpClientBuilder ApplyResiliencePolicies(IHttpClientBuilder httpClientBuilder) => httpClientBuilder
    .AddHttpMessageHandler<HostRateLimitingHandler>()
    .AddHttpMessageHandler<ScraperEtiquetteHandler>()
    .AddScraperResilience();

ApplyResiliencePolicies(builder.Services.AddHttpClient<IBeymenApiClient, BeymenApiClient>(client =>
{
    client.BaseAddress = new Uri("https://www.beymen.com");
}));

builder.Services.AddScoped<IBeymenStockCheckService, BeymenStockCheckService>();

builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<CheckStockCommandConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(QueueNaming.StockCheckQueue("beymen"), e =>
        {
            e.ConfigureConsumer<CheckStockCommandConsumer>(context);
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("OK"));

app.MapGet("/health/scraper-stats", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var stats = await healthLog.GetStatsAsync("beymen", lastN ?? 100);
    return Results.Ok(stats);
});

app.MapGet("/health/scraper-failures", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var failures = await healthLog.GetRecentFailuresAsync("beymen", lastN ?? 20);
    return Results.Ok(failures);
});

app.Run("http://0.0.0.0:5014");
