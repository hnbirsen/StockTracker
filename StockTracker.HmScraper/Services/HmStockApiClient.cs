using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.HmScraper.Services;

// Gerçek H&M entegrasyonu — Faz 6.1'de canlı www2.hm.com üzerinden keşfedildi (bkz. .claude/ARCHITECTURE.md
// > H&M Scraper). ⚠️ Online stok mimarisi DÜZELTİLDİ — kullanıcının paylaştığı 3 gerçek `curl` isteği,
// Stradivarius'takine benzer bir gözden kaçan API'yi ortaya çıkardı:
//
//   - Online stok: ARTIK PDP'nin `__NEXT_DATA__`'sından (Playwright) DEĞİL, tamamen AYRI ve KORUMASIZ bir
//     domain'den — `GET ofg.hm.com/pdh-availability/v1/product/tr/availability/{productId}` — okunuyor.
//     Bu domain `www2.hm.com`'un Akamai korumasının TAMAMEN DIŞINDA (canlı doğrulandı: `curl` ve
//     çerezsiz/`credentials:'omit'` `fetch` ile bile 200 dönüyor) — Bershka'nın ayrı `api.inditex.com`'u,
//     Beymen'in `sf-api`'si ile aynı "ayrı domain = korumasız" deseni. Yanıt `{"availability":[13 haneli
//     tam SKU'lar],"fewPieceLeft":[bunun alt kümesi]}` — PDP'nin `__NEXT_DATA__.ssrAvailability`'siyle
//     BİREBİR AYNI veri (bu API, Next.js sunucusunun PDP'yi oluştururken zaten çağırdığı arka uç). Tam SKU
//     = productId(7) + artId(3) + sizeCode(3) — 13 hane.
//   - Beden adı↔kod eşlemesi: hâlâ Playwright gerektiriyor (`IHmPdpFetcher`, `aemData.productArticleDetails.
//     variations[articleCode].sizes[]`) — ama bu veri ürün başına neredeyse hiç değişmediği için (stok gibi
//     anlık değil, ürün sayfasının kalıcı içeriği) 24 saat önbelleğe alınıyor, Playwright kullanım sıklığını
//     "her stok kontrolünde bir kez" yerine "ürün başına ~günde bir kez"e indiriyor.
//   - Mağaza stoğu: DEĞİŞMEDİ — `/tr_tr/sis/tr/{productId}/{artId}?latitude=...&longitude=...` hâlâ
//     Playwright üzerinden (Akamai'yi geçmiş bir oturumdan) çağrılıyor; bu endpoint AYNI `www2.hm.com`
//     domain'inde olduğu için (çerezsiz çalıştığı doğrulanmış olsa da, Akamai'nin asıl ayırt ettiği şey
//     istemci TLS/tarayıcı parmak izi — bir .NET `HttpClient` bunu taklit edemeyebilir) temkinli davranıldı.
//     Mango'yla aynı model: mağaza ID'si değil enlem/boylam ile "yakındaki mağazalar" sorgusu, yanıt SEYREK
//     DEĞİL (Zara'nın aksine tüm yakın mağazalar `traffLightInd` R/Y/G ile dönüyor).
public class HmStockApiClient : IHmStockApiClient
{
    private const string ScraperName = "hm";

    // Beden adı↔kod eşlemesi ürün başına neredeyse hiç değişmiyor — stok gibi anlık veri DEĞİL, bu yüzden
    // diğer scraper'ların 15dk'lık PDP cache'inden çok daha uzun (24 saat) tutulabiliyor.
    private static readonly TimeSpan SizeMapCacheTtl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _availabilityApiClient;
    private readonly IHmPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<HmStockApiClient> _logger;

    public HmStockApiClient(
        HttpClient httpClient,
        IHmPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<HmStockApiClient> logger)
    {
        _availabilityApiClient = httpClient;
        _pdpFetcher = pdpFetcher;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        if (!TrySplitProductCode(productCode, out var productId, out var artId))
        {
            _logger.LogWarning("productCode beklenen \"ürün/renk\" formatında değil: {ProductCode}", productCode);
            return null;
        }

        var sizeEntry = await ResolveSizeEntryAsync(size, productUrl, cancellationToken);
        if (sizeEntry is null) return null;

        var stopwatch = Stopwatch.StartNew();
        var fullSku = productId + artId + sizeEntry.SizeCode;

        HttpResponseMessage response;
        try
        {
            response = await _availabilityApiClient.GetAsync($"/pdh-availability/v1/product/tr/availability/{productId}", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "H&M online stok API'sine (productId={ProductId}) ulaşılamadı.", productId);
            await _healthLog.LogAttemptAsync(ScraperName, "PdhAvailability", success: false, null, ex.Message, productId, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }

        await _healthLog.LogAttemptAsync(
            ScraperName, "PdhAvailability", response.IsSuccessStatusCode, (int)response.StatusCode,
            errorMessage: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
            productId, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        PdhAvailabilityDto? availability;
        try
        {
            availability = await response.Content.ReadFromJsonAsync<PdhAvailabilityDto>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "H&M online stok yanıtı ayrıştırılamadı (productId={ProductId}).", productId);
            return null;
        }

        var isAvailable = availability?.Availability?.Contains(fullSku) ?? false;
        var isFewPieceLeft = availability?.FewPieceLeft?.Contains(fullSku) ?? false;

        return new StockCheckResult(isAvailable, null, isFewPieceLeft);
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

        // Quantity kasıtlı olarak taşınmıyor — `avaiQty` gerçek bir miktar değil, kabaca gruplanmış bir
        // değer (yalnızca 0/1000/2000/3000 gözlemlendi).
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
        var cacheKey = $"hm:pdp-sizecodes:{productUrl}";

        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            try
            {
                return JsonSerializer.Deserialize<List<SizeEntry>>(cached.ToString(), JsonOptions) ?? [];
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Redis'teki beden-kodu cache verisi ayrıştırılamadı, yeniden çekiliyor ({Url}).", productUrl);
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
            await db.StringSetAsync(cacheKey, sizesJson, SizeMapCacheTtl);
        }

        return sizes;
    }

    private record SizeEntry(string Name, string SizeCode);

    private record PdhAvailabilityDto(
        [property: JsonPropertyName("availability")] List<string>? Availability,
        [property: JsonPropertyName("fewPieceLeft")] List<string>? FewPieceLeft);

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
