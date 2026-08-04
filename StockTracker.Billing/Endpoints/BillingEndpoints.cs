using StockTracker.Billing.DTOs;
using StockTracker.Billing.Services;

namespace StockTracker.Billing.Endpoints;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        // GET /plans — aktif planların listesi (Free/Premium)
        app.MapGet("/plans", async (IUserPlanService service) =>
        {
            var plans = await service.GetPlansAsync();
            return Results.Ok(plans);
        });

        // GET /users/{userId}/plan — kullanıcının güncel planı (Faz 4.3'te limit kontrolü bunun üzerine kurulacak)
        app.MapGet("/users/{userId:guid}/plan", async (Guid userId, IUserPlanService service) =>
        {
            var userPlan = await service.GetUserPlanAsync(userId);
            return userPlan is not null ? Results.Ok(userPlan) : Results.NotFound();
        });

        // GET /limits/{userId} — Subscription Service'in yeni bir UserWatch oluşturmadan önce sorduğu
        // limit bilgisi (Faz 4.3). Kullanıcının planı yoksa Free limitlerine düşer, asla 404 vermez.
        app.MapGet("/limits/{userId:guid}", async (Guid userId, IUserPlanService service) =>
        {
            var limits = await service.GetUserLimitsAsync(userId);
            return Results.Ok(limits);
        });

        // POST /verify-purchase — mobil client, App Store/Play Store'da tamamladığı satın almanın
        // receipt/token'ını gönderir; ilgili store'un server-to-server API'sine karşı doğrulanır.
        // Not: Gateway "/api/billing" prefix'ini soyduğu için servisin kendi route'ları "/billing/..."
        // OLMAMALI (diğer tüm servislerle aynı konvansiyon — bkz. .claude/ARCHITECTURE.md).
        app.MapPost("/verify-purchase", async (VerifyPurchaseRequest request, IPurchaseVerificationService service, CancellationToken ct) =>
        {
            if (request.UserId == Guid.Empty)
                return Results.BadRequest("UserId boş olamaz.");
            if (string.IsNullOrWhiteSpace(request.TransactionIdOrToken))
                return Results.BadRequest("TransactionIdOrToken boş olamaz.");

            var result = await service.VerifyAndRecordAsync(request, ct);
            return result.Success
                ? Results.Ok(result.Subscription)
                : Results.Json(new { error = result.FailureReason }, statusCode: StatusCodes.Status422UnprocessableEntity);
        });

        // POST /webhooks/apple — App Store Server Notifications V2. Güvenlik, isteğin kendisindeki
        // JWS imzasından geliyor (bkz. AppleJwsVerifier) — ayrı bir paylaşılan secret/header yok.
        app.MapPost("/webhooks/apple", async (AppleWebhookRequest body, IAppleWebhookProcessor processor, CancellationToken ct) =>
        {
            var success = await processor.ProcessAsync(body.SignedPayload, ct);
            return success ? Results.Ok() : Results.BadRequest();
        });

        // POST /webhooks/google — Cloud Pub/Sub push. Güvenlik, Authorization header'ındaki
        // Google-imzalı OIDC bearer token'dan geliyor (bkz. GoogleOidcTokenValidator).
        app.MapPost("/webhooks/google", async (HttpRequest httpRequest, IGoogleWebhookProcessor processor, CancellationToken ct) =>
        {
            using var reader = new StreamReader(httpRequest.Body);
            var body = await reader.ReadToEndAsync(ct);
            var bearerToken = httpRequest.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

            var result = await processor.ProcessAsync(bearerToken, body, ct);
            return result switch
            {
                GoogleWebhookResult.Unauthorized => Results.Unauthorized(),
                GoogleWebhookResult.InvalidPayload => Results.BadRequest(),
                _ => Results.Ok()
            };
        });

        app.MapGet("/health", () => Results.Ok("OK"));
    }
}

public record AppleWebhookRequest(string SignedPayload);
