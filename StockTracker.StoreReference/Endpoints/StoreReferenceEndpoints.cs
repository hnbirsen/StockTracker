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

        // GET /stores/{id} — Subscription Service'in Stock Poller'ının (Faz 3.2) BrandSpecificStoreId'yi
        // WatchGroup.StoreId'den doğrudan çözmesi için (GET /stores'un aksine tek bir ID ile sorgular).
        app.MapGet("/stores/{id:guid}", async (Guid id, IStoreReferenceService service) =>
        {
            var store = await service.GetStoreByIdAsync(id);
            return store is not null ? Results.Ok(store) : Results.NotFound();
        });

        app.MapGet("/health", () => Results.Ok("OK"));
    }
}
