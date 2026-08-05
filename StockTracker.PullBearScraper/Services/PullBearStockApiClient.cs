using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.PullBearScraper.Services;

// Gerçek Pull&Bear (Inditex) entegrasyonu — canlı pullandbear.com üzerinden keşfedildi (bkz.
// .claude/ARCHITECTURE.md > Pull&Bear Scraper). Massimo Dutti ile TAMAMEN AYNI platform ve mimari — aynı
// hibrit durum:
//
//   - Online stok: ürün sayfası (`<product-modular>` custom element'inin `__product.detail.colors[].sizes[]`
//     JS özelliği) Zara/Bershka/Massimo Dutti gibi Akamai Bot Manager'ın arkasında — canlı doğrulandı: düz
//     `curl` (gerçekçi UA'yla bile) gerçek HTML yerine bir `bm-verify` sorgu parametresiyle JS-yönlendirme
//     (challenge) sayfası döndürüyor. Bu yüzden online stok SADECE Playwright (gerçek Chrome kanalı) ile
//     okunabiliyor (bkz. IPullBearPdpFetcher). productCode'un renk parçası (ör. "07460338/250" -> "250")
//     sayfadaki `color.id` ile eşleştiriliyor (Zara/Mango/Massimo Dutti'deki ColorId eşleştirme deseniyle
//     birebir aynı gerekçe).
//
//   - Mağaza stoğu: gerçek stok-sorgusu endpoint'i `api/storefront/1/stores/{storeId}/products/{catEntryId}/
//     available-sizes?physicalStoreIds=...&sizeIds=...` — Massimo Dutti'yle BİREBİR AYNI API şekli — AYNI
//     domain'de olmasına rağmen Akamai KORUMASIZ (canlı doğrulandı: düz `curl` ile 200 + gerçek, sayısal
//     stok verisi). Bu yüzden bu çağrı için Playwright DEĞİL, düz dayanıklılık politikalı bir HttpClient
//     kullanılıyor. Yanıt Zara/Mango/Massimo Dutti gibi SEYREK (sparse): sorgulanan mağaza dizide hiç yer
//     almıyorsa, o bedenin o mağazada YOK demek, Unknown değil. Enlem/boylam GEREKMİYOR — mağaza ID'si
//     doğrudan yeterli (Zara/Massimo Dutti'deki gibi).
//
//   ⚠️ ÜRÜN KODU FORMATI ÇAKIŞMASI (bilinçli, belgelenen bir kısıtlama): Pull&Bear'ın productCode formatı
//   (8 haneli temel referans / 3 haneli renk kodu, ör. "07460338/250") Massimo Dutti'ninkiyle BİREBİR AYNI
//   ŞEKİLDE — iki marka aynı alt-yapıyı (aynı "MD Front" tarzı platform) paylaştığı için. Bu, saf regex
//   tabanlı `BrandCodeSignature` eşleşmesinin bu iki markayı BİRBİRİNDEN AYIRT EDEMEYECEĞİ anlamına geliyor
//   — bir kod her iki markanın da deseniyle eşleşecek, BrandDetection Service'in "birden fazla aday" akışı
//   (zaten var olan, manuel çözüm gerektiren mekanizma) devreye girecek. Bu bir hata değil, gerçek bir
//   platform-paylaşımı sonucu ortaya çıkan dürüst bir kısıtlama — bkz. `.claude/PENDING_INPUTS.md`.
//
// PLAYWRIGHT + REDIS CACHE-ASIDE: PDP verisi (online stok VE mağaza sorgusu için gereken catEntryId/mastersSizeId)
// productUrl başına 15 dakika Redis'te önbelleğe alınıyor (diğer scraper'larla aynı gerekçe). Mağaza stok
// API'si Akamai korumasız ve ucuz olduğu için önbelleklenmiyor — her zaman canlı sorgulanıyor.
public class PullBearStockApiClient : IPullBearStockApiClient
{
    private const string ScraperName = "pullbear";

    // Pull&Bear'ın Türkiye online mağazası için sabit "storeId" (kanal/context ID) — canlı ağ trafiğiyle
    // doğrulandı (`itxrest/1/catalog/store/25009521/...` ve `api/storefront/1/stores/25009521/...` her iki
    // API'de de aynı değer kullanılıyor). Massimo Dutti'deki "StoreChannelId" kavramıyla aynı rolde.
    private const string StoreChannelId = "25009521";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _storeApiClient;
    private readonly IPullBearPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<PullBearStockApiClient> _logger;

    public PullBearStockApiClient(
        HttpClient httpClient,
        IPullBearPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<PullBearStockApiClient> logger)
    {
        _storeApiClient = httpClient;
        _pdpFetcher = pdpFetcher;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(productCode, size, productUrl, cancellationToken);
        // Pull&Bear'ın online verisinde sayısal bir miktar yok (yalnızca isBuyable boolean) — Quantity/
        // IsLastUnit online kontrolde her zaman null.
        return sizeEntry is null ? null : new StockCheckResult(sizeEntry.IsBuyable, null, null);
    }

    public async Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(productCode, size, productUrl, cancellationToken);
        if (sizeEntry is null) return null;

        var stopwatch = Stopwatch.StartNew();
        var context = $"store={brandSpecificStoreId} catEntryId={sizeEntry.CatEntryId} mastersSizeId={sizeEntry.MastersSizeId}";

        var requestUri = $"/api/storefront/1/stores/{StoreChannelId}/products/{sizeEntry.CatEntryId}/available-sizes" +
            $"?appId=1&physicalStoreIds={Uri.EscapeDataString(brandSpecificStoreId)}&sizeIds={Uri.EscapeDataString(sizeEntry.MastersSizeId)}";

        HttpResponseMessage response;
        try
        {
            response = await _storeApiClient.GetAsync(requestUri, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Pull&Bear mağaza stok API'sine ({Context}) ulaşılamadı.", context);
            await _healthLog.LogAttemptAsync(ScraperName, "StoreAvailableSizes", success: false, null, ex.Message, context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }

        await _healthLog.LogAttemptAsync(
            ScraperName, "StoreAvailableSizes", response.IsSuccessStatusCode, (int)response.StatusCode,
            errorMessage: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
            context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        AvailableSizesResponseDto? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<AvailableSizesResponseDto>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Pull&Bear mağaza stok yanıtı ayrıştırılamadı ({Context}).", context);
            return null;
        }

        var stores = payload?.SizesAvailableByPhysicalStores ?? [];
        if (!long.TryParse(brandSpecificStoreId, out var targetStoreId))
        {
            _logger.LogWarning("brandSpecificStoreId sayısal değil: {StoreId}", brandSpecificStoreId);
            return null;
        }

        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ (bkz. sınıf üstündeki yorum): dizide hiç yer almayan mağaza = o
        // bedenin o mağazada YOK demek (Zara/Mango/Massimo Dutti'deki sparse-yanıt semantiğiyle aynı) —
        // Unknown değil, False.
        var storeEntry = stores.FirstOrDefault(s => s.PhysicalStoreId == targetStoreId);
        if (storeEntry is null) return new StockCheckResult(false, null, null);

        var sizeAvailability = (storeEntry.SizesAvailability ?? [])
            .FirstOrDefault(s => string.Equals(s.SizeId, sizeEntry.MastersSizeId, StringComparison.Ordinal));

        if (sizeAvailability is null) return new StockCheckResult(false, null, null);

        // API tam sayısal miktar veriyor (`stock`) — Zara/Massimo Dutti'deki gibi doğrudan StockResultEvent'e
        // taşınıyor. IsLastUnit, API'de ayrı bir bayrak olmadığı için miktardan türetiliyor.
        return new StockCheckResult(sizeAvailability.Stock > 0, sizeAvailability.Stock, sizeAvailability.Stock == 1);
    }

    private async Task<SizeEntry?> ResolveSizeEntryAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        if (!TrySplitProductCode(productCode, out _, out var colorId))
        {
            _logger.LogWarning("productCode beklenen \"referans/renk\" formatında değil: {ProductCode}", productCode);
            return null;
        }

        var sizes = await GetProductSizesAsync(productUrl, cancellationToken);
        var normalizedSize = size.Trim();

        var match = sizes.FirstOrDefault(entry =>
            string.Equals(entry.ColorId, colorId, StringComparison.Ordinal) &&
            string.Equals(entry.Name, normalizedSize, StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match;

        _logger.LogWarning(
            "Ürün sayfasında productCode={ProductCode} (ColorId={ColorId}) beden={Size} eşleşmesi bulunamadı ({Url}).",
            productCode, colorId, size, productUrl);
        return null;
    }

    private async Task<List<SizeEntry>> GetProductSizesAsync(string productUrl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"pullbear:pdp-sizes:{productUrl}";

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
            _logger.LogWarning(ex, "Playwright'tan dönen beden JSON'u ayrıştırılamadı ({Url}).", productUrl);
            return [];
        }

        if (sizes.Count > 0)
        {
            await db.StringSetAsync(cacheKey, sizesJson, CacheTtl);
        }

        return sizes;
    }

    // productCode formatı: {8 haneli temel referans}/{3 haneli renk kodu} — ör. "07460338/250". Bkz.
    // .claude/DATABASE.md > brand_db BrandCodeSignatures (Massimo Dutti ile ÇAKIŞAN bir desen, bkz. sınıf
    // üstündeki yorum).
    private static bool TrySplitProductCode(string productCode, out string baseReference, out string colorId)
    {
        var parts = productCode.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            baseReference = parts[0];
            colorId = parts[1];
            return true;
        }

        baseReference = string.Empty;
        colorId = string.Empty;
        return false;
    }

    private record SizeEntry(string Name, string ColorId, string CatEntryId, string MastersSizeId, bool IsBuyable, string? BackSoon);

    private record AvailableSizesResponseDto(
        [property: JsonPropertyName("sizesAvailableByPhysicalStores")] List<StoreEntryDto>? SizesAvailableByPhysicalStores);

    private record StoreEntryDto(
        [property: JsonPropertyName("physicalStoreId")] long PhysicalStoreId,
        [property: JsonPropertyName("sizesAvailability")] List<SizeAvailabilityDto>? SizesAvailability);

    private record SizeAvailabilityDto(
        [property: JsonPropertyName("sizeId")] string SizeId,
        [property: JsonPropertyName("stock")] int Stock);
}
