using Microsoft.EntityFrameworkCore;
using StockTracker.Billing.Data;
using StockTracker.Billing.DTOs;
using StockTracker.Billing.Entities;

namespace StockTracker.Billing.Services;

public interface IUserPlanService
{
    Task AssignFreePlanAsync(Guid userId);
    Task<List<PlanDto>> GetPlansAsync();
    Task<UserPlanDto?> GetUserPlanAsync(Guid userId);
}

public class UserPlanService : IUserPlanService
{
    private readonly BillingDbContext _db;

    public UserPlanService(BillingDbContext db)
    {
        _db = db;
    }

    // Faz 4.1 — UserRegisteredEvent consumer'ı tarafından çağrılır. Idempotent: kullanıcının zaten bir
    // planı varsa (ör. event'in at-least-once teslimatla iki kez tüketilmesi) hiçbir şey yapmaz.
    public async Task AssignFreePlanAsync(Guid userId)
    {
        var alreadyHasPlan = await _db.UserPlans.AnyAsync(up => up.UserId == userId);
        if (alreadyHasPlan)
            return;

        _db.UserPlans.Add(new UserPlan { UserId = userId, PlanId = BillingDbContext.FreePlanId });
        await _db.SaveChangesAsync();
    }

    public async Task<List<PlanDto>> GetPlansAsync()
    {
        return await _db.Plans
            .Where(p => p.IsActive)
            .Select(p => new PlanDto(p.Id, p.Name, p.MaxTrackedProducts, p.CheckFrequencyMinutes, p.AppStoreProductId, p.PlayStoreProductId))
            .ToListAsync();
    }

    public async Task<UserPlanDto?> GetUserPlanAsync(Guid userId)
    {
        var userPlan = await _db.UserPlans
            .Include(up => up.Plan)
            .FirstOrDefaultAsync(up => up.UserId == userId);

        if (userPlan is null)
            return null;

        return new UserPlanDto(
            userPlan.UserId,
            new PlanDto(
                userPlan.Plan.Id, userPlan.Plan.Name, userPlan.Plan.MaxTrackedProducts,
                userPlan.Plan.CheckFrequencyMinutes, userPlan.Plan.AppStoreProductId, userPlan.Plan.PlayStoreProductId),
            userPlan.AssignedAt);
    }
}
