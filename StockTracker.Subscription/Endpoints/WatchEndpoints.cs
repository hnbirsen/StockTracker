using StockTracker.Subscription.DTOs;
using StockTracker.Subscription.Services;

namespace StockTracker.Subscription.Endpoints;

public static class WatchEndpoints
{
    public static void MapWatchEndpoints(this WebApplication app)
    {
        // POST /watches — mevcut WatchGroup varsa (aynı ürün+beden+mağaza) ona bağlanır, yoksa yeni oluşturur.
        app.MapPost("/watches", async (CreateWatchRequest request, IWatchService service) =>
        {
            if (request.UserId == Guid.Empty)
                return Results.BadRequest("UserId boş olamaz.");

            if (string.IsNullOrWhiteSpace(request.ProductCode))
                return Results.BadRequest("Ürün kodu boş olamaz.");

            if (string.IsNullOrWhiteSpace(request.Size))
                return Results.BadRequest("Beden boş olamaz.");

            var result = await service.CreateWatchAsync(request);
            if (!result.Success)
            {
                return Results.Json(
                    new { error = result.ErrorCode, message = result.ErrorMessage },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Created($"/watches/{result.Watch!.UserWatchId}", result.Watch);
        });

        // GET /watches?userId= — kullanıcının takip listesi
        app.MapGet("/watches", async (Guid userId, IWatchService service) =>
        {
            if (userId == Guid.Empty)
                return Results.BadRequest("userId boş olamaz.");

            var watches = await service.GetWatchesAsync(userId);
            return Results.Ok(watches);
        });

        // DELETE /watches/{id}?userId= — userId ile sahiplik doğrulanır, başkasının takibi silinemez
        app.MapDelete("/watches/{id:guid}", async (Guid id, Guid userId, IWatchService service) =>
        {
            if (userId == Guid.Empty)
                return Results.BadRequest("userId boş olamaz.");

            var deleted = await service.DeleteWatchAsync(id, userId);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        // GET /internal/watchers?productCode=&size=&storeId= — servisler arası direkt HTTP, gateway'den geçmez.
        // Notification Service (Faz 3.3), bir restock event'inde kimlere bildirim gideceğini buradan çözer.
        app.MapGet("/internal/watchers", async (string productCode, string size, Guid? storeId, IWatchService service) =>
        {
            var userIds = await service.GetWatcherUserIdsAsync(productCode, size, storeId);
            return Results.Ok(userIds);
        });

        app.MapGet("/health", () => Results.Ok("OK"));
    }
}
