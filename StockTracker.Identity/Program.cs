using Microsoft.EntityFrameworkCore;
using StockTracker.Identity.Data;
using StockTracker.Identity.Endpoints;
using StockTracker.Identity.Services;
using StockTracker.Shared.Contracts.Configuration;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

// db connection
var connectionString = Environment.GetEnvironmentVariable("IDENTITY_DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("Connection string 'IdentityDb' not found.");

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(connectionString));

// configure jwt settings
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
    ?? builder.Configuration["JwtSettings:SecretKey"]
    ?? throw new InvalidOperationException("JWT secret key not found.");

// services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();

var app = builder.Build();

// auto migrate database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapAuthEndpoints();

app.Run("http://0.0.0.0:5001");