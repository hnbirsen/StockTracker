using MassTransit;
using StackExchange.Redis;
using StockTracker.MangoScraper.Consumers;
using StockTracker.MangoScraper.Services;
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

builder.Services.AddSingleton<IScraperHealthLogService, ScraperHealthLogService>();

// Mango'da Bershka/Zara'nın aksine PLAYWRIGHT YOK — ne PDP ne de mağaza stok API'si bot korumalı
// (canlı doğrulandı, bkz. .claude/ARCHITECTURE.md > Mango Scraper), bu yüzden ikisi de düz, dayanıklılık
// politikalı `HttpClient` ile okunuyor. Aynı paylaşılan `StockTracker.Shared.Scraping` zinciri (host bazlı
// rate limiting, gerçekçi header profili, Retry-After'a uyan retry + bot-tespiti circuit breaker)
// Bershka'daki gerekçenin aynısıyla kullanılıyor — nezaket/güvenilirlik için, bot tespiti olmasa bile.
IHttpClientBuilder ApplyResiliencePolicies(IHttpClientBuilder httpClientBuilder) => httpClientBuilder
    .AddHttpMessageHandler<HostRateLimitingHandler>()
    .AddHttpMessageHandler<ScraperEtiquetteHandler>()
    .AddScraperResilience();

builder.Services.AddTransient<ScraperEtiquetteHandler>();
builder.Services.AddTransient(_ => new HostRateLimitingHandler(requestsPerMinute: 60));

// PDP fetcher: BaseAddress yok — productUrl her zaman tam, mutlak bir URL (Product Service'ten geliyor).
ApplyResiliencePolicies(builder.Services.AddHttpClient<IMangoPdpFetcher, MangoPdpFetcher>());

// Mağaza stok API'si: sabit host (api.shop.mango.com).
ApplyResiliencePolicies(builder.Services.AddHttpClient<IMangoStockApiClient, MangoStockApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.shop.mango.com");
}));

builder.Services.AddScoped<IMangoStockCheckService, MangoStockCheckService>();

builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<CheckStockCommandConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(QueueNaming.StockCheckQueue("mango"), e =>
        {
            e.ConfigureConsumer<CheckStockCommandConsumer>(context);
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("OK"));

app.MapGet("/health/scraper-stats", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var stats = await healthLog.GetStatsAsync("mango", lastN ?? 100);
    return Results.Ok(stats);
});

app.MapGet("/health/scraper-failures", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var failures = await healthLog.GetRecentFailuresAsync("mango", lastN ?? 20);
    return Results.Ok(failures);
});

app.Run("http://0.0.0.0:5011");
