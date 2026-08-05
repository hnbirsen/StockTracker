using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MangoScraper.Services;

// Gerçek Mango entegrasyonu — Faz 6.1'de canlı shop.mango.com üzerinden keşfedildi (bkz.
// .claude/ARCHITECTURE.md > Mango Scraper). Bershka/Zara'dan EN BÜYÜK FARK: Mango'nun ne ürün sayfası
// (PDP) ne de mağaza stok API'si Akamai/Transmit Security gibi bir bot yönetim sistemi tarafından
// engellenmiyor — gerçekçi bir User-Agent + Accept-Language yeterli (canlı doğrulandı: `curl` bile
// çalışıyor). Bu yüzden bu scraper'da PLAYWRIGHT YOK — hem online hem mağaza stoğu düz, dayanıklılık
// politikalı (`StockTracker.Shared.Scraping`) bir `HttpClient` ile okunuyor.
//
//   - Online stok: `IMangoPdpFetcher`, PDP'nin Next.js RSC akışından `colors[].sizes[].available`
//     (doğrudan boolean) okuyor. productCode'un renk parçası (ör. "37013869/56" -> "56") `color.id` ile
//     eşleştiriliyor.
//   - Mağaza stoğu: Zara'nın "belirli mağaza ID'si" modelinin AKSİNE, Mango'nun
//     `store-finder/v2/stores/stock` endpoint'i enlem/boylam etrafında "yakındaki mağazalar" araması
//     yapıyor. CANLI VERİYLE DOĞRULANDI: sorgulanan mağazanın KENDİ koordinatları verilirse (mesafe ≈ 0)
//     o mağaza güvenilir şekilde sonuç listesinde çıkıyor — bu yüzden `store_db`'deki her Mango mağazası
//     için gerçek enlem/boylam saklanıyor (bkz. Messages.V2.CheckStockCommand > StoreLatitude/Longitude).
//     Yanıt "seyrek" (sparse) — Zara'da doğrulanan davranışın aynısı: bir mağaza dönen dizide hiç
//     yer almıyorsa, o mağazanın var olmadığı değil, o ürünü/rengi TAŞIMADIĞI anlamına geliyor (canlı
//     veriyle çapraz doğrulandı: Cevahir AVM store'u bir üründe hiç çıkmadı, farklı bir üründe gerçek
//     verisiyle çıktı). Mango, tam stok adedi yerine yalnızca "bu bedenler bu mağazada mevcut" listesi
//     (`sizes: string[]`) döndürüyor — Zara/Bershka'nın aksine miktar bilgisi yok.
public class MangoStockApiClient : IMangoStockApiClient
{
    private const string ScraperName = "mango";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _storeFinderClient;
    private readonly IMangoPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<MangoStockApiClient> _logger;

    public MangoStockApiClient(
        HttpClient httpClient,
        IMangoPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<MangoStockApiClient> logger)
    {
        _storeFinderClient = httpClient;
        _pdpFetcher = pdpFetcher;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<bool?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(productCode, size, productUrl, cancellationToken);
        return sizeEntry?.Available;
    }

    public async Task<bool?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, double storeLatitude, double storeLongitude, CancellationToken cancellationToken)
    {
        if (!TrySplitProductCode(productCode, out var productId, out var colorId))
        {
            _logger.LogWarning("productCode beklenen \"referans/renk\" formatında değil: {ProductCode}", productCode);
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var context = $"productId={productId} colorId={colorId} store={brandSpecificStoreId} lat={storeLatitude} lng={storeLongitude}";

        var requestUri = $"/ncs/store-finder/v2/stores/stock" +
            $"?colorId={Uri.EscapeDataString(colorId)}&productId={Uri.EscapeDataString(productId)}" +
            $"&countryIso=TR&languageIso=TR&channelId=shop" +
            $"&latitude={storeLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&longitude={storeLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        HttpResponseMessage response;
        try
        {
            response = await _storeFinderClient.GetAsync(requestUri, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Mango mağaza stok API'sine ({Context}) ulaşılamadı.", context);
            await _healthLog.LogAttemptAsync(ScraperName, "StoreFinder", success: false, null, ex.Message, context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }

        await _healthLog.LogAttemptAsync(
            ScraperName, "StoreFinder", response.IsSuccessStatusCode, (int)response.StatusCode,
            errorMessage: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
            context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        StoreFinderResponseDto? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<StoreFinderResponseDto>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Mango mağaza stok yanıtı ayrıştırılamadı ({Context}).", context);
            return null;
        }

        var stores = payload?.Stores ?? [];

        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ (bkz. sınıf üstündeki yorum): dizide hiç yer almayan mağaza,
        // o üründen/renkten o mağazada YOK demek — Unknown değil, False.
        var storeEntry = stores.FirstOrDefault(s => string.Equals(s.Id, brandSpecificStoreId, StringComparison.Ordinal));
        if (storeEntry is null) return false;

        return (storeEntry.Sizes ?? [])
            .Any(s => string.Equals(s, size, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TrySplitProductCode(string productCode, out string productId, out string colorId)
    {
        var parts = productCode.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            productId = parts[0];
            colorId = parts[1];
            return true;
        }

        productId = string.Empty;
        colorId = string.Empty;
        return false;
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
        var cacheKey = $"mango:pdp-sizes:{productUrl}";

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

    private record SizeEntry(string Name, bool Available, string ColorId);

    private record StoreFinderResponseDto(
        [property: JsonPropertyName("stores")] List<StoreEntryDto>? Stores);

    private record StoreEntryDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("sizes")] List<string>? Sizes);
}
