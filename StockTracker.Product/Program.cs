using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StockTracker.Product.Data;
using StockTracker.Product.Endpoints;
using StockTracker.Product.Services;
using StockTracker.Shared.Contracts.Configuration;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("PRODUCT_DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("ProductDb")
    ?? throw new InvalidOperationException("ProductDb connection string bulunamadı.");

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis connection string bulunamadı.");

builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseNpgsql(connectionString));

var redisOptions = ConfigurationOptions.Parse(redisConnection);
redisOptions.AbortOnConnectFail = false;

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisOptions));

builder.Services.AddScoped<ICodeFormatDetector, CodeFormatDetector>();
builder.Services.AddScoped<IProductLookupService, ProductLookupService>();
builder.Services.AddSingleton<ICacheMetricsService, CacheMetricsService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    await db.Database.MigrateAsync();
}

app.MapProductEndpoints();

app.Run("http://0.0.0.0:5002");
