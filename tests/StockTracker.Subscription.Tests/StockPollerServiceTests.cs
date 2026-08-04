using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StockTracker.Shared.Contracts.Messages.V1;
using StockTracker.Subscription.Configuration;
using StockTracker.Subscription.Data;
using StockTracker.Subscription.DTOs;
using StockTracker.Subscription.Entities;
using StockTracker.Subscription.Services;
using CheckStockCommandV2 = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.Subscription.Tests;

public class StockPollerServiceTests
{
    private readonly Mock<IProductServiceClient> _productClient = new();
    private readonly Mock<IStoreReferenceServiceClient> _storeClient = new();
    private readonly Mock<ISendEndpointProvider> _sendEndpointProvider = new();
    private readonly Mock<ISendEndpoint> _sendEndpoint = new();

    private static SubscriptionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SubscriptionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private StockPollerService CreateSut(SubscriptionDbContext db, PollerSettings? settings = null)
    {
        _sendEndpointProvider
            .Setup(p => p.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(_sendEndpoint.Object);

        return new StockPollerService(
            db,
            _productClient.Object,
            _storeClient.Object,
            _sendEndpointProvider.Object,
            Options.Create(settings ?? new PollerSettings()),
            Mock.Of<ILogger<StockPollerService>>());
    }

    [Theory]
    [InlineData(6, 5)] // yüksek öncelik eşiği (5) sonrası
    [InlineData(2, 15)] // orta öncelik eşiği (2) sonrası
    [InlineData(1, 60)] // eşiklerin altı: düşük öncelik
    public void ResolveCheckInterval_ReturnsIntervalMatchingWatcherTier(int watcherCount, int expectedMinutes)
    {
        using var db = CreateDbContext();
        var sut = CreateSut(db);

        var interval = sut.ResolveCheckInterval(watcherCount);

        interval.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void IsDue_WhenNeverChecked_ReturnsTrue()
    {
        StockPollerService.IsDue(null, TimeSpan.FromMinutes(5), DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsDue_WhenIntervalNotYetElapsed_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        StockPollerService.IsDue(now.AddMinutes(-2), TimeSpan.FromMinutes(5), now).Should().BeFalse();
    }

    [Fact]
    public void IsDue_WhenIntervalElapsed_ReturnsTrue()
    {
        var now = DateTime.UtcNow;
        StockPollerService.IsDue(now.AddMinutes(-6), TimeSpan.FromMinutes(5), now).Should().BeTrue();
    }

    [Fact]
    public async Task RunPollCycleAsync_SkipsWatchGroupsThatAreConfirmedInStock()
    {
        await using var db = CreateDbContext();
        db.WatchGroups.Add(new WatchGroup { ProductCode = "111", Size = "M", LastKnownStatus = StockStatus.InStock });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.RunPollCycleAsync(CancellationToken.None);

        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(It.IsAny<Uri>()), Times.Never);
    }

    [Fact]
    public async Task RunPollCycleAsync_PicksUpNeverCheckedWatchGroups()
    {
        await using var db = CreateDbContext();
        var watchGroup = new WatchGroup { ProductCode = "111", Size = "M", LastKnownStatus = null };
        db.WatchGroups.Add(watchGroup);
        await db.SaveChangesAsync();

        _productClient
            .Setup(c => c.LookupAsync("111"))
            .ReturnsAsync(new ProductLookupResponse("111", true, Guid.NewGuid(), "Bershka", "bershka", "https://example.com/p"));

        var sut = CreateSut(db);
        await sut.RunPollCycleAsync(CancellationToken.None);

        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommandV2>(cmd => cmd.ProductCode == "111"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunPollCycleAsync_SkipsOutOfStockGroupWhenIntervalNotElapsed()
    {
        await using var db = CreateDbContext();
        db.WatchGroups.Add(new WatchGroup
        {
            ProductCode = "111",
            Size = "M",
            LastKnownStatus = StockStatus.OutOfStock,
            LastCheckedAt = DateTime.UtcNow.AddMinutes(-1) // düşük öncelik aralığı (60 dk) henüz dolmadı
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.RunPollCycleAsync(CancellationToken.None);

        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(It.IsAny<Uri>()), Times.Never);
    }

    [Fact]
    public async Task RunPollCycleAsync_WhenDue_PublishesAndUpdatesLastCheckedAt()
    {
        await using var db = CreateDbContext();
        var watchGroup = new WatchGroup
        {
            ProductCode = "111",
            Size = "M",
            LastKnownStatus = StockStatus.OutOfStock,
            LastCheckedAt = DateTime.UtcNow.AddHours(-2)
        };
        db.WatchGroups.Add(watchGroup);
        await db.SaveChangesAsync();

        _productClient
            .Setup(c => c.LookupAsync("111"))
            .ReturnsAsync(new ProductLookupResponse("111", true, Guid.NewGuid(), "Bershka", "bershka", "https://example.com/p"));

        var sut = CreateSut(db);
        var beforeRun = DateTime.UtcNow;
        await sut.RunPollCycleAsync(CancellationToken.None);

        var updated = await db.WatchGroups.FirstAsync(w => w.Id == watchGroup.Id);
        updated.LastCheckedAt.Should().BeOnOrAfter(beforeRun);

        _sendEndpoint.Verify(e => e.Send(It.IsAny<CheckStockCommandV2>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunPollCycleAsync_PrioritizesHighWatcherCountOverLowerInterval()
    {
        // 6 kullanıcı = yüksek öncelik (5 dk aralık); 10 dk önce kontrol edilmiş → tekrar kontrol zamanı gelmiş.
        await using var db = CreateDbContext();
        var watchGroup = new WatchGroup
        {
            ProductCode = "111",
            Size = "M",
            LastKnownStatus = StockStatus.OutOfStock,
            LastCheckedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        db.WatchGroups.Add(watchGroup);
        db.UserWatches.AddRange(Enumerable.Range(0, 6)
            .Select(_ => new UserWatch { UserId = Guid.NewGuid(), WatchGroupId = watchGroup.Id }));
        await db.SaveChangesAsync();

        _productClient
            .Setup(c => c.LookupAsync("111"))
            .ReturnsAsync(new ProductLookupResponse("111", true, Guid.NewGuid(), "Bershka", "bershka", "https://example.com/p"));

        var sut = CreateSut(db);
        await sut.RunPollCycleAsync(CancellationToken.None);

        _sendEndpoint.Verify(e => e.Send(It.IsAny<CheckStockCommandV2>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunPollCycleAsync_WhenStoreIdSet_ResolvesBrandSpecificStoreId()
    {
        await using var db = CreateDbContext();
        var storeId = Guid.NewGuid();
        var watchGroup = new WatchGroup { ProductCode = "111", Size = "M", StoreId = storeId };
        db.WatchGroups.Add(watchGroup);
        await db.SaveChangesAsync();

        _productClient
            .Setup(c => c.LookupAsync("111"))
            .ReturnsAsync(new ProductLookupResponse("111", true, Guid.NewGuid(), "Bershka", "bershka", "https://example.com/p"));

        _storeClient
            .Setup(c => c.GetStoreByIdAsync(storeId))
            .ReturnsAsync(new StoreDto(storeId, Guid.NewGuid(), "16884"));

        var sut = CreateSut(db);
        await sut.RunPollCycleAsync(CancellationToken.None);

        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommandV2>(cmd => cmd.StoreId == storeId && cmd.BrandSpecificStoreId == "16884"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunPollCycleAsync_WhenBrandUnresolved_SkipsWithoutPublishingOrUpdatingLastCheckedAt()
    {
        await using var db = CreateDbContext();
        var watchGroup = new WatchGroup { ProductCode = "111", Size = "M" };
        db.WatchGroups.Add(watchGroup);
        await db.SaveChangesAsync();

        _productClient
            .Setup(c => c.LookupAsync("111"))
            .ReturnsAsync(new ProductLookupResponse("111", false, null, null, null, null));

        var sut = CreateSut(db);
        await sut.RunPollCycleAsync(CancellationToken.None);

        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(It.IsAny<Uri>()), Times.Never);
        var updated = await db.WatchGroups.FirstAsync(w => w.Id == watchGroup.Id);
        updated.LastCheckedAt.Should().BeNull();
    }
}
