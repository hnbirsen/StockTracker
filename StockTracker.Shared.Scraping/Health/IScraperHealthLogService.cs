namespace StockTracker.Shared.Scraping.Health;

// Marka scraper'ları arasında paylaşılan sağlık/izlenebilirlik katmanı (bkz. .claude/ROADMAP.md Faz 2.5,
// .claude/ARCHITECTURE.md > Ölçeklenme Riski). Her scraper kendi Postgres veritabanını AÇMAK yerine
// (bu veri domain verisi değil, tüm scraper'larda birebir aynı şekilde tekrarlanacak operasyonel
// telemetri olduğu için) zaten paylaşılan tek Redis instance'ını, scraper adına göre namespace'lenmiş
// key'lerle kullanır — Search Orchestrator throttle ve Bershka PDP cache'i de aynı gerekçeyle Redis'te.
public interface IScraperHealthLogService
{
    // Tek bir deneme (Playwright PDP çekimi, stok API çağrısı, vb.) kaydedilir. Asla exception fırlatmaz —
    // sağlık loglaması scraper'ın asıl iş akışını (stok kontrolü) hiçbir koşulda kesintiye uğratmamalı.
    // `context`: hangi istek üzerinde olunduğunu insan tarafından okunabilir şekilde taşır (ör. productUrl,
    // ya da productUrl + mağaza/partnumber bilgisi) — hata ayıklarken "hangi URL'de hata alındı" sorusuna
    // Redis'teki ham kaydı okuyarak bile cevap verebilmek için.
    Task LogAttemptAsync(
        string scraperName,
        string source,
        bool success,
        int? httpStatusCode,
        string? errorMessage,
        string? context,
        int durationMs,
        CancellationToken cancellationToken = default);

    Task<ScraperHealthStats> GetStatsAsync(
        string scraperName,
        int lastN = 100,
        CancellationToken cancellationToken = default);

    // Son N denemeden yalnızca BAŞARISIZ olanları, tam bağlamıyla (Context, ErrorMessage) döner —
    // "hangi ürün/mağazada hata alındı" sorusuna Redis'e elle bakmadan cevap vermek için.
    Task<IReadOnlyList<ScraperHealthLogEntry>> GetRecentFailuresAsync(
        string scraperName,
        int lastN = 20,
        CancellationToken cancellationToken = default);
}
