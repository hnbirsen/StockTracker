using StockTracker.SearchOrchestrator.DTOs;
using StockTracker.SearchOrchestrator.Services;

namespace StockTracker.SearchOrchestrator.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapPost("/search", async (SearchRequest request, ISearchThrottleService throttle, ISearchOrchestratorService orchestrator) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProductCode))
                return Results.BadRequest("Ürün kodu boş olamaz.");

            if (string.IsNullOrWhiteSpace(request.Size))
                return Results.BadRequest("Beden boş olamaz.");

            if (request.UserId == Guid.Empty)
                return Results.BadRequest("UserId boş olamaz.");

            var acquired = await throttle.TryAcquireAsync(request.UserId, request.ProductCode, request.Size);
            if (!acquired)
            {
                return Results.Json(
                    new { message = "Bu ürün/beden için aramanız zaten işleniyor. Lütfen kısa süre sonra tekrar deneyin." },
                    statusCode: StatusCodes.Status429TooManyRequests
                );
            }

            var response = await orchestrator.SearchAsync(request);

            // "Queued": istek kabul edildi, sonuç asenkron olarak bildirimle gelecek (202).
            // "BrandUnknown": marka tespit edilemedi/manuel seçim gerekiyor, hemen yanıtlanır (200).
            return response.Status == "Queued"
                ? Results.Accepted(value: response)
                : Results.Ok(response);
        });

        app.MapGet("/health", () => Results.Ok("OK"));
    }
}
