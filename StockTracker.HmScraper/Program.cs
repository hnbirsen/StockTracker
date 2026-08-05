using MassTransit;
using StackExchange.Redis;
using StockTracker.HmScraper.Consumers;
using StockTracker.HmScraper.Services;
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

// PDP hâlâ Akamai korumalı — beden adı↔kod eşlemesini okumak için Playwright/gerçek Chrome kanalı gerekiyor
// (bkz. .claude/ARCHITECTURE.md > H&M Scraper). Bu artık yalnızca ürün başına ~günde bir kez çalışıyor
// (24 saatlik Redis cache) — online stok kontrolünün kendisi Playwright GEREKTİRMİYOR (bkz. altta).
builder.Services.AddSingleton<IHmPdpFetcher, PlaywrightHmFetcher>();

// Online stok API'si (`ofg.hm.com/pdh-availability/...`) AYRI bir domain'de ve korumasız (kullanıcının
// paylaştığı gerçek curl istekleriyle doğrulandı — bkz. HmStockApiClient üstündeki not) — düz, dayanıklılık
// politikalı bir HttpClient ile çağrılıyor.
builder.Services.AddTransient<ScraperEtiquetteHandler>();
builder.Services.AddTransient(_ => new HostRateLimitingHandler(requestsPerMinute: 60));

IHttpClientBuilder ApplyResiliencePolicies(IHttpClientBuilder httpClientBuilder) => httpClientBuilder
    .AddHttpMessageHandler<HostRateLimitingHandler>()
    .AddHttpMessageHandler<ScraperEtiquetteHandler>()
    .AddScraperResilience();

ApplyResiliencePolicies(builder.Services.AddHttpClient<IHmStockApiClient, HmStockApiClient>(client =>
{
    client.BaseAddress = new Uri("https://ofg.hm.com");
}));

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
