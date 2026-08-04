using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Shared.Contracts.Messages.V1;
using StockTracker.Subscription.Data;
using StockTracker.Subscription.DTOs;
using StockTracker.Subscription.Entities;
using StockTracker.Subscription.Services;

namespace StockTracker.Subscription.Tests;

public class WatchServiceTests
{
    private readonly Mock<IBillingServiceClient> _billingClient = new();

    private static SubscriptionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SubscriptionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private WatchService CreateSut(SubscriptionDbContext db, UserLimitsResponse? limits = null)
    {
        _billingClient
            .Setup(c => c.GetUserLimitsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(limits ?? new UserLimitsResponse(Guid.Empty, "Free", 100, 60)); // testlerde varsayılan: pratikte hiç dolmayan bir limit

        return new WatchService(db, _billingClient.Object, Mock.Of<ILogger<WatchService>>());
    }

    [Fact]
    public async Task CreateWatchAsync_WhenNoExistingWatchGroup_CreatesNewGroupAndUserWatch()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var userId = Guid.NewGuid();

        var result = await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1234567890123", "M", null));

        result.Success.Should().BeTrue();
        result.Watch!.ProductCode.Should().Be("1234567890123");
        result.Watch.Size.Should().Be("M");
        result.Watch.StoreId.Should().BeNull();
        db.WatchGroups.Should().ContainSingle();
        db.UserWatches.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateWatchAsync_WhenTwoDifferentUsersWatchSameProductSizeStore_DedupsToSingleWatchGroup()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var storeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await sut.CreateWatchAsync(new CreateWatchRequest(userA, "1234567890123", "M", storeId));
        await sut.CreateWatchAsync(new CreateWatchRequest(userB, "1234567890123", "M", storeId));

        db.WatchGroups.Should().ContainSingle();
        db.UserWatches.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateWatchAsync_WhenSameUserWatchesTwice_DoesNotCreateDuplicateUserWatch()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var userId = Guid.NewGuid();
        var request = new CreateWatchRequest(userId, "1234567890123", "M", null);

        var first = await sut.CreateWatchAsync(request);
        var second = await sut.CreateWatchAsync(request);

        first.Watch!.UserWatchId.Should().Be(second.Watch!.UserWatchId);
        db.UserWatches.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateWatchAsync_WhenSameProductSizeButDifferentStore_CreatesSeparateWatchGroups()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var userId = Guid.NewGuid();

        await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1234567890123", "M", Guid.NewGuid()));
        await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1234567890123", "M", Guid.NewGuid()));

        db.WatchGroups.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateWatchAsync_WhenUserAtPlanLimit_ReturnsFailureWithoutCreatingWatch()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var sut = CreateSut(db, new UserLimitsResponse(userId, "Free", 1, 60));

        var first = await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1111111111111", "S", null));
        var second = await sut.CreateWatchAsync(new CreateWatchRequest(userId, "2222222222222", "M", null));

        first.Success.Should().BeTrue();
        second.Success.Should().BeFalse();
        second.ErrorCode.Should().Be("WATCH_LIMIT_EXCEEDED");
        db.UserWatches.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateWatchAsync_WhenAlreadyWatchingSameGroupAtLimit_StillSucceeds_BecauseNoNewSlotConsumed()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var sut = CreateSut(db, new UserLimitsResponse(userId, "Free", 1, 60));
        var request = new CreateWatchRequest(userId, "1111111111111", "S", null);

        await sut.CreateWatchAsync(request); // limiti dolduruyor
        var repeated = await sut.CreateWatchAsync(request); // aynı grup — dedup, limit kontrolüne girmemeli

        repeated.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreateWatchAsync_WhenBillingServiceUnavailable_FailsOpenAndAllowsWatch()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        _billingClient.Setup(c => c.GetUserLimitsAsync(userId)).ThrowsAsync(new HttpRequestException("unreachable"));
        var sut = new WatchService(db, _billingClient.Object, Mock.Of<ILogger<WatchService>>());

        var result = await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1234567890123", "M", null));

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetWatchesAsync_ReturnsOnlyRequestedUsersWatches()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await sut.CreateWatchAsync(new CreateWatchRequest(userA, "1111111111111", "S", null));
        await sut.CreateWatchAsync(new CreateWatchRequest(userB, "2222222222222", "L", null));

        var result = await sut.GetWatchesAsync(userA);

        result.Should().ContainSingle();
        result[0].ProductCode.Should().Be("1111111111111");
    }

    [Fact]
    public async Task GetWatchesAsync_IncludesLastKnownStatusAndLastCheckedAt_FromWatchGroup()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var watchGroup = new WatchGroup
        {
            ProductCode = "1234567890123",
            Size = "M",
            LastKnownStatus = StockStatus.OutOfStock,
            LastCheckedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.WatchGroups.Add(watchGroup);
        db.UserWatches.Add(new UserWatch { UserId = userId, WatchGroupId = watchGroup.Id });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var result = await sut.GetWatchesAsync(userId);

        result.Should().ContainSingle();
        result[0].LastKnownStatus.Should().Be(StockStatus.OutOfStock);
        result[0].LastCheckedAt.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task DeleteWatchAsync_WhenOwnedByUser_RemovesUserWatchOnly()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var userId = Guid.NewGuid();
        var created = await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1234567890123", "M", null));

        var deleted = await sut.DeleteWatchAsync(created.Watch!.UserWatchId, userId);

        deleted.Should().BeTrue();
        db.UserWatches.Should().BeEmpty();
        db.WatchGroups.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteWatchAsync_WhenNotOwnedByUser_ReturnsFalseAndDoesNotDelete()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var created = await sut.CreateWatchAsync(new CreateWatchRequest(owner, "1234567890123", "M", null));

        var deleted = await sut.DeleteWatchAsync(created.Watch!.UserWatchId, intruder);

        deleted.Should().BeFalse();
        db.UserWatches.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteWatchAsync_WhenIdDoesNotExist_ReturnsFalse()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var deleted = await sut.DeleteWatchAsync(Guid.NewGuid(), Guid.NewGuid());

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetWatcherUserIdsAsync_ReturnsAllUsersWatchingThatWatchGroup()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await sut.CreateWatchAsync(new CreateWatchRequest(userA, "1234567890123", "M", null));
        await sut.CreateWatchAsync(new CreateWatchRequest(userB, "1234567890123", "M", null));

        var watchers = await sut.GetWatcherUserIdsAsync("1234567890123", "M", null);

        watchers.Should().BeEquivalentTo(new[] { userA, userB });
    }

    [Fact]
    public async Task GetWatcherUserIdsAsync_WhenNoMatchingWatchGroup_ReturnsEmptyList()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var watchers = await sut.GetWatcherUserIdsAsync("does-not-exist", "M", null);

        watchers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWatcherUserIdsAsync_DistinguishesByStoreId()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var storeId = Guid.NewGuid();
        var onlineUser = Guid.NewGuid();
        var storeUser = Guid.NewGuid();

        await sut.CreateWatchAsync(new CreateWatchRequest(onlineUser, "1234567890123", "M", null));
        await sut.CreateWatchAsync(new CreateWatchRequest(storeUser, "1234567890123", "M", storeId));

        var onlineWatchers = await sut.GetWatcherUserIdsAsync("1234567890123", "M", null);
        var storeWatchers = await sut.GetWatcherUserIdsAsync("1234567890123", "M", storeId);

        onlineWatchers.Should().BeEquivalentTo(new[] { onlineUser });
        storeWatchers.Should().BeEquivalentTo(new[] { storeUser });
    }
}
