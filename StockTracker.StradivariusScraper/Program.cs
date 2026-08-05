using MassTransit;
using StackExchange.Redis;
using StockTracker.StradivariusScraper.Consumers;
using StockTracker.StradivariusScraper.Services;
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

// Online stok için Playwright/gerçek Chrome kanalı ŞART (bkz. IStradivariusPdpFetcher üstündeki not — ürün
// sayfası Akamai Bot Manager'ın arkasında). Chrome process'i pahalı olduğu için tekil (singleton) kayıtlı.
builder.Services.AddSingleton<IStradivariusPdpFetcher, PlaywrightStradivariusFetcher>();

// Mağaza stok API'si (`api/storefront/1/stores/.../skus-availability-in-stores/actions/filter`) AYNI
// domain'de olmasına rağmen korumasız (kullanıcının paylaştığı gerçek curl istekleriyle doğrulandı — bkz.
// StradivariusStockApiClient üstündeki not) — Massimo Dutti/Pull&Bear'daki gibi düz, dayanıklılık
// politikalı bir HttpClient ile çağrılıyor.
builder.Services.AddTransient<ScraperEtiquetteHandler>();
builder.Services.AddTransient(_ => new HostRateLimitingHandler(requestsPerMinute: 60));

IHttpClientBuilder ApplyResiliencePolicies(IHttpClientBuilder httpClientBuilder) => httpClientBuilder
    .AddHttpMessageHandler<HostRateLimitingHandler>()
    .AddHttpMessageHandler<ScraperEtiquetteHandler>()
    .AddScraperResilience();

ApplyResiliencePolicies(builder.Services.AddHttpClient<IStradivariusStockApiClient, StradivariusStockApiClient>(client =>
{
    client.BaseAddress = new Uri("https://www.stradivarius.com");
}));

builder.Services.AddScoped<IStradivariusStockCheckService, StradivariusStockCheckService>();

builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<CheckStockCommandConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(QueueNaming.StockCheckQueue("stradivarius"), e =>
        {
            e.ConfigureConsumer<CheckStockCommandConsumer>(context);
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("OK"));

app.MapGet("/health/scraper-stats", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var stats = await healthLog.GetStatsAsync("stradivarius", lastN ?? 100);
    return Results.Ok(stats);
});

app.MapGet("/health/scraper-failures", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var failures = await healthLog.GetRecentFailuresAsync("stradivarius", lastN ?? 20);
    return Results.Ok(failures);
});

app.Run("http://0.0.0.0:5016");
