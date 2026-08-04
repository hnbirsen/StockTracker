using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Billing.Data;
using StockTracker.Billing.DTOs;
using StockTracker.Billing.Entities;
using StockTracker.Billing.Services;

namespace StockTracker.Billing.Tests;

public class PurchaseVerificationServiceTests
{
    private readonly Mock<IAppleAppStoreServerClient> _appleClient = new();
    private readonly Mock<IGooglePlayDeveloperClient> _googleClient = new();
    private readonly Mock<IUserPlanService> _userPlanService = new();

    private static BillingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private PurchaseVerificationService CreateSut(BillingDbContext db) => new(
        db, _appleClient.Object, _googleClient.Object, _userPlanService.Object, Mock.Of<ILogger<PurchaseVerificationService>>());

    [Fact]
    public async Task VerifyAndRecordAsync_WithInvalidPlatform_ReturnsFailure()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.VerifyAndRecordAsync(new VerifyPurchaseRequest(Guid.NewGuid(), "Amazon", "txn-1", null), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAndRecordAsync_WhenAppleClientReturnsNull_ReturnsFailure()
    {
        await using var db = CreateDbContext();
        _appleClient.Setup(c => c.GetTransactionInfoAsync("txn-1", It.IsAny<CancellationToken>())).ReturnsAsync((AppleTransactionInfo?)null);
        var sut = CreateSut(db);

        var result = await sut.VerifyAndRecordAsync(new VerifyPurchaseRequest(Guid.NewGuid(), "Apple", "txn-1", null), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAndRecordAsync_WhenAppleTransactionActive_CreatesSubscriptionAndUpgradesToPremium()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        _appleClient
            .Setup(c => c.GetTransactionInfoAsync("txn-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppleTransactionInfo("orig-txn-1", "premium_monthly", DateTimeOffset.UtcNow.AddMonths(1), null));

        var sut = CreateSut(db);
        var result = await sut.VerifyAndRecordAsync(new VerifyPurchaseRequest(userId, "Apple", "txn-1", null), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Subscription!.Status.Should().Be(nameof(SubscriptionStatus.Active));
        var subscription = await db.UserSubscriptions.FirstAsync(s => s.UserId == userId);
        subscription.StoreTransactionId.Should().Be("orig-txn-1");
        _userPlanService.Verify(s => s.SetPlanAsync(userId, BillingDbContext.PremiumPlanId), Times.Once);
    }

    [Fact]
    public async Task VerifyAndRecordAsync_WhenAppleTransactionExpired_DoesNotUpgradeToPremium()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        _appleClient
            .Setup(c => c.GetTransactionInfoAsync("txn-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppleTransactionInfo("orig-txn-1", "premium_monthly", DateTimeOffset.UtcNow.AddDays(-1), null));

        var sut = CreateSut(db);
        var result = await sut.VerifyAndRecordAsync(new VerifyPurchaseRequest(userId, "Apple", "txn-1", null), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Subscription!.Status.Should().Be(nameof(SubscriptionStatus.Expired));
        _userPlanService.Verify(s => s.SetPlanAsync(userId, BillingDbContext.FreePlanId), Times.Once);
    }

    [Fact]
    public async Task VerifyAndRecordAsync_ForGoogleWithoutSubscriptionId_ReturnsFailure()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.VerifyAndRecordAsync(new VerifyPurchaseRequest(Guid.NewGuid(), "Google", "token-1", null), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAndRecordAsync_WhenGoogleSubscriptionActive_CreatesSubscriptionWithPurchaseToken()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        _googleClient
            .Setup(c => c.GetSubscriptionAsync("sub-1", "token-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleSubscriptionInfo(DateTimeOffset.UtcNow.AddMonths(1), true, null));

        var sut = CreateSut(db);
        var result = await sut.VerifyAndRecordAsync(new VerifyPurchaseRequest(userId, "Google", "token-1", "sub-1"), CancellationToken.None);

        result.Success.Should().BeTrue();
        var subscription = await db.UserSubscriptions.FirstAsync(s => s.UserId == userId);
        subscription.PurchaseToken.Should().Be("token-1");
        subscription.Platform.Should().Be(Platform.Google);
    }

    [Fact]
    public async Task VerifyAndRecordAsync_WhenCalledTwiceForSameUser_UpdatesExistingSubscriptionInPlace()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        _appleClient
            .Setup(c => c.GetTransactionInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppleTransactionInfo("orig-txn-1", "premium_monthly", DateTimeOffset.UtcNow.AddMonths(1), null));

        var sut = CreateSut(db);
        await sut.VerifyAndRecordAsync(new VerifyPurchaseRequest(userId, "Apple", "txn-1", null), CancellationToken.None);
        await sut.VerifyAndRecordAsync(new VerifyPurchaseRequest(userId, "Apple", "txn-1", null), CancellationToken.None);

        (await db.UserSubscriptions.CountAsync(s => s.UserId == userId)).Should().Be(1);
    }
}
