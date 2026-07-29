namespace StockTracker.Shared.Scraping.Health;

// Context: hatanın hangi istek üzerinde oluştuğunu insan tarafından okunabilir şekilde taşır
// (ör. PlaywrightPdp için productUrl; StockApi için "productUrl | store={id} partNumber={digits}").
// Redis'te ham JSON olarak durduğu için `redis-cli LRANGE` ile bakıldığında da doğrudan anlamlı olsun diye.
public record ScraperHealthLogEntry(
    string Source,
    bool Success,
    int? HttpStatusCode,
    string? ErrorMessage,
    string? Context,
    int DurationMs,
    DateTime Timestamp);
