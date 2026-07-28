using Microsoft.EntityFrameworkCore;
using StockTracker.BrandDetection.Data;
using StockTracker.BrandDetection.Endpoints;
using StockTracker.BrandDetection.Services;
using StockTracker.Shared.Contracts.Configuration;

EnvFileLoader.LoadFromNearestEnvFile();

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
