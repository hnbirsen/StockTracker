using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTracker.Shared.Contracts.Messaging;
using CheckStockCommandV2 = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;
using StockStatus = StockTracker.Shared.Contracts.Messages.V1.StockStatus;
using StockTracker.Subscription.Configuration;
using StockTracker.Subscription.Data;
using StockTracker.Subscription.Entities;

namespace StockTracker.Subscription.Services;

public interface IStockPollerService
{
    Task RunPollCycleAsync(CancellationToken cancellationToken);
}

// Faz 3.2 — periyodik olarak henüz "stokta" olduğu doğrulanmamış (OutOfStock/Unknown/hiç kontrol edilmemiş)
// WatchGroup'ları tekrar kontrole gönderir. Roadmap'te "LastKnownStatus = OutOfStock olan WatchGroup'lar"
// deniyor, ama kapsam bilinçli olarak genişletildi (bkz. .claude/ARCHITECTURE.md — Stock Poller): yeni
// oluşturulan bir WatchGroup'un LastKnownStatus'u null'dur (henüz hiç kontrol edilmedi), sadece OutOfStock'a
// bakılsaydı bu grup asla kontrol edilmezdi. InStock olan gruplar atlanır — kullanıcı zaten haberdar edilmiş
// olur (Faz 3.3), tekrar kontrol gereksizdir.
public class StockPollerService : IStockPollerService
{
    private readonly SubscriptionDbContext _db;
    private readonly IProductServiceClient _productClient;
    private readonly IStoreReferenceServiceClient _storeClient;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly PollerSettings _settings;
    private readonly ILogger<StockPollerService> _logger;

    public StockPollerService(
        SubscriptionDbContext db,
        IProductServiceClient productClient,
        IStoreReferenceServiceClient storeClient,
        ISendEndpointProvider sendEndpointProvider,
        IOptions<PollerSettings> settings,
        ILogger<StockPollerService> logger)
    {
        _db = db;
        _productClient = productClient;
        _storeClient = storeClient;
        _sendEndpointProvider = sendEndpointProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task RunPollCycleAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var candidates = await _db.WatchGroups
            .Where(w => w.LastKnownStatus == null || w.LastKnownStatus != StockStatus.InStock)
            .Select(w => new
            {
                WatchGroup = w,
                WatcherCount = _db.UserWatches.Count(uw => uw.WatchGroupId == w.Id)
            })
            .ToListAsync(cancellationToken);

        var dueCount = 0;
        foreach (var candidate in candidates)
        {
            var interval = ResolveCheckInterval(candidate.WatcherCount);
            if (!IsDue(candidate.WatchGroup.LastCheckedAt, interval, now))
                continue;

            dueCount++;
            var published = await TryPublishCheckStockCommandAsync(candidate.WatchGroup, cancellationToken);
            if (published)
                candidate.WatchGroup.LastCheckedAt = now;
        }

        if (dueCount > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stock poller döngüsü tamamlandı — aday: {CandidateCount}, kontrol zamanı gelen: {DueCount}",
            candidates.Count, dueCount);
    }

    public TimeSpan ResolveCheckInterval(int watcherCount) => watcherCount switch
    {
        var c when c >= _settings.HighPriorityWatcherThreshold => TimeSpan.FromMinutes(_settings.HighPriorityCheckIntervalMinutes),
        var c when c >= _settings.MediumPriorityWatcherThreshold => TimeSpan.FromMinutes(_settings.MediumPriorityCheckIntervalMinutes),
        _ => TimeSpan.FromMinutes(_settings.LowPriorityCheckIntervalMinutes)
    };

    public static bool IsDue(DateTime? lastCheckedAt, TimeSpan interval, DateTime now) =>
        lastCheckedAt is null || now - lastCheckedAt.Value >= interval;

    private async Task<bool> TryPublishCheckStockCommandAsync(WatchGroup watchGroup, CancellationToken cancellationToken)
    {
        var lookup = await _productClient.LookupAsync(watchGroup.ProductCode);
        if (lookup is null || !lookup.IsResolved || lookup.BrandId is null || lookup.ScraperQueueName is null)
        {
            _logger.LogWarning(
                "Poller — WatchGroup {WatchGroupId} için marka çözülemedi ({ProductCode}), bu döngüde atlanıyor.",
                watchGroup.Id, watchGroup.ProductCode);
            return false;
        }

        string? brandSpecificStoreId = null;
        if (watchGroup.StoreId is { } storeId)
        {
            var store = await _storeClient.GetStoreByIdAsync(storeId);
            brandSpecificStoreId = store?.BrandSpecificStoreId;
        }

        var queueName = QueueNaming.StockCheckQueue(lookup.ScraperQueueName);
        var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{queueName}"));

        await sendEndpoint.Send(new CheckStockCommandV2(
            CommandId: Guid.NewGuid(),
            ProductCode: watchGroup.ProductCode,
            BrandId: lookup.BrandId.Value,
            BrandName: lookup.BrandName!,
            Size: watchGroup.Size,
            StoreId: watchGroup.StoreId,
            BrandSpecificStoreId: brandSpecificStoreId,
            City: null,
            District: null,
            ProductUrl: lookup.ProductUrl,
            RequestedAt: DateTime.UtcNow
        ), cancellationToken);

        _logger.LogInformation(
            "Poller — CheckStockCommand gönderildi, WatchGroupId: {WatchGroupId}, queue: {Queue}",
            watchGroup.Id, queueName);

        return true;
    }
}
