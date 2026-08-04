using MassTransit;
using Microsoft.EntityFrameworkCore;
using StockTracker.Notification.Consumers;
using StockTracker.Notification.Data;
using StockTracker.Notification.Services;
using StockTracker.Shared.Contracts.Configuration;
using StockTracker.Shared.Contracts.Messaging;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("NOTIFICATION_DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("NotificationDb")
    ?? throw new InvalidOperationException("NotificationDb connection string bulunamadı.");

var identityServiceUrl = Environment.GetEnvironmentVariable("IDENTITY_SERVICE_URL")
    ?? builder.Configuration["IdentityServiceUrl"]
    ?? throw new InvalidOperationException("IdentityServiceUrl bulunamadı.");

var subscriptionServiceUrl = Environment.GetEnvironmentVariable("SUBSCRIPTION_SERVICE_URL")
    ?? builder.Configuration["SubscriptionServiceUrl"]
    ?? throw new InvalidOperationException("SubscriptionServiceUrl bulunamadı.");

// SendGrid/FCM — gerçek hesap/anahtar henüz yok (bkz. .claude/ARCHITECTURE.md — Notification Service).
// Bilinçli olarak `throw` YOK: servis bu anahtarlar olmadan da ayağa kalkabilmeli, ilgili sender
// çağrıldığında yapılandırılmamış olduğunu loglayıp gönderim yapmadan false döner.
var sendGridApiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY") ?? builder.Configuration["SendGrid:ApiKey"];
var sendGridFromEmail = Environment.GetEnvironmentVariable("NOTIFICATION_FROM_EMAIL")
    ?? builder.Configuration["SendGrid:FromEmail"]
    ?? "notifications@stocktracker.local";
var fcmServerKey = Environment.GetEnvironmentVariable("FCM_SERVER_KEY") ?? builder.Configuration["Fcm:ServerKey"];

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHttpClient<IIdentityServiceClient, IdentityServiceClient>(client =>
{
    client.BaseAddress = new Uri(identityServiceUrl);
});

builder.Services.AddHttpClient<ISubscriptionServiceClient, SubscriptionServiceClient>(client =>
{
    client.BaseAddress = new Uri(subscriptionServiceUrl);
});

// SendGridEmailSender/FcmPushSender constructor'ları HttpClient dışında ham string parametreler de alıyor
// (apiKey/serverKey/fromEmail) — AddHttpClient<TInterface, TImplementation> bunları DI'dan otomatik
// çözemediği için (birden fazla `string` parametresi belirsizlik yaratır), adlandırılmış HttpClient'lar +
// elle factory delegate ile inşa ediliyor.
builder.Services.AddHttpClient("SendGridClient", client => client.BaseAddress = new Uri("https://api.sendgrid.com"));
builder.Services.AddHttpClient("FcmClient", client => client.BaseAddress = new Uri("https://fcm.googleapis.com"));

builder.Services.AddScoped<IEmailSender>(sp => new SendGridEmailSender(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("SendGridClient"),
    sendGridApiKey,
    sendGridFromEmail,
    sp.GetRequiredService<ILogger<SendGridEmailSender>>()));

builder.Services.AddScoped<IPushSender>(sp => new FcmPushSender(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("FcmClient"),
    fcmServerKey,
    sp.GetRequiredService<ILogger<FcmPushSender>>()));

builder.Services.AddSingleton<IUserDeviceTokenProvider, NoOpDeviceTokenProvider>();
builder.Services.AddScoped<INotificationProcessingService, NotificationProcessingService>();

// StockResultEvent (fanout) — Faz 3.3: her scraper sonucu burada işlenip yok->var geçişinde ilgili
// kullanıcılara bildirim gönderilir. Subscription Service'in kendi "subscription-stock-result-events"
// kuyruğundan bağımsız, ayrı bir kuyruk (aynı fanout exchange'in başka bir kopyası).
builder.Services.AddStockTrackerRabbitMq(
    builder.Configuration,
    configureConsumers: x => x.AddConsumer<StockResultEventConsumer>(),
    configureEndpoints: (context, cfg) =>
    {
        cfg.ReceiveEndpoint("notification-stock-result-events", e =>
        {
            e.ConfigureConsumer<StockResultEventConsumer>(context);
        });
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/health", () => Results.Ok("OK"));

app.Run("http://0.0.0.0:5008");
