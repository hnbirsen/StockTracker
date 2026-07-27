using Microsoft.EntityFrameworkCore;
using StockTracker.Product.Data;
using StockTracker.Product.DTOs;
using StockTracker.Product.Services;

namespace StockTracker.Product.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        // Ürün kodu ile marka sorgula
        app.MapGet("/lookup/{productCode}", async (string productCode, IProductLookupService lookupService) =>
        {
            if (string.IsNullOrWhiteSpace(productCode))
                return Results.BadRequest("Ürün kodu boş olamaz.");

            var result = await lookupService.LookupAsync(productCode);
            return Results.Ok(result);
        });

        // Brand Detection sonucunu kaydet
        app.MapPost("/mappings", async (SaveMappingRequest request, IProductLookupService lookupService) =>
        {
            await lookupService.SaveMappingAsync(
                request.ProductCode,
                request.BrandId,
                request.ResolvedVia,
                request.Confidence,
                request.ProductUrl
            );
            return Results.Ok();
        });

        // Tüm aktif markaları listele
        app.MapGet("/brands", async (ProductDbContext db) =>
        {
            var brands = await db.Brands
                .Where(b => b.IsActive)
                .Select(b => new BrandDto(b.Id, b.Name, b.ScraperQueueName, b.IsActive))
                .ToListAsync();

            return Results.Ok(brands);
        });

        app.MapGet("/health", () => Results.Ok("OK"));
    }
}
