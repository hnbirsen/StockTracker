namespace StockTracker.Subscription.Configuration;

// Faz 3.2 — Stock Poller önceliklendirme/sıklık ayarları, appsettings.json > "Poller" bölümünden okunur.
// Çok kullanıcılı WatchGroup'lar daha sık, az kullanıcılı olanlar daha seyrek kontrol edilir.
public class PollerSettings
{
    public int IntervalSeconds { get; set; } = 60;

    public int HighPriorityWatcherThreshold { get; set; } = 5;
    public int MediumPriorityWatcherThreshold { get; set; } = 2;

    public int HighPriorityCheckIntervalMinutes { get; set; } = 5;
    public int MediumPriorityCheckIntervalMinutes { get; set; } = 15;
    public int LowPriorityCheckIntervalMinutes { get; set; } = 60;
}
