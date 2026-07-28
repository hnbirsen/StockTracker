using StackExchange.Redis;

namespace StockTracker.Product.Services;

public interface ICacheMetricsService
{
    Task RecordHitAsync(string key);
    Task RecordMissAsync(string key);
    Task<CacheMetricsSummary> GetSummaryAsync();
}

public record CacheMetricsSummary(long TotalHits, long TotalMisses, double HitRatePercent);

public class CacheMetricsService : ICacheMetricsService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CacheMetricsService> _logger;

    private const string HitKey = "cache:metrics:hits";
    private const string MissKey = "cache:metrics:misses";

    public CacheMetricsService(IConnectionMultiplexer redis, ILogger<CacheMetricsService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task RecordHitAsync(string key)
    {
        var db = _redis.GetDatabase();
        var hits = await db.StringIncrementAsync(HitKey);
        _logger.LogDebug("Cache HIT — key: {Key} | total hits: {Hits}", key, hits);
    }

    public async Task RecordMissAsync(string key)
    {
        var db = _redis.GetDatabase();
        var misses = await db.StringIncrementAsync(MissKey);
        _logger.LogDebug("Cache MISS — key: {Key} | total misses: {Misses}", key, misses);
    }

    public async Task<CacheMetricsSummary> GetSummaryAsync()
    {
        var db = _redis.GetDatabase();

        var hitsVal = await db.StringGetAsync(HitKey);
        var missesVal = await db.StringGetAsync(MissKey);

        var hits = hitsVal.HasValue ? (long)hitsVal : 0;
        var misses = missesVal.HasValue ? (long)missesVal : 0;
        var total = hits + misses;
        var hitRate = total == 0 ? 0.0 : Math.Round((double)hits / total * 100, 2);

        return new CacheMetricsSummary(hits, misses, hitRate);
    }
}
