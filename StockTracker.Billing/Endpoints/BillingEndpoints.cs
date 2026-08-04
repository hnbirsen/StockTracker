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

        app.MapGet("/health", () => Results.Ok("OK"));
    }
}
