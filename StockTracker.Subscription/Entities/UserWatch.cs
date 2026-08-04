namespace StockTracker.Subscription.Entities;

// UserId + WatchGroupId'nin N:1 ayrımı — aynı ürün+beden+mağazayı takip eden birden fazla kullanıcı
// tek bir WatchGroup'a bağlanır, kullanıcı başına ayrı kayıt burada tutulur.
public class UserWatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid WatchGroupId { get; set; }
    public WatchGroup WatchGroup { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
