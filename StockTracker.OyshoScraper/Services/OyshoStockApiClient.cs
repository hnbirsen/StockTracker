using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.OyshoScraper.Services;

// Gerçek Oysho/Inditex entegrasyonu — canlı oysho.com üzerinden keşfedildi (bkz. .claude/ARCHITECTURE.md
// > Oysho Scraper). Bershka ile AYNI arka uç stok API'sini (`api.inditex.com/ocpstiencom-external/...`)
// paylaşıyor — bu, sitenin kendi `#oyshoServer-state` script etiketindeki `CONFIG_KEY.productStockPhysicalStoreUrl`
// alanından doğrulandı. Ama PDP'nin veri şekli Bershka'nın Vue component ağacından FARKLI: Oysho, sunucu
// tarafında render edilmiş bir Angular uygulaması, veri doğrudan `#oyshoServer-state` içinde düz JSON
// (`PRODUCT_HYDRATION_KEY.product.colors[].sizes[]`) olarak geliyor — hiçbir hydration/component taraması
// gerekmiyor (bkz. IOyshoPdpFetcher).
//   - Online stok: o bedenin `availability` alanı "in_stock" mı? (Zara'daki "coming_soon"/"out_of_stock"
//     ile aynı üçlü enum). `hasFewUnits` bayrağı PDP'nin KENDİ verdiği bir "az kaldı" sinyali (Mango'nun
//     `lastUnits`'iyle aynı desen) — IsLastUnit doğrudan bu alandan geliyor, bizim türettiğimiz bir şey değil.
//   - Mağaza stok: o bedenin gerçek `partnumber`'ı (campaign dahil) ile Bershka'daki BİREBİR AYNI fiziksel
//     stok API'sini sorguluyoruz, dönen `sizeStocks[]` içinden `masterSizeId`'ye eşit `size` alanını
//     filtreliyoruz (Bershka'da bulunan `sizeId` ≠ `size` hatasıyla aynı gerekçe — bkz. SizeStockDto).
//
// ⚠️ DOĞRULANMAMIŞ RİSK (dürüstçe belgelenen bir sınır): Bershka'da aynı renk için birden fazla partnumber
// "ailesi" olabildiği (bkz. BershkaStockApiClient) tek round keşifte Oysho'da GÖZLEMLENMEDİ — ama aynı
// `ocpstiencom-external` arka ucunu paylaştığı için aynı davranış teorik olarak mümkün. Bu scraper şimdilik
// yalnızca hedef bedenin KENDİ partnumber'ını sorguluyor (Massimo Dutti/Pull&Bear'daki gibi basit yaklaşım).
// Canlı çoklu-ürün testinde Bershka'dakine benzer bir tutarsızlık gözlemlenirse aynı çoklu-aile sorgulama
// mantığı buraya da eklenmeli.
//
// PLAYWRIGHT + REDIS CACHE-ASIDE: ürün sayfası (PDP) Akamai Bot Manager'ın JS interstitial'ının arkasında
// olduğu için düz HTTP ile çekilemiyor (bkz. IOyshoPdpFetcher) — bir PDP çekimi ürünün TÜM bedenlerinin
// verisini tek seferde verdiği için, sonucu productUrl başına Redis'te 15 dakika önbelleğe alıyoruz.
public class OyshoStockApiClient : IOyshoStockApiClient
{
    private const string ScraperName = "oysho";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _stockApiClient;
    private readonly IOyshoPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<OyshoStockApiClient> _logger;

    public OyshoStockApiClient(
        HttpClient httpClient,
        IOyshoPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<OyshoStockApiClient> logger)
    {
        _stockApiClient = httpClient;
        _pdpFetcher = pdpFetcher;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(productCode, size, productUrl, cancellationToken);
        if (sizeEntry is null) return null;

        // Online kontrolde sayısal miktar yok (yalnızca "in_stock"/"coming_soon"/"out_of_stock" string'i) —
        // Quantity her zaman null. IsLastUnit ise PDP'nin kendi `hasFewUnits` bayrağından doğrudan geliyor.
        return new StockCheckResult(sizeEntry.Availability == "in_stock", null, sizeEntry.HasFewUnits);
    }

    public async Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(productCode, size, productUrl, cancellationToken);
        if (sizeEntry is null) return null;

        if (!TrySplitPartNumber(sizeEntry.PartNumber, out var partNumberDigits, out var campaignId) ||
            !int.TryParse(sizeEntry.MasterSizeId, out var targetSizeId))
        {
            _logger.LogWarning(
                "Ürün sayfasından okunan partnumber/masterSizeId ayrıştırılamadı: {PartNumber} / {MasterSizeId}",
                sizeEntry.PartNumber, sizeEntry.MasterSizeId);
            return null;
        }

        var requestUri = $"/ocpstiencom-external/common/1/stock/campaign/{Uri.EscapeDataString(campaignId)}/product/part-number/{Uri.EscapeDataString(partNumberDigits)}?physicalStoreId={Uri.EscapeDataString(brandSpecificStoreId)}";

        var stopwatch = Stopwatch.StartNew();
        var response = await _stockApiClient.GetAsync(requestUri, cancellationToken);
        await _healthLog.LogAttemptAsync(
            ScraperName, "StockApi", response.IsSuccessStatusCode, (int)response.StatusCode,
            errorMessage: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
            context: $"{productUrl} | store={brandSpecificStoreId} partNumber={partNumberDigits}",
            (int)stopwatch.ElapsedMilliseconds, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Oysho stok API'si beklenmeyen durum kodu döndürdü: {StatusCode} ({Uri})", response.StatusCode, requestUri);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<StockResponseDto>(JsonOptions, cancellationToken);
        var sizeStocks = (payload?.Stocks ?? []).SelectMany(s => s.SizeStocks ?? []).ToList();

        // Bershka'da bulunan davranışla aynı: bazı ürünler mağaza stok özelliğini hiç desteklemiyor,
        // API "stocks": [] döndürüyor. Bunu "OutOfStock" saymak yanıltıcı — Unknown (null) dönülmeli.
        if (sizeStocks.Count == 0) return null;

        var matching = sizeStocks.Where(sz => sz.Size == targetSizeId).ToList();
        if (matching.Count == 0) return null;

        var totalQuantity = matching.Sum(sz => sz.Quantity);

        // Mağaza API'si tam sayısal miktar veriyor. IsLastUnit burada (online kontrolün aksine) API'nin
        // kendi bir bayrağı olmadığı için miktardan türetiliyor (Quantity == 1) — Bershka'daki gerekçenin aynısı.
        return new StockCheckResult(totalQuantity > 0, totalQuantity, totalQuantity == 1);
    }

    // productUrl'e ait TÜM bedenlerin listesini (cache-aside ile) getirir, sonra verilen productCode'un
    // "/" sonrasındaki ColorId'sine (PDP'nin kendi `color.id` alanıyla) + beden adına (case-insensitive
    // string eşleşmesi) ait olanı bulur.
    private async Task<SizeEntry?> ResolveSizeEntryAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizes = await GetProductSizesAsync(productUrl, cancellationToken);
        var normalizedSize = size.Trim();
        var slashIndex = productCode.IndexOf('/');
        var targetColorId = slashIndex >= 0 ? productCode[(slashIndex + 1)..] : productCode;

        var match = sizes.FirstOrDefault(entry =>
            string.Equals(entry.ColorId, targetColorId, StringComparison.Ordinal) &&
            string.Equals(entry.Name, normalizedSize, StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match;

        _logger.LogWarning(
            "Ürün sayfasında productCode={ProductCode} (ColorId={ColorId}) beden={Size} eşleşmesi bulunamadı ({Url}) — desteklenmeyen beden formatı ya da yanlış productCode olabilir.",
            productCode, targetColorId, size, productUrl);
        return null;
    }

    private async Task<List<SizeEntry>> GetProductSizesAsync(string productUrl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"oysho:pdp-sizes:{productUrl}";

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

        // Boş sonucu cache'lemiyoruz — geçici bir Playwright/Akamai hatası olabilir, bir sonraki
        // istek tekrar denesin (15 dakika boyunca "hiç beden yok" diye kilitlenmesin).
        if (sizes.Count > 0)
        {
            await db.StringSetAsync(cacheKey, sizesJson, CacheTtl);
        }

        return sizes;
    }

    private static bool TrySplitPartNumber(string partNumber, out string digits, out string campaignId)
    {
        var dashIndex = partNumber.IndexOf('-');
        if (dashIndex < 0)
        {
            digits = string.Empty;
            campaignId = string.Empty;
            return false;
        }

        digits = partNumber[..dashIndex];
        campaignId = partNumber[(dashIndex + 1)..];
        return true;
    }

    private record SizeEntry(string Name, string Availability, bool HasFewUnits, string PartNumber, string MasterSizeId, string ColorId);

    private record StockResponseDto([property: JsonPropertyName("stocks")] List<StoreStockDto>? Stocks);

    private record StoreStockDto([property: JsonPropertyName("sizeStocks")] List<SizeStockDto>? SizeStocks);

    // ÖNEMLİ: "sizeId" ve "size" AYNI ŞEY DEĞİL — Bershka'da gerçek verilerle bulunan hatayla aynı gerekçe
    // (aynı `ocpstiencom-external` arka ucu paylaşıldığı için, canlı Oysho verisiyle de doğrulandı: gerçek
    // yanıtta "sizeId":8,"size":123 gibi farklı çiftler gözlendi). Filtreleme "size" alanına göre yapılmalı.
    private record SizeStockDto(
        [property: JsonPropertyName("sizeId")] int SizeId,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("quantity")] int Quantity);
}
