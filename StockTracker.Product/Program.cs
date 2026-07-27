using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StockTracker.Product.Data;
using StockTracker.Product.Endpoints;
using StockTracker.Product.Services;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, ".env")) && Directory.GetParent(root) != null)
{
    root = Directory.GetParent(root)!.FullName;
}

var envPath = Path.Combine(root, ".env");
if (File.Exists(envPath))
{
    var lines = File.ReadAllLines(envPath);
    foreach (var line in lines)
    {
        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}

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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    await db.Database.MigrateAsync();
}

app.MapProductEndpoints();

app.Run("http://0.0.0.0:5002");
