using Microsoft.EntityFrameworkCore;
using StockTracker.Shared.Contracts.Messages.V1;
using StockTracker.Subscription.Data;

namespace StockTracker.Subscription.Services;

public interface IWatchGroupStatusUpdater
{
    Task UpdateFromStockResultAsync(StockResultEvent stockResultEvent, CancellationToken cancellationToken);
}

// StockResultEvent (scraper'ların yayınladığı sonuç, fanout) tüketilip ilgili WatchGroup'un
// LastKnownStatus/LastCheckedAt'i güncellenir — hem kullanıcının POST /watches ile ilk oluşturduğu
// (henüz hiç kontrol edilmemiş, LastKnownStatus=null) hem de Stock Poller'ın (Faz 3.2) tetiklediği
// tekrar kontrollerin sonucu buradan işlenir. Bu, Stock Poller'ın OutOfStock filtresinin zamanla
// gerçek verilerle beslenmesini sağlayan tek yol.
public class WatchGroupStatusUpdater : IWatchGroupStatusUpdater
{
    private readonly SubscriptionDbContext _db;
    private readonly ILogger<WatchGroupStatusUpdater> _logger;

    public WatchGroupStatusUpdater(SubscriptionDbContext db, ILogger<WatchGroupStatusUpdater> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task UpdateFromStockResultAsync(StockResultEvent stockResultEvent, CancellationToken cancellationToken)
    {
        var watchGroup = await _db.WatchGroups.FirstOrDefaultAsync(w =>
            w.ProductCode == stockResultEvent.ProductCode &&
            w.Size == stockResultEvent.Size &&
            w.StoreId == stockResultEvent.StoreId,
            cancellationToken);

        if (watchGroup is null)
        {
            // Bu ürün+beden+mağaza kombinasyonunu takip eden kimse yok — görmezden gelinir
            // (ör. Search Orchestrator'ın ilk anlık aramasından gelen sonuç, henüz bir Watch'a bağlanmamış).
            return;
        }

        watchGroup.LastKnownStatus = stockResultEvent.Status;
        watchGroup.LastCheckedAt = stockResultEvent.CheckedAt;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "WatchGroup {WatchGroupId} güncellendi — Status: {Status}",
            watchGroup.Id, stockResultEvent.Status);
    }
}
