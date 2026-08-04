using Microsoft.EntityFrameworkCore;
using StockTracker.Subscription.Data;
using StockTracker.Subscription.DTOs;
using StockTracker.Subscription.Entities;

namespace StockTracker.Subscription.Services;

public interface IWatchService
{
    Task<CreateWatchResult> CreateWatchAsync(CreateWatchRequest request);
    Task<List<WatchDto>> GetWatchesAsync(Guid userId);
    Task<bool> DeleteWatchAsync(Guid userWatchId, Guid userId);
    Task<List<Guid>> GetWatcherUserIdsAsync(string productCode, string size, Guid? storeId);
}

public class WatchService : IWatchService
{
    private readonly SubscriptionDbContext _db;
    private readonly IBillingServiceClient _billingClient;
    private readonly ILogger<WatchService> _logger;

    public WatchService(SubscriptionDbContext db, IBillingServiceClient billingClient, ILogger<WatchService> logger)
    {
        _db = db;
        _billingClient = billingClient;
        _logger = logger;
    }

    public async Task<CreateWatchResult> CreateWatchAsync(CreateWatchRequest request)
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

        // Kullanıcı bu WatchGroup'u zaten takip ediyor — yeni bir "slot" tüketilmiyor, limit kontrolüne
        // gerek yok (aksi halde kullanıcı zaten sahip olduğu bir takibi tekrar isteyince limite takılırdı).
        if (existingUserWatch is not null)
            return new CreateWatchResult(true, ToDto(existingUserWatch, watchGroup), null, null);

        // Faz 4.3 — Billing Service'e sorup kullanıcının plan limitini aşıp aşmadığını kontrol et.
        // Billing Service'e ulaşılamazsa (ağ hatası, geçici kesinti) FAIL-OPEN: takip oluşturmaya izin
        // verilir, uyarı loglanır. Gerekçe: bu bir MVP ödeme-uygulaması değil, "kullanıcı ürün takip
        // edemiyor" hatası "limit gerçekte aşılmadan biri fazladan ürün takip etti" riskinden çok daha
        // kötü bir kullanıcı deneyimi — bkz. .claude/ARCHITECTURE.md > Subscription > Limit Kontrolü.
        var currentWatchCount = await _db.UserWatches.CountAsync(uw => uw.UserId == request.UserId);
        UserLimitsResponse? limits = null;
        try
        {
            limits = await _billingClient.GetUserLimitsAsync(request.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Billing Service'e limit sorgusu başarısız — fail-open, takibe izin veriliyor.");
        }

        if (limits is not null && currentWatchCount >= limits.MaxTrackedProducts)
        {
            return new CreateWatchResult(false, null, "WATCH_LIMIT_EXCEEDED",
                $"'{limits.PlanName}' planınızın izin verdiği en fazla {limits.MaxTrackedProducts} ürün takip limitine ulaştınız. Daha fazla ürün takip etmek için planınızı yükseltin.");
        }

        var userWatch = new UserWatch { UserId = request.UserId, WatchGroupId = watchGroup.Id };
        _db.UserWatches.Add(userWatch);
        await _db.SaveChangesAsync();

        return new CreateWatchResult(true, ToDto(userWatch, watchGroup), null, null);
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

    // Faz 3.3 — Notification Service'in "bu ürün/beden/mağazayı kimler takip ediyor" sorusuna cevap vermesi için.
    public async Task<List<Guid>> GetWatcherUserIdsAsync(string productCode, string size, Guid? storeId)
    {
        var watchGroup = await _db.WatchGroups.FirstOrDefaultAsync(w =>
            w.ProductCode == productCode && w.Size == size && w.StoreId == storeId);

        if (watchGroup is null)
            return new List<Guid>();

        return await _db.UserWatches
            .Where(uw => uw.WatchGroupId == watchGroup.Id)
            .Select(uw => uw.UserId)
            .ToListAsync();
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
