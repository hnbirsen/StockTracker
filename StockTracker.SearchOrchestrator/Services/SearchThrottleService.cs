using StackExchange.Redis;

namespace StockTracker.SearchOrchestrator.Services;

public interface ISearchThrottleService
{
    Task<bool> TryAcquireAsync(Guid userId, string productCode, string size);
}

// Aynı kullanıcının aynı ürün/beden için art arda arama isteği göndermesini engeller —
// her arama bir RabbitMQ mesajı tetiklediği için tekrar tıklama scraper'ları gereksiz yere meşgul eder.
public class SearchThrottleService : ISearchThrottleService
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(30);
    private readonly IConnectionMultiplexer _redis;

    public SearchThrottleService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<bool> TryAcquireAsync(Guid userId, string productCode, string size)
    {
        var db = _redis.GetDatabase();
        var key = $"search:throttle:{userId}:{productCode}:{size}";
        return await db.StringSetAsync(key, "1", ThrottleWindow, When.NotExists);
    }
}
