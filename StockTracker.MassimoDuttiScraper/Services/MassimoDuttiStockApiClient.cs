using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MassimoDuttiScraper.Services;

// Gerçek Massimo Dutti (Inditex) entegrasyonu — canlı massimodutti.com üzerinden keşfedildi (bkz.
// .claude/ARCHITECTURE.md > Massimo Dutti Scraper). Zara ile AYNI grup (Inditex) ama mimarisi HİÇBİR mevcut
// scraper'la birebir aynı değil — hibrit bir durum:
//
//   - Online stok: ürün sayfası (`#mdfrontw-state` Angular SSR state, `colors[].sizes[].isBuyable`) Zara/Bershka
//     gibi Akamai Bot Manager'ın arkasında — canlı doğrulandı: düz `curl` (gerçekçi UA'yla bile) gerçek HTML
//     yerine bir `bm-verify` sorgu parametresiyle JS-yönlendirme (challenge) sayfası döndürüyor. Aynı şekilde
//     `itxrest/2/catalog/.../detail` API'si de canlı testte 403 (`Service Unavailable`) aldı. Bu yüzden online
//     stok SADECE Playwright (gerçek Chrome kanalı) ile okunabiliyor (bkz. IMassimoDuttiPdpFetcher).
//     productCode'un renk parçası (ör. "06244810/251" -> "251") sayfadaki `color.id` ile eşleştiriliyor
//     (Zara/Mango'daki ColorId eşleştirme deseniyle birebir aynı gerekçe).
//
//   - Mağaza stoğu: gerçek stok-sorgusu endpoint'i `api/storefront/1/stores/{storeId}/products/{catEntryId}/
//     available-sizes?physicalStoreIds=...&sizeIds=...` — AYNI domain'de olmasına rağmen Akamai KORUMASIZ
//     (canlı doğrulandı: düz `curl` ile 200 + gerçek, sayısal stok verisi). Bu yüzden bu çağrı için Playwright
//     DEĞİL, düz dayanıklılık politikalı bir HttpClient kullanılıyor.
//
//     ⚠️ DÜZELTME (ilk keşifte YANLIŞ tespit edilmişti): bu scraper'ın ilk sürümünde mağaza sorgusu için
//     `itxrest/2/bam/store/{storeId}/physical-store` (genel mağaza BULUCU API'si — yalnızca il/ilçe bazlı
//     mağaza listesi döner) kullanılmış ve bu API'nin döndürdüğü `receiveStockQuery` bayrağının TÜM Türkiye
//     mağazalarında `false` olması "stok sorgusu bu ülke için desteklenmiyor" şeklinde yorumlanmıştı. Kullanıcı
//     kendi tarayıcısında GERÇEK bir stok sonucu gördüğünü bildirince yeniden araştırıldı: `receiveStockQuery`
//     bayrağının mağaza stok sorgusuyla HİÇBİR ilgisi yok (muhtemelen mağazalar arası transfer/lojistik gibi
//     tamamen farklı bir iç işlevi belirtiyor) — asıl stok verisi yukarıdaki AYRI `available-sizes` endpoint'i
//     üzerinden geliyor ve gerçek mağaza ID'leriyle test edildiğinde GERÇEK sayısal stok adedi (`stock`) dönüyor.
//     Bu, Zara/Mango'daki gibi SEYREK (sparse) bir yanıt: sorgulanan mağaza dizide hiç yer almıyorsa (canlı
//     veriyle doğrulandı — CEVAHIR/Şişli örneği), o mağazada o beden/ürün YOK demek, Unknown değil.
//
//     Bu API'yi çağırmak için PDP'den iki değer gerekiyor: seçili rengin `catentryId`'si (ürün URL'indeki
//     `pelement` sorgu parametresiyle aynı — Zara'nın URL'deki `v1` fallback'ine benzer, ama burada PDP'den
//     birincil kaynak olarak okunuyor) ve hedef bedenin `mastersSizeId`'si (her zaman görünen beden adıyla
//     birebir aynı DEĞİL — ör. alfa bedenlerde farklı olabilir, bu yüzden PDP'den okunuyor, tahmin edilmiyor).
//     Enlem/boylam GEREKMİYOR (Mango/H&M'in aksine) — mağaza ID'si doğrudan yeterli (Zara'daki gibi).
//
// PLAYWRIGHT + REDIS CACHE-ASIDE: PDP verisi (online stok VE mağaza sorgusu için gereken catEntryId/mastersSizeId)
// productUrl başına 15 dakika Redis'te önbelleğe alınıyor (diğer scraper'larla aynı gerekçe). Mağaza stok
// API'si Akamai korumasız ve ucuz olduğu için önbelleklenmiyor — her zaman canlı sorgulanıyor.
public class MassimoDuttiStockApiClient : IMassimoDuttiStockApiClient
{
    private const string ScraperName = "massimodutti";

    // Massimo Dutti'nin Türkiye online mağazası için sabit "storeId" (kanal/context ID) — canlı ağ trafiğiyle
    // doğrulandı (`itxrest/2/catalog/store/34009471/...` ve `api/storefront/1/stores/34009471/...` her iki
    // API'de de aynı değer kullanılıyor). Bershka/Zara'daki "chainId" kavramıyla aynı rolde.
    private const string StoreChannelId = "34009471";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _storeApiClient;
    private readonly IMassimoDuttiPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<MassimoDuttiStockApiClient> _logger;

    public MassimoDuttiStockApiClient(
        HttpClient httpClient,
        IMassimoDuttiPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<MassimoDuttiStockApiClient> logger)
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
        // Massimo Dutti'nin online verisinde sayısal bir miktar yok (yalnızca isBuyable boolean) — Quantity/
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
            _logger.LogWarning(ex, "Massimo Dutti mağaza stok API'sine ({Context}) ulaşılamadı.", context);
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
            _logger.LogWarning(ex, "Massimo Dutti mağaza stok yanıtı ayrıştırılamadı ({Context}).", context);
            return null;
        }

        var stores = payload?.SizesAvailableByPhysicalStores ?? [];
        if (!long.TryParse(brandSpecificStoreId, out var targetStoreId))
        {
            _logger.LogWarning("brandSpecificStoreId sayısal değil: {StoreId}", brandSpecificStoreId);
            return null;
        }

        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ (bkz. sınıf üstündeki yorum): dizide hiç yer almayan mağaza = o
        // bedenin o mağazada YOK demek (Zara/Mango'daki sparse-yanıt semantiğiyle aynı) — Unknown değil, False.
        var storeEntry = stores.FirstOrDefault(s => s.PhysicalStoreId == targetStoreId);
        if (storeEntry is null) return new StockCheckResult(false, null, null);

        var sizeAvailability = (storeEntry.SizesAvailability ?? [])
            .FirstOrDefault(s => string.Equals(s.SizeId, sizeEntry.MastersSizeId, StringComparison.Ordinal));

        if (sizeAvailability is null) return new StockCheckResult(false, null, null);

        // API tam sayısal miktar veriyor (`stock`) — Zara'daki gibi doğrudan StockResultEvent'e taşınıyor.
        // IsLastUnit, Massimo Dutti'nin API'sinde ayrı bir bayrak olmadığı için miktardan türetiliyor.
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
        var cacheKey = $"massimodutti:pdp-sizes:{productUrl}";

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

    // productCode formatı: {8 haneli temel referans}/{3 haneli renk kodu} — ör. "06244810/251". Bkz.
    // .claude/DATABASE.md > brand_db BrandCodeSignatures.
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
