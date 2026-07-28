using Microsoft.EntityFrameworkCore;
using StockTracker.BrandDetection.Data;
using StockTracker.BrandDetection.Endpoints;
using StockTracker.BrandDetection.Services;

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

var connectionString = Environment.GetEnvironmentVariable("BRAND_DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("BrandDb")
    ?? throw new InvalidOperationException("BrandDb connection string bulunamadı.");

var productServiceUrl = Environment.GetEnvironmentVariable("PRODUCT_SERVICE_URL")
    ?? builder.Configuration["ProductServiceUrl"]
    ?? throw new InvalidOperationException("ProductServiceUrl bulunamadı.");

builder.Services.AddDbContext<BrandDetectionDbContext>(options =>
    options.UseNpgsql(connectionString));

// Product Service'e HTTP çağrısı için typed client
builder.Services.AddHttpClient<IProductServiceClient, ProductServiceClient>(client =>
{
    client.BaseAddress = new Uri(productServiceUrl);
});

builder.Services.AddScoped<IBrandDetectionService, BrandDetectionService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BrandDetectionDbContext>();
    await db.Database.MigrateAsync();
}

app.MapBrandDetectionEndpoints();

app.Run("http://0.0.0.0:5003");
