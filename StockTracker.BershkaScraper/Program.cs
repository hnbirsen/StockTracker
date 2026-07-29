using MassTransit;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;
using StockTracker.BershkaScraper.Consumers;
using StockTracker.BershkaScraper.Http;
using StockTracker.BershkaScraper.Services;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;

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

app.Run("http://0.0.0.0:5009");
