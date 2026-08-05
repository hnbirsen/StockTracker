using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.StradivariusScraper.Services;

// Gerçek Stradivarius (Inditex) entegrasyonu — canlı stradivarius.com üzerinden keşfedildi (bkz.
// .claude/ARCHITECTURE.md > Stradivarius Scraper). Diğer Inditex markalarından FARKLI, kendine özgü bir
// hibrit mimari:
//
//   - Online stok: ürün sayfası Zara/Bershka/Massimo Dutti/Pull&Bear gibi Akamai Bot Manager'ın arkasında
//     (canlı doğrulandı, `bm-verify` deseni) — ama beden/stok verisi bir JS state/API çağrısı DEĞİL,
//     doğrudan SUNUCU TARAFINDA RENDER EDİLMİŞ HTML'de (`button[data-testid="size-item"]` + `data-sku`,
//     `disabled`, "Stok tükenmek üzere" metni). Bu yüzden Playwright yalnızca Akamai'yi geçmek için gerekli,
//     hydration/polling GEREKMİYOR (bkz. IStradivariusPdpFetcher.FetchOnlineSizesJsonAsync).
//
//   - Mağaza stoğu: **kullanıcının kendi tarayıcısından paylaştığı gerçek `curl` istekleriyle** (Beymen'deki
//     collaborative-debugging emsaliyle aynı yöntem) bulunan, KORUMASIZ bir REST API —
//     `POST /api/storefront/1/stores/54009571/skus-availability-in-stores/actions/filter?language=tr-TR`,
//     gövde `{"physicalStoreIds":[...],"skus":[...]}`. Yanıt SEYREK (sparse, Zara/Mango/Massimo Dutti gibi):
//     `{"<physicalStoreId>":{"<sku>":{"size":"M","stock":5,...}}}` — sorgulanan mağaza/sku dizide hiç yer
//     almıyorsa o bedenin o mağazada YOK demek. Bu, projede ilk denemede ATLANMIŞ bir endpoint'ti — canlı
//     keşifte ("MAĞAZA STOK DURUMU" modalının kapalı bir Shadow DOM içinde olduğu, hiçbir JS/DOM erişiminin
//     mümkün olmadığı doğrulanmıştı) modalın KENDİ ağ trafiği bizim otomasyon araçlarımızda görünmüyordu —
//     kullanıcının kendi DevTools Network sekmesinden paylaştığı gerçek istekler sayesinde bulundu.
//   - Kimlik doğrulama: `Authorization: Bearer {access_token}` — bu, sitenin HER YENİ misafir (guest)
//     oturumuna otomatik verdiği bir JWT (canlı doğrulandı: `user_type=guest` çerezi, ~24 saat geçerlilik),
//     ayrı bir login/kayıt akışı GEREKMİYOR. Token, `IStradivariusPdpFetcher`'ın aynı PDP ziyaretinde
//     `document.cookie`'den okunup online beden verisiyle birlikte dönüyor.
//   - Mağaza keşfi: gerçek numaralı mağaza ID'leri `itxrest/2/bam/store/{storeId}/physical-store` (enlem/
//     boylam ile "yakındaki mağazalar" araması, Massimo Dutti/Pull&Bear'daki gibi YALNIZCA keşif için)
//     üzerinden bulundu — Kadıköy=16879 (City's Kozyatağı), Şişli=2859 (Cevahir), Çankaya=2968 (Kentpark),
//     Bornova=2868 (Forum Bornova). `BrandSpecificStoreId` bu sayısal ID'ler (Zara/Massimo Dutti/Pull&Bear
//     ile aynı model — Beymen'deki isim-tabanlı modelin AKSİNE).
//
// PLAYWRIGHT + REDIS CACHE-ASIDE: PDP verisi (online stok + mağaza sorgusu için gereken access_token VE
// per-beden SKU'lar) productUrl başına 15 dakika Redis'te önbelleğe alınıyor (diğer scraper'larla aynı
// gerekçe — guest-session token'ın ~24 saatlik geçerlilik süresi içinde rahatça sığıyor). Mağaza stok API'si
// korumasız ve ucuz olduğu için hiç önbelleklenmiyor — her zaman canlı sorgulanıyor.
public class StradivariusStockApiClient : IStradivariusStockApiClient
{
    private const string ScraperName = "stradivarius";

    // Stradivarius'un Türkiye online mağazası için sabit "storeId" (kanal/context ID) — canlı ağ
    // trafiğiyle doğrulandı (`itxrest/2/bam/store/54009571/...` ve `api/storefront/1/stores/54009571/...`
    // her iki API'de de aynı değer kullanılıyor). Massimo Dutti/Pull&Bear'daki "StoreChannelId" kavramıyla
    // aynı rolde.
    private const string StoreChannelId = "54009571";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _storeApiClient;
    private readonly IStradivariusPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<StradivariusStockApiClient> _logger;

    public StradivariusStockApiClient(
        HttpClient httpClient,
        IStradivariusPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<StradivariusStockApiClient> logger)
    {
        _storeApiClient = httpClient;
        _pdpFetcher = pdpFetcher;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var pdpData = await GetPdpDataAsync(productUrl, cancellationToken);
        var match = pdpData?.Sizes.FirstOrDefault(s => string.Equals(s.Name, size, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            _logger.LogWarning(
                "Stradivarius ürün sayfasında beden={Size} eşleşmesi bulunamadı ({Url}).", size, productUrl);
            return null;
        }

        return new StockCheckResult(!match.Disabled, null, match.LowStock);
    }

    public async Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken)
    {
        var pdpData = await GetPdpDataAsync(productUrl, cancellationToken);
        if (pdpData?.AccessToken is null)
        {
            _logger.LogWarning("Stradivarius guest-session access_token alınamadı ({Url}).", productUrl);
            return null;
        }

        var match = pdpData.Sizes.FirstOrDefault(s => string.Equals(s.Name, size, StringComparison.OrdinalIgnoreCase));
        if (match?.Sku is null)
        {
            _logger.LogWarning(
                "Stradivarius ürün sayfasında beden={Size} için SKU bulunamadı ({Url}).", size, productUrl);
            return null;
        }

        if (!long.TryParse(brandSpecificStoreId, out var targetStoreId) || !long.TryParse(match.Sku, out var sku))
        {
            _logger.LogWarning("brandSpecificStoreId veya SKU sayısal değil: store={StoreId} sku={Sku}", brandSpecificStoreId, match.Sku);
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var context = $"store={brandSpecificStoreId} sku={sku}";

        var requestUri = $"/api/storefront/1/stores/{StoreChannelId}/skus-availability-in-stores/actions/filter?language=tr-TR";
        var requestBody = new AvailabilityRequestDto([targetStoreId], [sku]);

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(requestBody, options: JsonOptions)
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pdpData.AccessToken);
            response = await _storeApiClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Stradivarius mağaza stok API'sine ({Context}) ulaşılamadı.", context);
            await _healthLog.LogAttemptAsync(ScraperName, "SkusAvailabilityInStores", success: false, null, ex.Message, context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }

        await _healthLog.LogAttemptAsync(
            ScraperName, "SkusAvailabilityInStores", response.IsSuccessStatusCode, (int)response.StatusCode,
            errorMessage: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
            context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        Dictionary<string, Dictionary<string, SkuAvailabilityDto>>? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<Dictionary<string, Dictionary<string, SkuAvailabilityDto>>>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Stradivarius mağaza stok yanıtı ayrıştırılamadı ({Context}).", context);
            return null;
        }

        // CANLI KULLANICI TRAFİĞİYLE DOĞRULANAN SEYREK (sparse) DAVRANIŞ: sorgulanan mağaza anahtarı yanıtta
        // hiç yer almıyorsa (ya da mağaza var ama sku yoksa) o bedenin o mağazada YOK demek — Unknown değil,
        // False (Zara/Mango/Massimo Dutti'deki sparse-yanıt semantiğiyle aynı).
        if (payload is null || !payload.TryGetValue(brandSpecificStoreId, out var skuMap) ||
            !skuMap.TryGetValue(match.Sku, out var availability))
        {
            return new StockCheckResult(false, null, null);
        }

        // API tam sayısal miktar veriyor (`stock`) — Zara/Massimo Dutti/Pull&Bear'daki gibi doğrudan
        // StockResultEvent'e taşınıyor. IsLastUnit, API'de ayrı bir bayrak olmadığı için miktardan türetiliyor.
        return new StockCheckResult(availability.Stock > 0, availability.Stock, availability.Stock == 1);
    }

    private async Task<PdpDataDto?> GetPdpDataAsync(string productUrl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"stradivarius:pdp-data:{productUrl}";

        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            try
            {
                var cachedData = JsonSerializer.Deserialize<PdpDataDto>(cached.ToString(), JsonOptions);
                if (cachedData is not null) return cachedData;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Redis'teki PDP cache verisi ayrıştırılamadı, yeniden çekiliyor ({Url}).", productUrl);
            }
        }

        var resultJson = await _pdpFetcher.FetchOnlineSizesJsonAsync(productUrl, cancellationToken);
        if (resultJson is null) return null;

        PdpDataDto? data;
        try
        {
            data = JsonSerializer.Deserialize<PdpDataDto>(resultJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Playwright'tan dönen PDP JSON'u ayrıştırılamadı ({Url}).", productUrl);
            return null;
        }

        if (data is not null && data.Sizes.Count > 0)
        {
            await db.StringSetAsync(cacheKey, resultJson, CacheTtl);
        }

        return data;
    }

    private record PdpDataDto(
        [property: JsonPropertyName("AccessToken")] string? AccessToken,
        [property: JsonPropertyName("Sizes")] List<SizeEntry> Sizes);

    private record SizeEntry(string Name, string? Sku, bool Disabled, bool LowStock);

    private record AvailabilityRequestDto(
        [property: JsonPropertyName("physicalStoreIds")] List<long> PhysicalStoreIds,
        [property: JsonPropertyName("skus")] List<long> Skus);

    private record SkuAvailabilityDto(
        [property: JsonPropertyName("size")] string Size,
        [property: JsonPropertyName("stock")] int Stock,
        [property: JsonPropertyName("isOnSale")] bool IsOnSale);
}
