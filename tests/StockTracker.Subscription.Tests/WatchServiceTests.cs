using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTracker.Shared.Contracts.Messages.V1;
using StockTracker.Subscription.Data;
using StockTracker.Subscription.DTOs;
using StockTracker.Subscription.Entities;
using StockTracker.Subscription.Services;

namespace StockTracker.Subscription.Tests;

public class WatchServiceTests
{
    private static SubscriptionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SubscriptionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CreateWatchAsync_WhenNoExistingWatchGroup_CreatesNewGroupAndUserWatch()
    {
        await using var db = CreateDbContext();
        var sut = new WatchService(db);
        var userId = Guid.NewGuid();

        var result = await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1234567890123", "M", null));

        result.ProductCode.Should().Be("1234567890123");
        result.Size.Should().Be("M");
        result.StoreId.Should().BeNull();
        db.WatchGroups.Should().ContainSingle();
        db.UserWatches.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateWatchAsync_WhenTwoDifferentUsersWatchSameProductSizeStore_DedupsToSingleWatchGroup()
    {
        await using var db = CreateDbContext();
        var sut = new WatchService(db);
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
        var sut = new WatchService(db);
        var userId = Guid.NewGuid();
        var request = new CreateWatchRequest(userId, "1234567890123", "M", null);

        var first = await sut.CreateWatchAsync(request);
        var second = await sut.CreateWatchAsync(request);

        first.UserWatchId.Should().Be(second.UserWatchId);
        db.UserWatches.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateWatchAsync_WhenSameProductSizeButDifferentStore_CreatesSeparateWatchGroups()
    {
        await using var db = CreateDbContext();
        var sut = new WatchService(db);
        var userId = Guid.NewGuid();

        await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1234567890123", "M", Guid.NewGuid()));
        await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1234567890123", "M", Guid.NewGuid()));

        db.WatchGroups.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetWatchesAsync_ReturnsOnlyRequestedUsersWatches()
    {
        await using var db = CreateDbContext();
        var sut = new WatchService(db);
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

        var sut = new WatchService(db);
        var result = await sut.GetWatchesAsync(userId);

        result.Should().ContainSingle();
        result[0].LastKnownStatus.Should().Be(StockStatus.OutOfStock);
        result[0].LastCheckedAt.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task DeleteWatchAsync_WhenOwnedByUser_RemovesUserWatchOnly()
    {
        await using var db = CreateDbContext();
        var sut = new WatchService(db);
        var userId = Guid.NewGuid();
        var created = await sut.CreateWatchAsync(new CreateWatchRequest(userId, "1234567890123", "M", null));

        var deleted = await sut.DeleteWatchAsync(created.UserWatchId, userId);

        deleted.Should().BeTrue();
        db.UserWatches.Should().BeEmpty();
        db.WatchGroups.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteWatchAsync_WhenNotOwnedByUser_ReturnsFalseAndDoesNotDelete()
    {
        await using var db = CreateDbContext();
        var sut = new WatchService(db);
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var created = await sut.CreateWatchAsync(new CreateWatchRequest(owner, "1234567890123", "M", null));

        var deleted = await sut.DeleteWatchAsync(created.UserWatchId, intruder);

        deleted.Should().BeFalse();
        db.UserWatches.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteWatchAsync_WhenIdDoesNotExist_ReturnsFalse()
    {
        await using var db = CreateDbContext();
        var sut = new WatchService(db);

        var deleted = await sut.DeleteWatchAsync(Guid.NewGuid(), Guid.NewGuid());

        deleted.Should().BeFalse();
    }
}
