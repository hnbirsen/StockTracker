using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace StockTracker.Shared.Scraping.Health;

public class ScraperHealthLogService : IScraperHealthLogService
{
    // Her scraper için son N denemeyi tutan capped-list — büyümesin diye her yazımda bu sınıra trim edilir.
    private const int MaxEntriesPerScraper = 500;

    // Örneklem çok küçükken (soğuk başlangıç, ilk birkaç deneme) tek bir hata "başarı oranı çöktü"
    // alarmını tetiklememeli — en az bu kadar örnek birikmeden alarm değerlendirilmez.
    private const int MinSampleSizeForAlert = 10;
    private const double AlertThresholdPercent = 70.0;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ScraperHealthLogService> _logger;

    public ScraperHealthLogService(IConnectionMultiplexer redis, ILogger<ScraperHealthLogService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task LogAttemptAsync(
        string scraperName,
        string source,
        bool success,
        int? httpStatusCode,
        string? errorMessage,
        string? context,
        int durationMs,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = new ScraperHealthLogEntry(source, success, httpStatusCode, NormalizeToSingleLine(errorMessage), context, durationMs, DateTime.UtcNow);
            var json = JsonSerializer.Serialize(entry, JsonOptions);

            var db = _redis.GetDatabase();
            var key = LogKey(scraperName);

            // LPUSH: en yeni deneme her zaman başta (index 0) — LRANGE 0..N-1 doğrudan "son N deneme" verir.
            await db.ListLeftPushAsync(key, json);
            await db.ListTrimAsync(key, 0, MaxEntriesPerScraper - 1);
        }
        catch (Exception ex)
        {
            // Sağlık loglaması ASLA scraper'ın asıl iş akışını kesintiye uğratmamalı — Redis'e erişilemese
            // bile stok kontrolü sonucu kullanıcıya dönmeye devam etmeli.
            _logger.LogWarning(ex, "Scraper sağlık logu yazılamadı (scraper: {ScraperName}, source: {Source}).", scraperName, source);
        }
    }

    public async Task<ScraperHealthStats> GetStatsAsync(
        string scraperName,
        int lastN = 100,
        CancellationToken cancellationToken = default)
    {
        var entries = await ReadEntriesAsync(scraperName, lastN);
        var total = entries.Count;
        var successCount = entries.Count(e => e.Success);
        var successRate = total == 0 ? 0.0 : Math.Round((double)successCount / total * 100, 2);

        var distribution = entries
            .GroupBy(e => e.HttpStatusCode?.ToString() ?? "none")
            .ToDictionary(g => g.Key, g => g.Count());

        var alertTriggered = total >= MinSampleSizeForAlert && successRate < AlertThresholdPercent;
        if (alertTriggered)
        {
            _logger.LogWarning(
                "Scraper sağlık alarmı — {ScraperName}: son {Total} denemede başarı oranı %{SuccessRate} (eşik: %{Threshold}).",
                scraperName, total, successRate, AlertThresholdPercent);
        }

        return new ScraperHealthStats(scraperName, total, successRate, distribution, alertTriggered);
    }

    public async Task<IReadOnlyList<ScraperHealthLogEntry>> GetRecentFailuresAsync(
        string scraperName,
        int lastN = 20,
        CancellationToken cancellationToken = default)
    {
        // Başarısızlıklar genelde daha seyrek olduğu için, son N BAŞARISIZLIĞI toplayabilmek adına
        // tüm capped-list'i (en fazla MaxEntriesPerScraper kadar) tarayıp filtreliyoruz.
        var entries = await ReadEntriesAsync(scraperName, MaxEntriesPerScraper);
        return entries.Where(e => !e.Success).Take(lastN).ToList();
    }

    private async Task<List<ScraperHealthLogEntry>> ReadEntriesAsync(string scraperName, int lastN)
    {
        var db = _redis.GetDatabase();
        var key = LogKey(scraperName);

        var rawEntries = await db.ListRangeAsync(key, 0, lastN - 1);

        var entries = new List<ScraperHealthLogEntry>();
        foreach (var raw in rawEntries)
        {
            if (!raw.HasValue) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<ScraperHealthLogEntry>((string)raw!, JsonOptions);
                if (entry is not null) entries.Add(entry);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Scraper sağlık log kaydı ayrıştırılamadı (scraper: {ScraperName}).", scraperName);
            }
        }

        return entries;
    }

    private static string LogKey(string scraperName) => $"scraper:health:{scraperName}:log";

    // Playwright'ın çok satırlı exception mesajları (ör. DNS hatalarında call log'u da içerir) redis-cli
    // ile ham kayda bakarken okunabilirliği bozuyordu (gerçek verilerle doğrulandı, kullanıcı fark etti) —
    // tek satıra indirgenir, işlevsel bir etkisi yok (JSON içinde zaten geçerliydi).
    private static string? NormalizeToSingleLine(string? message) =>
        message?.ReplaceLineEndings(" ");
}
