using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Shared.Contracts.Messages.V1;
using StockTracker.Subscription.Data;
using StockTracker.Subscription.Entities;
using StockTracker.Subscription.Services;

namespace StockTracker.Subscription.Tests;

public class WatchGroupStatusUpdaterTests
{
    private static SubscriptionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SubscriptionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static WatchGroupStatusUpdater CreateSut(SubscriptionDbContext db) =>
        new(db, Mock.Of<ILogger<WatchGroupStatusUpdater>>());

    [Fact]
    public async Task UpdateFromStockResultAsync_WhenMatchingWatchGroupExists_UpdatesStatusAndCheckedAt()
    {
        await using var db = CreateDbContext();
        var watchGroup = new WatchGroup { ProductCode = "111", Size = "M", StoreId = null };
        db.WatchGroups.Add(watchGroup);
        await db.SaveChangesAsync();

        var checkedAt = DateTime.UtcNow;
        var evt = new StockResultEvent(Guid.NewGuid(), "111", Guid.NewGuid(), "M", null, StockStatus.InStock, checkedAt, "test");

        var sut = CreateSut(db);
        await sut.UpdateFromStockResultAsync(evt, CancellationToken.None);

        var updated = await db.WatchGroups.FirstAsync(w => w.Id == watchGroup.Id);
        updated.LastKnownStatus.Should().Be(StockStatus.InStock);
        updated.LastCheckedAt.Should().Be(checkedAt);
    }

    [Fact]
    public async Task UpdateFromStockResultAsync_WhenNoMatchingWatchGroup_DoesNothing()
    {
        await using var db = CreateDbContext();
        var evt = new StockResultEvent(Guid.NewGuid(), "does-not-exist", Guid.NewGuid(), "M", null, StockStatus.InStock, DateTime.UtcNow, "test");

        var sut = CreateSut(db);
        var act = async () => await sut.UpdateFromStockResultAsync(evt, CancellationToken.None);

        await act.Should().NotThrowAsync();
        db.WatchGroups.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateFromStockResultAsync_MatchesByProductCodeSizeAndStoreId_NotJustProductCode()
    {
        await using var db = CreateDbContext();
        var storeId = Guid.NewGuid();
        var onlineGroup = new WatchGroup { ProductCode = "111", Size = "M", StoreId = null };
        var storeGroup = new WatchGroup { ProductCode = "111", Size = "M", StoreId = storeId };
        db.WatchGroups.AddRange(onlineGroup, storeGroup);
        await db.SaveChangesAsync();

        var evt = new StockResultEvent(Guid.NewGuid(), "111", Guid.NewGuid(), "M", storeId, StockStatus.OutOfStock, DateTime.UtcNow, "test");

        var sut = CreateSut(db);
        await sut.UpdateFromStockResultAsync(evt, CancellationToken.None);

        var updatedOnline = await db.WatchGroups.FirstAsync(w => w.Id == onlineGroup.Id);
        var updatedStore = await db.WatchGroups.FirstAsync(w => w.Id == storeGroup.Id);

        updatedOnline.LastKnownStatus.Should().BeNull();
        updatedStore.LastKnownStatus.Should().Be(StockStatus.OutOfStock);
    }
}
