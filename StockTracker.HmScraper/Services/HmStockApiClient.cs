using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.HmScraper.Services;

// Gerçek H&M entegrasyonu — Faz 6.1'de canlı www2.hm.com üzerinden keşfedildi (bkz. .claude/ARCHITECTURE.md
// > H&M Scraper). Zara'ya en yakın mimari: hem PDP hem mağaza stok API'si Akamai korumalı (canlı doğrulandı:
// `curl` ikisinde de Zara'yla birebir aynı "Access Denied" sayfasını dönüyor) — bu yüzden Playwright
// (`IHmPdpFetcher`) kullanılıyor, Mango'daki gibi düz HttpClient DEĞİL.
//
//   - Online stok: PDP'nin `__NEXT_DATA__` içindeki `ssrAvailability.availability`/`fewPieceLeft`
//     dizilerinden (tam 13 haneli SKU listesi) okunuyor — Bershka/Zara'nın string enum'larının aksine
//     doğrudan bir "bu SKU'lar stokta" listesi, yorumlamaya gerek yok.
//   - Mağaza stoğu: Mango'nun "belirli mağaza ID'si yerine enlem/boylam" modeliyle AYNI —
//     `/tr_tr/sis/tr/{productId}/{artId}?latitude=...&longitude=...` — ama yanıt ZARA/MANGO'DAN FARKLI
//     olarak SEYREK (sparse) DEĞİL: yarıçap içindeki TÜM mağazalar (stoksuz olanlar dahil) açık bir
//     `traffLightInd` (R=stokta yok, Y=birkaç tane kaldı, G=stokta — canlı doğrulandı: R ve G çok, Y
//     UI'nin kendi renk lejantında görülüyor ama bu turda canlı bir örneğine rastlanmadı) ile dönüyor.
//     Bu yüzden hedef mağaza yanıtta HİÇ yoksa (Zara'nın "yok=OutOfStock" kuralının AKSİNE) Unknown
//     dönülüyor — çünkü H&M'de "mağaza var ama listede yok" durumu hiç gözlemlenmedi, muhtemelen bir
//     sorgu/yarıçap sorununa işaret eder, "stok yok" anlamına gelmez.
//   - Mağaza sorgusu, PDP'den okunan `SizeCode`'a ihtiyaç duyuyor (Bershka'nın partnumber'ı gibi) çünkü
//     `/sis/` yanıtındaki beden kayıtları isim değil ("XS") 3 haneli kod ("002") kullanıyor.
public class HmStockApiClient : IHmStockApiClient
{
    private const string ScraperName = "hm";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHmPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<HmStockApiClient> _logger;

    public HmStockApiClient(
        IHmPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<HmStockApiClient> logger)
    {
        _pdpFetcher = pdpFetcher;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(size, productUrl, cancellationToken);
        // Online kontrolde H&M'in kendi `fewPieceLeft` bayrağı IsLastUnit'e taşınıyor — Quantity burada da
        // hiç yok (bkz. sınıf üstündeki yorum), ayrıca API'nin kendisi de sayısal bir online miktar vermiyor.
        return sizeEntry is null ? null : new StockCheckResult(sizeEntry.Available, null, sizeEntry.FewPieceLeft);
    }

    public async Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, double storeLatitude, double storeLongitude, string productUrl, CancellationToken cancellationToken)
    {
        if (!TrySplitProductCode(productCode, out var productId, out var artId))
        {
            _logger.LogWarning("productCode beklenen \"ürün/renk\" formatında değil: {ProductCode}", productCode);
            return null;
        }

        var sizeEntry = await ResolveSizeEntryAsync(size, productUrl, cancellationToken);
        if (sizeEntry is null) return null;

        var json = await _pdpFetcher.FetchStoreAvailabilityJsonAsync(productUrl, productId, artId, storeLatitude, storeLongitude, cancellationToken);
        if (json is null) return null;

        StoreFinderResponseDto? response;
        try
        {
            response = JsonSerializer.Deserialize<StoreFinderResponseDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "H&M mağaza stok yanıtı ayrıştırılamadı ({Url}).", productUrl);
            return null;
        }

        var stores = response?.Stores ?? [];

        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ (Zara'nın TERSİ — bkz. sınıf üstündeki yorum): H&M'in yanıtı
        // seyrek değil, yarıçap içindeki TÜM mağazaları döner. Hedef mağaza hiç yoksa bu bir sorgu sorunu
        // olabilir — Unknown dönülüyor, OutOfStock değil.
        var storeEntry = stores.FirstOrDefault(s => string.Equals(s.StoreCode, brandSpecificStoreId, StringComparison.OrdinalIgnoreCase));
        if (storeEntry is null) return null;

        var sizeStatus = (storeEntry.Sizes?.Size ?? [])
            .FirstOrDefault(s => string.Equals(s.SizeCode, sizeEntry.SizeCode, StringComparison.Ordinal));
        if (sizeStatus is null) return null;

        var inStock = !string.Equals(sizeStatus.TrafficLightInd, "R", StringComparison.OrdinalIgnoreCase);
        var isLastUnit = string.Equals(sizeStatus.TrafficLightInd, "Y", StringComparison.OrdinalIgnoreCase);

        // Quantity kasıtlı olarak taşınmıyor — bkz. sınıf üstündeki ve StockCheckResult üstündeki yorum
        // (`avaiQty` gerçek bir miktar değil, kabaca gruplanmış bir değer).
        return new StockCheckResult(inStock, null, isLastUnit);
    }

    private static bool TrySplitProductCode(string productCode, out string productId, out string artId)
    {
        var parts = productCode.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            productId = parts[0];
            artId = parts[1];
            return true;
        }

        productId = string.Empty;
        artId = string.Empty;
        return false;
    }

    private async Task<SizeEntry?> ResolveSizeEntryAsync(string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizes = await GetProductSizesAsync(productUrl, cancellationToken);
        var normalizedSize = size.Trim();

        var match = sizes.FirstOrDefault(entry => string.Equals(entry.Name, normalizedSize, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;

        _logger.LogWarning(
            "Ürün sayfasında beden={Size} eşleşmesi bulunamadı ({Url}) — desteklenmeyen beden formatı olabilir.",
            size, productUrl);
        return null;
    }

    private async Task<List<SizeEntry>> GetProductSizesAsync(string productUrl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"hm:pdp-sizes:{productUrl}";

        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            try
            {
                return JsonSerializer.Deserialize<List<SizeEntry>>(cached.ToString(), JsonOptions) ?? [];
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Redis'teki PDP cache verisi ayrıştırılamadı, yeniden çekiliyor ({Url}).", productUrl);
            }
        }

        var sizesJson = await _pdpFetcher.FetchProductDataJsonAsync(productUrl, cancellationToken);
        if (sizesJson is null) return [];

        List<SizeEntry> sizes;
        try
        {
            sizes = JsonSerializer.Deserialize<List<SizeEntry>>(sizesJson, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "PDP fetcher'dan dönen beden JSON'u ayrıştırılamadı ({Url}).", productUrl);
            return [];
        }

        if (sizes.Count > 0)
        {
            await db.StringSetAsync(cacheKey, sizesJson, CacheTtl);
        }

        return sizes;
    }

    private record SizeEntry(string Name, string SizeCode, bool Available, bool FewPieceLeft);

    private record StoreFinderResponseDto(
        [property: JsonPropertyName("stores")] List<StoreEntryDto>? Stores);

    private record StoreEntryDto(
        [property: JsonPropertyName("storeCode")] string StoreCode,
        [property: JsonPropertyName("sizes")] SizesWrapperDto? Sizes);

    private record SizesWrapperDto(
        [property: JsonPropertyName("size")] List<SizeStatusDto>? Size);

    private record SizeStatusDto(
        [property: JsonPropertyName("sizeCode")] string SizeCode,
        [property: JsonPropertyName("avaiQty")] int AvaiQty,
        [property: JsonPropertyName("traffLightInd")] string TrafficLightInd);
}
