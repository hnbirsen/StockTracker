using MassTransit;
using Microsoft.EntityFrameworkCore;
using StockTracker.Billing.Consumers;
using StockTracker.Billing.Data;
using StockTracker.Billing.Endpoints;
using StockTracker.Billing.Services;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("BILLING_DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("BillingDb")
    ?? throw new InvalidOperationException("BillingDb connection string bulunamadı.");

builder.Services.AddDbContext<BillingDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IUserPlanService, UserPlanService>();

// UserRegisteredEvent (fanout) — Identity Service kayıt sonrası publish eder, Billing burada tüketip
// yeni kullanıcıya otomatik Free plan atar (Faz 4.1).
builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<UserRegisteredEventConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint("billing-user-registered-events", e =>
        {
            e.ConfigureConsumer<UserRegisteredEventConsumer>(context);
        });
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
    await db.Database.MigrateAsync();
}

app.MapBillingEndpoints();

app.Run("http://0.0.0.0:5007");
