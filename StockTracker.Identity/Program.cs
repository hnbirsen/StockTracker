using Microsoft.EntityFrameworkCore;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, ".env")) && Directory.GetParent(root) != null)
{
    root = Directory.GetParent(root)!.FullName;
}

var envPath = Path.Combine(root, ".env");
if (File.Exists(envPath))
{
    var lines = File.ReadAllLines(envPath);
    foreach (var line in lines)
    {
        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}

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
    dbContext.Database.Migrate();
}

app.MapAuthEndpoints();

app.Run("http://0.0.0.0:5001");