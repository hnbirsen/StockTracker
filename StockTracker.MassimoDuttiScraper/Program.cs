using MassTransit;
using StackExchange.Redis;
using StockTracker.MassimoDuttiScraper.Consumers;
using StockTracker.MassimoDuttiScraper.Services;
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

// Online stok için Playwright/gerçek Chrome kanalı ŞART (bkz. IMassimoDuttiPdpFetcher üstündeki not — ürün
// sayfası Akamai Bot Manager'ın arkasında). Chrome process'i pahalı olduğu için tekil (singleton) kayıtlı.
builder.Services.AddSingleton<IMassimoDuttiPdpFetcher, PlaywrightMassimoDuttiFetcher>();

// Mağaza bulucu API'si (`itxrest/2/bam/store/.../physical-store`) AYNI domain'de olmasına rağmen Akamai
// korumasız (canlı doğrulandı — bkz. MassimoDuttiStockApiClient üstündeki not) — Bershka'nın ayrı stok API'si
// gibi düz, dayanıklılık politikalı bir HttpClient ile çağrılıyor.
builder.Services.AddTransient<ScraperEtiquetteHandler>();
builder.Services.AddTransient(_ => new HostRateLimitingHandler(requestsPerMinute: 60));

IHttpClientBuilder ApplyResiliencePolicies(IHttpClientBuilder httpClientBuilder) => httpClientBuilder
    .AddHttpMessageHandler<HostRateLimitingHandler>()
    .AddHttpMessageHandler<ScraperEtiquetteHandler>()
    .AddScraperResilience();

ApplyResiliencePolicies(builder.Services.AddHttpClient<IMassimoDuttiStockApiClient, MassimoDuttiStockApiClient>(client =>
{
    client.BaseAddress = new Uri("https://www.massimodutti.com");
}));

builder.Services.AddScoped<IMassimoDuttiStockCheckService, MassimoDuttiStockCheckService>();

builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<CheckStockCommandConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(QueueNaming.StockCheckQueue("massimodutti"), e =>
        {
            e.ConfigureConsumer<CheckStockCommandConsumer>(context);
        });
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("OK"));

app.MapGet("/health/scraper-stats", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var stats = await healthLog.GetStatsAsync("massimodutti", lastN ?? 100);
    return Results.Ok(stats);
});

app.MapGet("/health/scraper-failures", async (IScraperHealthLogService healthLog, int? lastN) =>
{
    var failures = await healthLog.GetRecentFailuresAsync("massimodutti", lastN ?? 20);
    return Results.Ok(failures);
});

app.Run("http://0.0.0.0:5013");
