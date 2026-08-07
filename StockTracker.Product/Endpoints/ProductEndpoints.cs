using Microsoft.EntityFrameworkCore;
using StockTracker.Product.Data;
using StockTracker.Product.DTOs;
using StockTracker.Product.Services;
using StackExchange.Redis;

namespace StockTracker.Product.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        // Ürün kodu ile marka sorgula
        // Query string kullanıyoruz çünkü ürün kodu slash içerebilir (örn. 12345/678/123)
        // GET /lookup?code=12345/678/123
        app.MapGet("/lookup/{**code}", async (string code, IProductLookupService lookupService) =>
        {
            if (string.IsNullOrWhiteSpace(code))
                return Results.BadRequest("Ürün kodu boş olamaz.");

            var result = await lookupService.LookupAsync(code);
            return Results.Ok(result);
        });

        // Ürün sayfası URL'i ile marka sorgula (kullanıcı ürün kodu yerine linki yapıştırdığında).
        // Yalnızca DAHA ÖNCE kaydedilmiş bir eşleme varsa sonuç döner — bkz. IProductLookupService.LookupByUrlAsync.
        app.MapGet("/lookup-by-url", async (string url, IProductLookupService lookupService) =>
        {
            if (string.IsNullOrWhiteSpace(url))
                return Results.BadRequest("URL boş olamaz.");

            var result = await lookupService.LookupByUrlAsync(url);
            return result is null ? Results.NotFound() : Results.Ok(result);
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

        // Cache metriklerini görüntüle
        app.MapGet("/cache/metrics", async (ICacheMetricsService metrics) =>
        {
            var summary = await metrics.GetSummaryAsync();
            return Results.Ok(summary);
        });

        // Belirli bir ürün kodunun cache'ini manuel temizle (test/debug amaçlı)
        // DELETE /cache?code=12345/678/123
        app.MapDelete("/cache/{**code}", async (string code, IConnectionMultiplexer redis) =>
        {
            var cache = redis.GetDatabase();
            await cache.KeyDeleteAsync($"product:lookup:{code.Trim()}");
            return Results.NoContent();
        });

        app.MapGet("/health", () => Results.Ok("OK"));
    }
}
