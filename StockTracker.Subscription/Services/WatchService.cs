using Microsoft.EntityFrameworkCore;
using StockTracker.Subscription.Data;
using StockTracker.Subscription.DTOs;
using StockTracker.Subscription.Entities;

namespace StockTracker.Subscription.Services;

public interface IWatchService
{
    Task<WatchDto> CreateWatchAsync(CreateWatchRequest request);
    Task<List<WatchDto>> GetWatchesAsync(Guid userId);
    Task<bool> DeleteWatchAsync(Guid userWatchId, Guid userId);
}

public class WatchService : IWatchService
{
    private readonly SubscriptionDbContext _db;

    public WatchService(SubscriptionDbContext db)
    {
        _db = db;
    }

    public async Task<WatchDto> CreateWatchAsync(CreateWatchRequest request)
    {
        var watchGroup = await _db.WatchGroups.FirstOrDefaultAsync(w =>
            w.ProductCode == request.ProductCode &&
            w.Size == request.Size &&
            w.StoreId == request.StoreId);

        if (watchGroup is null)
        {
            watchGroup = new WatchGroup
            {
                ProductCode = request.ProductCode,
                Size = request.Size,
                StoreId = request.StoreId
            };
            _db.WatchGroups.Add(watchGroup);
            await _db.SaveChangesAsync();
        }

        var existingUserWatch = await _db.UserWatches.FirstOrDefaultAsync(uw =>
            uw.UserId == request.UserId && uw.WatchGroupId == watchGroup.Id);

        if (existingUserWatch is not null)
            return ToDto(existingUserWatch, watchGroup);

        var userWatch = new UserWatch { UserId = request.UserId, WatchGroupId = watchGroup.Id };
        _db.UserWatches.Add(userWatch);
        await _db.SaveChangesAsync();

        return ToDto(userWatch, watchGroup);
    }

    public async Task<List<WatchDto>> GetWatchesAsync(Guid userId)
    {
        return await _db.UserWatches
            .Where(uw => uw.UserId == userId)
            .OrderByDescending(uw => uw.CreatedAt)
            .Join(_db.WatchGroups, uw => uw.WatchGroupId, wg => wg.Id, (uw, wg) => new WatchDto(
                uw.Id,
                wg.Id,
                wg.ProductCode,
                wg.Size,
                wg.StoreId,
                wg.LastKnownStatus,
                wg.LastCheckedAt,
                uw.CreatedAt))
            .ToListAsync();
    }

    public async Task<bool> DeleteWatchAsync(Guid userWatchId, Guid userId)
    {
        var userWatch = await _db.UserWatches.FirstOrDefaultAsync(uw =>
            uw.Id == userWatchId && uw.UserId == userId);

        if (userWatch is null)
            return false;

        _db.UserWatches.Remove(userWatch);
        await _db.SaveChangesAsync();
        return true;
    }

    private static WatchDto ToDto(UserWatch userWatch, WatchGroup watchGroup) => new(
        userWatch.Id,
        watchGroup.Id,
        watchGroup.ProductCode,
        watchGroup.Size,
        watchGroup.StoreId,
        watchGroup.LastKnownStatus,
        watchGroup.LastCheckedAt,
        userWatch.CreatedAt);
}
