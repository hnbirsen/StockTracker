using MassTransit;
using Microsoft.EntityFrameworkCore;
using StockTracker.Billing.Configuration;
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

// Apple/Google gerçek hesap/anahtarları bu oturumda yok (bkz. .claude/ARCHITECTURE.md > Billing) —
// bilinçli olarak `throw` YOK: servis bunlar olmadan da ayağa kalkabilmeli, ilgili client çağrıldığında
// yapılandırılmamış olduğunu loglayıp null/false döner.
builder.Services.Configure<AppleStoreSettings>(options =>
{
    options.IssuerId = Environment.GetEnvironmentVariable("APPLE_ISSUER_ID") ?? builder.Configuration["Apple:IssuerId"];
    options.KeyId = Environment.GetEnvironmentVariable("APPLE_KEY_ID") ?? builder.Configuration["Apple:KeyId"];
    options.PrivateKeyBase64 = Environment.GetEnvironmentVariable("APPLE_PRIVATE_KEY_BASE64") ?? builder.Configuration["Apple:PrivateKeyBase64"];
    options.BundleId = Environment.GetEnvironmentVariable("APPLE_BUNDLE_ID") ?? builder.Configuration["Apple:BundleId"];
    options.Environment = Environment.GetEnvironmentVariable("APPLE_STORE_ENVIRONMENT") ?? builder.Configuration["Apple:Environment"] ?? "Sandbox";
});

builder.Services.Configure<GooglePlaySettings>(options =>
{
    options.ServiceAccountJsonBase64 = Environment.GetEnvironmentVariable("GOOGLE_PLAY_SERVICE_ACCOUNT_JSON_BASE64") ?? builder.Configuration["GooglePlay:ServiceAccountJsonBase64"];
    options.PackageName = Environment.GetEnvironmentVariable("GOOGLE_PLAY_PACKAGE_NAME") ?? builder.Configuration["GooglePlay:PackageName"];
    options.PushAudience = Environment.GetEnvironmentVariable("GOOGLE_PLAY_PUSH_AUDIENCE") ?? builder.Configuration["GooglePlay:PushAudience"];
});

builder.Services.AddScoped<IUserPlanService, UserPlanService>();
builder.Services.AddScoped<IPaymentEventProcessor, PaymentEventProcessor>();
builder.Services.AddScoped<IPurchaseVerificationService, PurchaseVerificationService>();
builder.Services.AddSingleton<IAppleJwsVerifier, AppleJwsVerifier>();
builder.Services.AddScoped<IAppleWebhookProcessor, AppleWebhookProcessor>();
builder.Services.AddScoped<IGoogleWebhookProcessor, GoogleWebhookProcessor>();

builder.Services.AddHttpClient<IAppleAppStoreServerClient, AppleAppStoreServerClient>();
builder.Services.AddHttpClient<IGooglePlayDeveloperClient, GooglePlayDeveloperClient>();
builder.Services.AddHttpClient<IGoogleOidcTokenValidator, GoogleOidcTokenValidator>();

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
