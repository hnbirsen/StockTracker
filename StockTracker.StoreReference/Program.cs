using Microsoft.EntityFrameworkCore;
using StockTracker.StoreReference.Data;
using StockTracker.StoreReference.Endpoints;
using StockTracker.StoreReference.Services;
using StockTracker.Shared.Contracts.Configuration;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("STORE_DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("StoreDb")
    ?? throw new InvalidOperationException("StoreDb connection string bulunamadı.");

builder.Services.AddDbContext<StoreReferenceDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IStoreReferenceService, StoreReferenceService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StoreReferenceDbContext>();
    await db.Database.MigrateAsync();
}

app.MapStoreReferenceEndpoints();

app.Run("http://0.0.0.0:5004");
