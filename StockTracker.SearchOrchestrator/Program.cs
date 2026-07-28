using StackExchange.Redis;
using StockTracker.SearchOrchestrator.Endpoints;
using StockTracker.SearchOrchestrator.Services;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var productServiceUrl = Environment.GetEnvironmentVariable("PRODUCT_SERVICE_URL")
    ?? builder.Configuration["ProductServiceUrl"]
    ?? throw new InvalidOperationException("ProductServiceUrl bulunamadı.");

var brandDetectionServiceUrl = Environment.GetEnvironmentVariable("BRAND_DETECTION_SERVICE_URL")
    ?? builder.Configuration["BrandDetectionServiceUrl"]
    ?? throw new InvalidOperationException("BrandDetectionServiceUrl bulunamadı.");

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis connection string bulunamadı.");

builder.Services.AddHttpClient<IProductServiceClient, ProductServiceClient>(client =>
{
    client.BaseAddress = new Uri(productServiceUrl);
});

builder.Services.AddHttpClient<IBrandDetectionServiceClient, BrandDetectionServiceClient>(client =>
{
    client.BaseAddress = new Uri(brandDetectionServiceUrl);
});

var redisOptions = ConfigurationOptions.Parse(redisConnection);
redisOptions.AbortOnConnectFail = false;

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisOptions));

builder.Services.AddSingleton<ISearchThrottleService, SearchThrottleService>();
builder.Services.AddScoped<ISearchOrchestratorService, SearchOrchestratorService>();

builder.Services.AddStockTrackerRabbitMq(builder.Configuration);

var app = builder.Build();

app.MapSearchEndpoints();

app.Run("http://0.0.0.0:5005");
