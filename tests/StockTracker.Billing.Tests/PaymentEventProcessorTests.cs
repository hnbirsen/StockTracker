using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Billing.Data;
using StockTracker.Billing.Entities;
using StockTracker.Billing.Services;

namespace StockTracker.Billing.Tests;

public class PaymentEventProcessorTests
{
    private readonly Mock<IUserPlanService> _userPlanService = new();

    private static BillingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private PaymentEventProcessor CreateSut(BillingDbContext db) =>
        new(db, _userPlanService.Object, Mock.Of<ILogger<PaymentEventProcessor>>());

    [Fact]
    public async Task ProcessAsync_WhenEventNotSeenBefore_RecordsPaymentEvent()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.ProcessAsync(Platform.Apple, "evt-1", "SUBSCRIBED", "{}", "txn-1", SubscriptionStatus.Active, DateTimeOffset.UtcNow.AddMonths(1), CancellationToken.None);

        result.Should().BeTrue();
        (await db.PaymentEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_WhenSameEventProcessedTwice_SecondCallIsIdempotentSkip()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        await sut.ProcessAsync(Platform.Apple, "evt-1", "SUBSCRIBED", "{}", "txn-1", SubscriptionStatus.Active, DateTimeOffset.UtcNow.AddMonths(1), CancellationToken.None);
        var second = await sut.ProcessAsync(Platform.Apple, "evt-1", "SUBSCRIBED", "{}", "txn-1", SubscriptionStatus.Active, DateTimeOffset.UtcNow.AddMonths(1), CancellationToken.None);

        second.Should().BeFalse();
        (await db.PaymentEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_WhenMatchingSubscriptionExists_UpdatesStatusAndUpgradesToPremium()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        db.UserSubscriptions.Add(new UserSubscription
        {
            UserId = userId,
            PlanId = BillingDbContext.FreePlanId,
            Platform = Platform.Apple,
            StoreTransactionId = "txn-1",
            Status = SubscriptionStatus.Expired
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.ProcessAsync(Platform.Apple, "evt-1", "DID_RENEW", "{}", "txn-1", SubscriptionStatus.Active, DateTimeOffset.UtcNow.AddMonths(1), CancellationToken.None);

        var subscription = await db.UserSubscriptions.FirstAsync(s => s.UserId == userId);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        _userPlanService.Verify(s => s.SetPlanAsync(userId, BillingDbContext.PremiumPlanId), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenStatusBecomesExpired_DowngradesToFree()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        db.UserSubscriptions.Add(new UserSubscription
        {
            UserId = userId,
            PlanId = BillingDbContext.PremiumPlanId,
            Platform = Platform.Apple,
            StoreTransactionId = "txn-1",
            Status = SubscriptionStatus.Active
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.ProcessAsync(Platform.Apple, "evt-1", "EXPIRED", "{}", "txn-1", SubscriptionStatus.Expired, null, CancellationToken.None);

        _userPlanService.Verify(s => s.SetPlanAsync(userId, BillingDbContext.FreePlanId), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenNoMatchingSubscription_RecordsEventButDoesNotChangePlan()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.ProcessAsync(Platform.Apple, "evt-1", "SUBSCRIBED", "{}", "unknown-txn", SubscriptionStatus.Active, DateTimeOffset.UtcNow.AddMonths(1), CancellationToken.None);

        result.Should().BeTrue();
        (await db.PaymentEvents.CountAsync()).Should().Be(1);
        _userPlanService.Verify(s => s.SetPlanAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ForGooglePlatform_MatchesSubscriptionByPurchaseToken()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        db.UserSubscriptions.Add(new UserSubscription
        {
            UserId = userId,
            PlanId = BillingDbContext.FreePlanId,
            Platform = Platform.Google,
            PurchaseToken = "token-abc",
            Status = SubscriptionStatus.Unknown
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.ProcessAsync(Platform.Google, "evt-1", "2", "{}", "token-abc", SubscriptionStatus.Active, DateTimeOffset.UtcNow.AddMonths(1), CancellationToken.None);

        var subscription = await db.UserSubscriptions.FirstAsync(s => s.UserId == userId);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
    }
}
