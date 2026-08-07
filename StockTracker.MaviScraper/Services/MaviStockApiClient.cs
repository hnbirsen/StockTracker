using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MaviScraper.Services;

// Gerçek Mavi entegrasyonu — canlı mavi.com üzerinden keşfedildi (bkz. .claude/ARCHITECTURE.md > Mavi
// Scraper). Diğer hiçbir markayla (hepsi Inditex/H&M) alt-yapı paylaşmıyor — SAP Hybris (Accelerator)
// tabanlı, Cloudflare korumalı bir platform.
//
// Bershka/Zara'nın aksine burada bir "ColorId eşleştirmesi" GEREKMİYOR: her PDP URL'i zaten TEK bir renk
// varyantını temsil ediyor (`productCode` = "{styleCode}-{colorCode}", ör. "1010381-A4216"), sayfaya gömülü
// `sizeVariantJson` dizisinin TAMAMI o renge ait — sadece istenen Beden(W)/Boy(L) kombinasyonunu bulmak yeterli.
//
//   - Online stok: PDP'nin SSR HTML'ine gömülü `sizeVariantJson` dizisinde ilgili `size`/`length` kaydının
//     `stockLevelStatus` ("inStock"/"outOfStock") ve GERÇEK sayısal `stockLevel` alanı.
//   - Mağaza stok: aynı kayıttaki `id` alanı (gerçek barkod) ile `/magazalar/get-stores-by-location`
//     sorgulanıyor — enlem/boylam bazlı "yakındaki mağazalar" modeli (Mango/H&M'deki gibi), yanıt SEYREK
//     (sparse): sorgulanan mağaza dizide yoksa o barkodun o mağazada YOK demek. Sayısal bir mağaza stok
//     adedi YOK, yalnızca var/yok.
//
// Beden formatı iki boyutlu olabiliyor (jean'lerde W=bel, L=boy) — `size` parametresi "W/L" (ör. "30/32")
// formatında verilirse ayrıştırılıp ikisi birden eşleştiriliyor; tek boyutlu ürünlerde (tişört vb.) düz
// "W" (ör. "M") yeterli, `length` boş string ile eşleşiyor.
//
// PLAYWRIGHT + REDIS CACHE-ASIDE: hem PDP hem mağaza sorgusu Cloudflare'in arkasında olduğu için düz HTTP
// ile çekilemiyor (bkz. IMaviPdpFetcher) — bir PDP çekimi ürünün TÜM beden/boy kombinasyonlarının verisini
// tek seferde verdiği için, sonucu productUrl başına Redis'te 15 dakika önbelleğe alıyoruz.
public class MaviStockApiClient : IMaviStockApiClient
{
    private const string ScraperName = "mavi";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMaviPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<MaviStockApiClient> _logger;

    public MaviStockApiClient(
        IMaviPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<MaviStockApiClient> logger)
    {
        _pdpFetcher = pdpFetcher;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(size, productUrl, cancellationToken);
        if (sizeEntry is null) return null;

        var inStock = sizeEntry.StockLevelStatus == "inStock";
        var quantity = inStock ? sizeEntry.StockLevel : (int?)null;
        return new StockCheckResult(inStock, quantity, quantity == 1);
    }

    public async Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, double storeLatitude, double storeLongitude, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(size, productUrl, cancellationToken);
        if (sizeEntry is null) return null;

        var resultJson = await _pdpFetcher.FetchStoreAvailabilityJsonAsync(productUrl, sizeEntry.Barcode, storeLatitude, storeLongitude, cancellationToken);
        if (resultJson is null) return null;

        StoreAvailabilityResponseDto? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StoreAvailabilityResponseDto>(resultJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Mavi mağaza yanıtı ayrıştırılamadı ({Url}).", productUrl);
            return null;
        }

        var results = payload?.AllStoreData?.FirstOrDefault()?.Results ?? [];
        if (!long.TryParse(brandSpecificStoreId, out var targetStoreId))
        {
            _logger.LogWarning("brandSpecificStoreId sayısal değil: {StoreId}", brandSpecificStoreId);
            return null;
        }

        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ: dizide yer almayan mağaza = o barkodun o mağazada YOK demek
        // (Zara/Mango/Massimo Dutti'deki sparse-yanıt semantiğiyle aynı) — Unknown değil, False. Mavi'nin
        // mağaza API'si sayısal bir adet vermiyor, bu yüzden Quantity/IsLastUnit her zaman null.
        var found = results.Any(s => s.StoreId == targetStoreId.ToString());
        return new StockCheckResult(found, null, null);
    }

    // productUrl'e ait TÜM beden/boy kombinasyonlarının listesini (cache-aside ile) getirir, sonra verilen
    // size'a ("W" ya da "W/L") ait olanı bulur.
    private async Task<SizeEntry?> ResolveSizeEntryAsync(string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizes = await GetProductSizesAsync(productUrl, cancellationToken);

        var slashIndex = size.IndexOf('/');
        var targetWidth = slashIndex >= 0 ? size[..slashIndex] : size;
        var targetLength = slashIndex >= 0 ? size[(slashIndex + 1)..] : string.Empty;

        var match = sizes.FirstOrDefault(entry =>
            string.Equals(entry.Size, targetWidth, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Length, targetLength, StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match;

        _logger.LogWarning(
            "Ürün sayfasında beden={Size} eşleşmesi bulunamadı ({Url}) — desteklenmeyen beden formatı olabilir.",
            size, productUrl);
        return null;
    }

    private async Task<List<SizeEntry>> GetProductSizesAsync(string productUrl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"mavi:pdp-sizes:{productUrl}";

        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            try
            {
                return JsonSerializer.Deserialize<List<SizeEntry>>(cached.ToString()) ?? [];
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Redis'teki PDP cache verisi ayrıştırılamadı, yeniden çekiliyor ({Url}).", productUrl);
            }
        }

        var sizesJson = await _pdpFetcher.FetchProductSizesJsonAsync(productUrl, cancellationToken);
        if (sizesJson is null) return [];

        List<SizeEntry> sizes;
        try
        {
            sizes = JsonSerializer.Deserialize<List<SizeEntry>>(sizesJson) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Playwright'tan dönen beden JSON'u ayrıştırılamadı ({Url}).", productUrl);
            return [];
        }

        // Boş sonucu cache'lemiyoruz — geçici bir Playwright/Cloudflare hatası olabilir, bir sonraki
        // istek tekrar denesin (15 dakika boyunca "hiç beden yok" diye kilitlenmesin).
        if (sizes.Count > 0)
        {
            await db.StringSetAsync(cacheKey, sizesJson, CacheTtl);
        }

        return sizes;
    }

    private record SizeEntry(string Size, string Length, string Barcode, int StockLevel, string StockLevelStatus);

    private record StoreAvailabilityResponseDto([property: JsonPropertyName("allStoreData")] List<StoreDataDto>? AllStoreData);

    private record StoreDataDto([property: JsonPropertyName("results")] List<StoreResultDto>? Results);

    private record StoreResultDto([property: JsonPropertyName("storeId")] string StoreId);
}
