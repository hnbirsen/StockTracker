namespace StockTracker.Shared.Scraping.Health;

public record ScraperHealthStats(
    string ScraperName,
    int SampleSize,
    double SuccessRatePercent,
    IReadOnlyDictionary<string, int> HttpStatusCodeDistribution,
    bool AlertTriggered);
