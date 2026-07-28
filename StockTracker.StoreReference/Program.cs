using StockTracker.Shared.Contracts.Configuration;

EnvFileLoader.LoadFromNearestEnvFile();

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => "OK");

app.Run("http://0.0.0.0:5004");