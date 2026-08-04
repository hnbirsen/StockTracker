using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Notification.Data;
using StockTracker.Notification.Entities;
using StockTracker.Notification.Services;
using StockTracker.Shared.Contracts.Messages.V1;

namespace StockTracker.Notification.Tests;

public class NotificationProcessingServiceTests
{
    private readonly Mock<ISubscriptionServiceClient> _subscriptionClient = new();
    private readonly Mock<IIdentityServiceClient> _identityClient = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IPushSender> _pushSender = new();
    private readonly Mock<IUserDeviceTokenProvider> _deviceTokenProvider = new();

    private static NotificationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private NotificationProcessingService CreateSut(NotificationDbContext db)
    {
        _deviceTokenProvider.Setup(p => p.GetDeviceTokenAsync(It.IsAny<Guid>())).ReturnsAsync((string?)null);
        _emailSender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        return new NotificationProcessingService(
            db,
            _subscriptionClient.Object,
            _identityClient.Object,
            _emailSender.Object,
            _pushSender.Object,
            _deviceTokenProvider.Object,
            Mock.Of<ILogger<NotificationProcessingService>>());
    }

    private static StockResultEvent CreateEvent(StockStatus status, Guid? commandId = null) => new(
        commandId ?? Guid.NewGuid(), "111", Guid.NewGuid(), "M", null, status, DateTime.UtcNow, "test");

    [Fact]
    public async Task ProcessAsync_WhenFirstEverCheckIsInStock_DoesNotNotify_BecausePreviousStatusWasUnknown()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        await sut.ProcessAsync(CreateEvent(StockStatus.InStock), CancellationToken.None);

        _subscriptionClient.Verify(c => c.GetWatcherUserIdsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        db.WatchGroupNotificationStates.Should().ContainSingle(s => s.LastKnownStatus == StockStatus.InStock);
    }

    [Fact]
    public async Task ProcessAsync_WhenStaysOutOfStock_DoesNotNotify()
    {
        await using var db = CreateDbContext();
        db.WatchGroupNotificationStates.Add(new WatchGroupNotificationState
        {
            ProductCode = "111", Size = "M", StoreId = null, LastKnownStatus = StockStatus.OutOfStock
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.ProcessAsync(CreateEvent(StockStatus.OutOfStock), CancellationToken.None);

        _subscriptionClient.Verify(c => c.GetWatcherUserIdsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenTransitionsFromInStockToOutOfStock_DoesNotNotify()
    {
        await using var db = CreateDbContext();
        db.WatchGroupNotificationStates.Add(new WatchGroupNotificationState
        {
            ProductCode = "111", Size = "M", StoreId = null, LastKnownStatus = StockStatus.InStock
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.ProcessAsync(CreateEvent(StockStatus.OutOfStock), CancellationToken.None);

        _subscriptionClient.Verify(c => c.GetWatcherUserIdsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        db.WatchGroupNotificationStates.Should().ContainSingle(s => s.LastKnownStatus == StockStatus.OutOfStock);
    }

    [Fact]
    public async Task ProcessAsync_WhenRestockDetectedButNoWatchers_DoesNotCallSenders()
    {
        await using var db = CreateDbContext();
        db.WatchGroupNotificationStates.Add(new WatchGroupNotificationState
        {
            ProductCode = "111", Size = "M", StoreId = null, LastKnownStatus = StockStatus.OutOfStock
        });
        await db.SaveChangesAsync();

        _subscriptionClient
            .Setup(c => c.GetWatcherUserIdsAsync("111", "M", null))
            .ReturnsAsync(new List<Guid>());

        var sut = CreateSut(db);
        await sut.ProcessAsync(CreateEvent(StockStatus.InStock), CancellationToken.None);

        _identityClient.Verify(c => c.GetUserEmailAsync(It.IsAny<Guid>()), Times.Never);
        _emailSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenRestockDetected_SendsEmailToEachWatcherAndLogsSuccess()
    {
        await using var db = CreateDbContext();
        db.WatchGroupNotificationStates.Add(new WatchGroupNotificationState
        {
            ProductCode = "111", Size = "M", StoreId = null, LastKnownStatus = StockStatus.OutOfStock
        });
        await db.SaveChangesAsync();

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        _subscriptionClient
            .Setup(c => c.GetWatcherUserIdsAsync("111", "M", null))
            .ReturnsAsync(new List<Guid> { userA, userB });
        _identityClient.Setup(c => c.GetUserEmailAsync(userA)).ReturnsAsync("a@example.com");
        _identityClient.Setup(c => c.GetUserEmailAsync(userB)).ReturnsAsync("b@example.com");

        var sut = CreateSut(db);
        var evt = CreateEvent(StockStatus.InStock);
        await sut.ProcessAsync(evt, CancellationToken.None);

        _emailSender.Verify(s => s.SendAsync("a@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(s => s.SendAsync("b@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        var logs = await db.NotificationLogs.ToListAsync();
        logs.Should().HaveCount(2);
        logs.Should().OnlyContain(l => l.Channel == NotificationChannel.Email && l.Success && l.CommandId == evt.CommandId);
    }

    [Fact]
    public async Task ProcessAsync_WhenUserHasNoDeviceToken_SkipsPushWithoutCallingSender()
    {
        await using var db = CreateDbContext();
        db.WatchGroupNotificationStates.Add(new WatchGroupNotificationState
        {
            ProductCode = "111", Size = "M", StoreId = null, LastKnownStatus = StockStatus.OutOfStock
        });
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        _subscriptionClient.Setup(c => c.GetWatcherUserIdsAsync("111", "M", null)).ReturnsAsync(new List<Guid> { userId });
        _identityClient.Setup(c => c.GetUserEmailAsync(userId)).ReturnsAsync("user@example.com");

        var sut = CreateSut(db);
        await sut.ProcessAsync(CreateEvent(StockStatus.InStock), CancellationToken.None);

        _pushSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        (await db.NotificationLogs.ToListAsync()).Should().OnlyContain(l => l.Channel == NotificationChannel.Email);
    }

    [Fact]
    public async Task ProcessAsync_WhenSameCommandProcessedTwice_DoesNotSendDuplicateEmail()
    {
        await using var db = CreateDbContext();
        db.WatchGroupNotificationStates.Add(new WatchGroupNotificationState
        {
            ProductCode = "111", Size = "M", StoreId = null, LastKnownStatus = StockStatus.OutOfStock
        });
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        _subscriptionClient.Setup(c => c.GetWatcherUserIdsAsync("111", "M", null)).ReturnsAsync(new List<Guid> { userId });
        _identityClient.Setup(c => c.GetUserEmailAsync(userId)).ReturnsAsync("user@example.com");

        var evt = CreateEvent(StockStatus.InStock);
        var sut = CreateSut(db);
        await sut.ProcessAsync(evt, CancellationToken.None);

        // Aynı CommandId ile ikinci kez tüketim (MassTransit at-least-once teslimat senaryosu) — durum zaten
        // InStock'a güncellendiği için "restock" tespiti tekrar tetiklenmeyecek (previousStatus artık InStock),
        // ama idempotency guard'ı ayrıca da doğrulamak için NotificationLog kaydı zaten var olduğu senaryoyu
        // izole test ediyoruz: state'i elle tekrar OutOfStock'a çekip aynı event'i tekrar işletiyoruz.
        var state = await db.WatchGroupNotificationStates.FirstAsync();
        state.LastKnownStatus = StockStatus.OutOfStock;
        await db.SaveChangesAsync();

        await sut.ProcessAsync(evt, CancellationToken.None);

        _emailSender.Verify(s => s.SendAsync("user@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        (await db.NotificationLogs.CountAsync()).Should().Be(1);
    }
}
