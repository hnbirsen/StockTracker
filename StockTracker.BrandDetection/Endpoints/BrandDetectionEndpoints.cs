using StockTracker.BrandDetection.DTOs;
using StockTracker.BrandDetection.Services;

namespace StockTracker.BrandDetection.Endpoints;

public static class BrandDetectionEndpoints
{
    public static void MapBrandDetectionEndpoints(this WebApplication app)
    {
        // Otomatik marka tespiti (format eşleşmesi)
        app.MapPost("/resolve", async (ResolveRequest request, IBrandDetectionService service) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProductCode))
                return Results.BadRequest("Ürün kodu boş olamaz.");

            var result = await service.ResolveAsync(request.ProductCode);
            return Results.Ok(result);
        });

        // Manuel marka seçimi (kullanıcı markayı kendisi seçtiğinde)
        app.MapPost("/resolve/manual", async (ManualResolveRequest request, IBrandDetectionService service) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProductCode))
                return Results.BadRequest("Ürün kodu boş olamaz.");

            var result = await service.ManualResolveAsync(request.ProductCode, request.BrandId, request.BrandName);
            return Results.Ok(result);
        });

        app.MapGet("/health", () => Results.Ok("OK"));
    }
}
