using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTracker.Billing.Data;
using StockTracker.Billing.Entities;
using StockTracker.Billing.Services;

namespace StockTracker.Billing.Tests;

public class UserPlanServiceTests
{
    private static BillingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Plan CreateFreePlan() => new()
    {
        Id = BillingDbContext.FreePlanId,
        Name = "Free",
        MaxTrackedProducts = 3,
        CheckFrequencyMinutes = 60
    };

    [Fact]
    public async Task AssignFreePlanAsync_WhenUserHasNoPlan_AssignsFreePlan()
    {
        await using var db = CreateDbContext();
        db.Plans.Add(CreateFreePlan());
        await db.SaveChangesAsync();

        var sut = new UserPlanService(db);
        var userId = Guid.NewGuid();

        await sut.AssignFreePlanAsync(userId);

        var userPlan = await db.UserPlans.FirstAsync(up => up.UserId == userId);
        userPlan.PlanId.Should().Be(BillingDbContext.FreePlanId);
    }

    [Fact]
    public async Task AssignFreePlanAsync_WhenUserAlreadyHasPlan_DoesNothing()
    {
        await using var db = CreateDbContext();
        var freePlan = CreateFreePlan();
        var premiumPlan = new Plan { Id = BillingDbContext.PremiumPlanId, Name = "Premium", MaxTrackedProducts = 50, CheckFrequencyMinutes = 5 };
        db.Plans.AddRange(freePlan, premiumPlan);
        var userId = Guid.NewGuid();
        db.UserPlans.Add(new UserPlan { UserId = userId, PlanId = premiumPlan.Id });
        await db.SaveChangesAsync();

        var sut = new UserPlanService(db);
        await sut.AssignFreePlanAsync(userId);

        var userPlans = await db.UserPlans.Where(up => up.UserId == userId).ToListAsync();
        userPlans.Should().ContainSingle();
        userPlans[0].PlanId.Should().Be(premiumPlan.Id); // Free ile ezilmedi — idempotent, mevcut plan korunuyor
    }

    [Fact]
    public async Task GetPlansAsync_ReturnsOnlyActivePlans()
    {
        await using var db = CreateDbContext();
        db.Plans.AddRange(
            new Plan { Name = "Free", MaxTrackedProducts = 3, CheckFrequencyMinutes = 60, IsActive = true },
            new Plan { Name = "Legacy", MaxTrackedProducts = 10, CheckFrequencyMinutes = 30, IsActive = false });
        await db.SaveChangesAsync();

        var sut = new UserPlanService(db);
        var plans = await sut.GetPlansAsync();

        plans.Should().ContainSingle(p => p.Name == "Free");
    }

    [Fact]
    public async Task GetUserPlanAsync_WhenUserHasPlan_ReturnsPlanDetails()
    {
        await using var db = CreateDbContext();
        var freePlan = CreateFreePlan();
        db.Plans.Add(freePlan);
        var userId = Guid.NewGuid();
        db.UserPlans.Add(new UserPlan { UserId = userId, PlanId = freePlan.Id });
        await db.SaveChangesAsync();

        var sut = new UserPlanService(db);
        var result = await sut.GetUserPlanAsync(userId);

        result.Should().NotBeNull();
        result!.Plan.Name.Should().Be("Free");
        result.Plan.MaxTrackedProducts.Should().Be(3);
    }

    [Fact]
    public async Task GetUserPlanAsync_WhenUserHasNoPlan_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var sut = new UserPlanService(db);

        var result = await sut.GetUserPlanAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
