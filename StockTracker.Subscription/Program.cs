using Microsoft.EntityFrameworkCore;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Subscription.Data;
using StockTracker.Subscription.Endpoints;
using StockTracker.Subscription.Services;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("SUBSCRIPTION_DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("SubscriptionDb")
    ?? throw new InvalidOperationException("SubscriptionDb connection string bulunamadı.");

builder.Services.AddDbContext<SubscriptionDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IWatchService, WatchService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SubscriptionDbContext>();
    await db.Database.MigrateAsync();
}

app.MapWatchEndpoints();

app.Run("http://0.0.0.0:5006");