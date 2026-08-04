using MassTransit;
using Microsoft.EntityFrameworkCore;
using StockTracker.Notification.Configuration;
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

// SMTP/FCM — gerçek sunucu/anahtar henüz yok (bkz. .claude/PENDING_INPUTS.md). Bilinçli olarak `throw`
// YOK: servis bunlar olmadan da ayağa kalkabilmeli, ilgili sender çağrıldığında yapılandırılmamış
// olduğunu loglayıp gönderim yapmadan false döner.
var fcmServerKey = Environment.GetEnvironmentVariable("FCM_SERVER_KEY") ?? builder.Configuration["Fcm:ServerKey"];

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.Configure<SmtpSettings>(options =>
{
    options.Host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? builder.Configuration["Smtp:Host"];
    options.Port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT") ?? builder.Configuration["Smtp:Port"], out var port) ? port : 587;
    options.Username = Environment.GetEnvironmentVariable("SMTP_USERNAME") ?? builder.Configuration["Smtp:Username"];
    options.Password = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? builder.Configuration["Smtp:Password"];
    options.UseSsl = !bool.TryParse(Environment.GetEnvironmentVariable("SMTP_USE_SSL") ?? builder.Configuration["Smtp:UseSsl"], out var useSsl) || useSsl;
    options.FromEmail = Environment.GetEnvironmentVariable("NOTIFICATION_FROM_EMAIL") ?? builder.Configuration["Smtp:FromEmail"] ?? "notifications@stocktracker.local";
    options.FromName = Environment.GetEnvironmentVariable("NOTIFICATION_FROM_NAME") ?? builder.Configuration["Smtp:FromName"];
});

builder.Services.AddHttpClient<IIdentityServiceClient, IdentityServiceClient>(client =>
{
    client.BaseAddress = new Uri(identityServiceUrl);
});

builder.Services.AddHttpClient<ISubscriptionServiceClient, SubscriptionServiceClient>(client =>
{
    client.BaseAddress = new Uri(subscriptionServiceUrl);
});

// FcmPushSender constructor'ı HttpClient dışında ham bir serverKey parametresi de alıyor —
// AddHttpClient<TInterface, TImplementation> bunu DI'dan otomatik çözemediği için adlandırılmış bir
// HttpClient + elle factory delegate ile inşa ediliyor. SmtpEmailSender'ın HttpClient'a ihtiyacı yok
// (MailKit kendi TCP/SMTP bağlantısını kuruyor), bu yüzden normal DI ile kaydediliyor.
builder.Services.AddHttpClient("FcmClient", client => client.BaseAddress = new Uri("https://fcm.googleapis.com"));

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

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
