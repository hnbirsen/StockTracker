using StockTracker.StoreReference.Services;

namespace StockTracker.StoreReference.Endpoints;

public static class StoreReferenceEndpoints
{
    public static void MapStoreReferenceEndpoints(this WebApplication app)
    {
        // GET /stores?brandId=&city=&district= — hepsi opsiyonel filtre
        app.MapGet("/stores", async (Guid? brandId, string? city, string? district, IStoreReferenceService service) =>
        {
            var stores = await service.GetStoresAsync(brandId, city, district);
            return Results.Ok(stores);
        });

        app.MapGet("/health", () => Results.Ok("OK"));
    }
}
