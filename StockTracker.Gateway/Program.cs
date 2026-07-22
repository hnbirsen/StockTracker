var builder = WebApplication.CreateBuilder(args);

// YARP Reverse Proxy'yi konfigüre et
// appsettings.json'daki "ReverseProxy" bölümünü oku
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// CORS (tarayıcıdan çağrı yapabilmek için)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Health checks (servislerin sağlıklı olup olmadığını kontrol etmek için)
builder.Services.AddHealthChecks();

// Logging ekle (hata ayıklama için)
builder.Logging.AddConsole();

var app = builder.Build();

// CORS'u aktifleştir
app.UseCors("AllowAll");

// Health check endpoint'ini ekle (http://localhost:8000/health/gateway)
app.MapHealthChecks("/health/gateway");

// YARP reverse proxy'yi akış içine ekle
app.MapReverseProxy();

// Çalıştır (varsayılan port 8000)
app.Run("http://0.0.0.0:8000");