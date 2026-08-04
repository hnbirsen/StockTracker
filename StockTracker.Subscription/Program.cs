using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;
using StockTracker.Subscription.Configuration;
using StockTracker.Subscription.Consumers;
using StockTracker.Subscription.Data;
using StockTracker.Subscription.Endpoints;
using StockTracker.Subscription.Jobs;
using StockTracker.Subscription.Services;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("SUBSCRIPTION_DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("SubscriptionDb")
    ?? throw new InvalidOperationException("SubscriptionDb connection string bulunamadı.");

var productServiceUrl = Environment.GetEnvironmentVariable("PRODUCT_SERVICE_URL")
    ?? builder.Configuration["ProductServiceUrl"]
    ?? throw new InvalidOperationException("ProductServiceUrl bulunamadı.");

var storeReferenceServiceUrl = Environment.GetEnvironmentVariable("STORE_REFERENCE_SERVICE_URL")
    ?? builder.Configuration["StoreReferenceServiceUrl"]
    ?? throw new InvalidOperationException("StoreReferenceServiceUrl bulunamadı.");

builder.Services.AddDbContext<SubscriptionDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IWatchService, WatchService>();
builder.Services.AddScoped<IWatchGroupStatusUpdater, WatchGroupStatusUpdater>();
builder.Services.AddScoped<IStockPollerService, StockPollerService>();

builder.Services.Configure<PollerSettings>(builder.Configuration.GetSection("Poller"));

builder.Services.AddHttpClient<IProductServiceClient, ProductServiceClient>(client =>
{
    client.BaseAddress = new Uri(productServiceUrl);
});

builder.Services.AddHttpClient<IStoreReferenceServiceClient, StoreReferenceServiceClient>(client =>
{
    client.BaseAddress = new Uri(storeReferenceServiceUrl);
});

// StockResultEvent (fanout) tüketilir — Faz 3.2: poller'ın gönderdiği CheckStockCommand'lara verilen
// sonuçlar, WatchGroup.LastKnownStatus/LastCheckedAt'i güncellemek için buradan işlenir.
builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<StockResultEventConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint("subscription-stock-result-events", e =>
        {
            e.ConfigureConsumer<StockResultEventConsumer>(context);
        });
    });

var pollerSettings = builder.Configuration.GetSection("Poller").Get<PollerSettings>() ?? new PollerSettings();

builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("StockPollerJob");
    q.AddJob<StockPollerJob>(opts => opts.WithIdentity(jobKey));
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithSimpleSchedule(s => s
            .WithIntervalInSeconds(pollerSettings.IntervalSeconds)
            .RepeatForever()));
});
builder.Services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SubscriptionDbContext>();
    await db.Database.MigrateAsync();
}

app.MapWatchEndpoints();

app.Run("http://0.0.0.0:5006");
