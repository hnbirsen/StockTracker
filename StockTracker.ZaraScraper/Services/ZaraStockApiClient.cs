using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.ZaraScraper.Services;

// Gerçek Zara/Inditex entegrasyonu — Faz 6.1'de canlı zara.com üzerinden keşfedildi (bkz. .claude/ARCHITECTURE.md
// > Zara Scraper). Bershka ile aynı genel yaklaşım (kendimiz hiçbir kod üretmiyoruz, ürünün kendi sayfasından
// gerçek veriyi okuyoruz) ama veri şekli ve mağaza stok mekanizması tamamen farklı:
//
//   - Online stok: Zara'nın PDP'si Bershka'daki gibi bir Vue component ağacı değil, `window.zara.viewPayload`
//     içinde SSR ile gömülü `product.detail.colors[].sizes[]` — her bedende doğrudan bir `availability`
//     alanı var. CANLI VERİYLE 10 ÜRÜNLÜK UÇTAN UCA TEST TURUNDA doğrulandı: bu alan yalnızca iki değer
//     ("in_stock" / diğerleri) almıyor, ÜÇÜNCÜ bir değer de var — `"low_on_stock"` (az sayıda kaldı, ama
//     hâlâ satın alınabilir). İlk tasarımda yalnızca `"in_stock"` true kabul ediliyordu; bu, gerçekten
//     stokta olan (sadece az sayıda) bir ürünü yanlışlıkla OutOfStock gösteriyordu — düzeltildi: hem
//     `"in_stock"` hem `"low_on_stock"` stokta sayılıyor. Aynı productCode'un renk parçası (son 3 hane,
//     ör. "5063/821/802" -> "802") sayfadaki `color.id` ile eşleştirilerek doğru renk seçiliyor (Bershka'daki
//     ColorId eşleştirme deseniyle birebir aynı gerekçe: bir PDP birden fazla renk barındırabilir).
//
//   - Mağaza stoğu: Bershka'nın ayrı, Akamai'siz bir stok API'si (api.inditex.com) var; Zara'da BÖYLE BİR ŞEY
//     YOK — `store-product-availability` endpoint'i www.zara.com'un kendi altında ve PDP kadar Akamai korumalı
//     (curl ile canlı doğrulandı: gerçekçi UA'yla bile 403). Bu yüzden düz HttpClient/resilience pipeline
//     KULLANILMIYOR — sorgu, PDP'ye yapılan bir Playwright navigasyonuyla kurulan tarayıcı oturumunun
//     çerezleriyle sayfa içinden atılıyor (bkz. IZaraPdpFetcher.FetchStoreAvailabilityJsonAsync).
//     Mağaza sorgusu için gereken `productId`, ürünün KENDİ `product.id` alanından DEĞİL, RENGE ÖZGÜ bir
//     değerden geliyor — canlı ağ trafiğiyle doğrulanan, ilk bakışta beklenmeyen bir ayrım (ör. bir üründe
//     `product.id`=545850110 iken store-product-availability SADECE renk bazlı 547843031 değeriyle çalışıyor).
//     İKİ EŞDEĞER KAYNAĞI VAR: ürün URL'indeki `v1` sorgu parametresi VE (10 ürünlük ikinci doğrulama
//     turunda bulunan, daha sağlam) sayfanın kendi `colors[].productId` alanı — ikisi birebir aynı değeri
//     taşıyor (canlı veriyle doğrulandı: aynı üründe `v1=547843031` ve `colors[{id:"802"}].productId=547843031`).
//     `colors[].productId` URL'in `v1` içermesine bağımlı olmadığı (bare bir ürün URL'inde bile mevcut) için
//     BİRİNCİL kaynak olarak kullanılıyor; URL'deki `v1` yalnızca PDP verisi hiç çekilemediğinde bir fallback.
//     Yanıt "seyrek" (sparse): sorgulanan mağazalardan sadece o ürünün GERÇEKTEN stoğu olanlar dizide yer
//     alıyor — aynı istekte iki mağaza sorgulanıp birinin dönmemesiyle doğrulandı (bkz. IZaraPdpFetcher
//     üstündeki not, bulgu #5). Bu yüzden dizide mağaza hiç yoksa Unknown DEĞİL, doğrudan OutOfStock (false)
//     dönülüyor — Bershka'nın "boş stocks[] = Unknown" davranışının TAM TERSİ, çünkü Zara'da boşluk API
//     hatası değil, doğrulanmış "bu mağazada yok" anlamına geliyor.
//
// PLAYWRIGHT + REDIS CACHE-ASIDE: PDP verisi (online stok) productUrl başına 15 dakika Redis'te önbelleğe
// alınıyor (Bershka'daki gerekçenin aynısı — Playwright pahalı, aynı ürüne art arda gelen aramalar tekrar
// tetiklemesin). Mağaza stoğu ise HER ZAMAN canlı sorgulanıyor (önbelleklenmiyor) — fiziksel stok online'a
// göre daha hızlı değiştiği için, zaten PlaywrightZaraFetcher kendi içinde hız sınırlaması uyguluyor.
public class ZaraStockApiClient : IZaraStockApiClient
{
    private const string ScraperName = "zara";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IZaraPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<ZaraStockApiClient> _logger;

    public ZaraStockApiClient(
        IZaraPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<ZaraStockApiClient> logger)
    {
        _pdpFetcher = pdpFetcher;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    // Canlı veriyle doğrulanan "stokta" durumları — bkz. sınıf üstündeki yorum, "low_on_stock" bulgusu.
    private static readonly HashSet<string> InStockAvailabilityValues = new(StringComparer.Ordinal)
    {
        "in_stock",
        "low_on_stock"
    };

    public async Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(productCode, size, productUrl, cancellationToken);
        // Online kontrolde sayısal miktar yok (yalnızca "in_stock"/"low_on_stock" gibi string enum) —
        // Quantity/IsLastUnit her zaman null.
        return sizeEntry is null ? null : new StockCheckResult(InStockAvailabilityValues.Contains(sizeEntry.Availability), null, null);
    }

    public async Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken)
    {
        // Birincil kaynak: PDP verisinin kendi `colors[].productId` alanı (ColorId eşleşmesiyle) — URL'in
        // `v1` parametresini içermesine bağımlı değil (bkz. sınıf üstündeki yorum). PDP hiç çekilemezse
        // (Playwright/Akamai hatası) URL'deki `v1`'e geri düşülüyor.
        var sizeEntry = await ResolveSizeEntryAsync(productCode, size, productUrl, cancellationToken);
        var productId = sizeEntry?.ProductId ?? ExtractProductId(productUrl);
        if (string.IsNullOrWhiteSpace(productId))
        {
            _logger.LogWarning("productId ne PDP verisinden ne de URL'in v1 parametresinden çıkarılabildi: {Url}", productUrl);
            return null;
        }

        var json = await _pdpFetcher.FetchStoreAvailabilityJsonAsync(productUrl, productId, brandSpecificStoreId, cancellationToken);
        if (json is null) return null;

        StoreAvailabilityResponseDto? response;
        try
        {
            response = JsonSerializer.Deserialize<StoreAvailabilityResponseDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "store-product-availability yanıtı ayrıştırılamadı ({Url}).", productUrl);
            return null;
        }

        var stores = response?.Stores ?? [];
        if (!long.TryParse(brandSpecificStoreId, out var targetStoreId))
        {
            _logger.LogWarning("brandSpecificStoreId sayısal değil: {StoreId}", brandSpecificStoreId);
            return null;
        }

        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ: dizide hiç yer almayan mağaza = o üründen o mağazada YOK
        // (Unknown değil). Bkz. sınıf üstündeki yorum. Mağaza/beden hiç bulunamadığında kesin bir miktar
        // bilmiyoruz (0 değil, "veri yok") — Quantity null kalıyor.
        var storeEntry = stores.FirstOrDefault(s => s.PhysicalStoreId == targetStoreId);
        if (storeEntry is null) return new StockCheckResult(false, null, null);

        var storeSizeEntry = (storeEntry.SizesAvailability ?? [])
            .FirstOrDefault(s => string.Equals(s.Size, size, StringComparison.OrdinalIgnoreCase));

        // Mağaza kaydı var ama bu beden hiç listelenmemiş — aynı "seyrek yanıt" mantığıyla, bu bedenden
        // o mağazada stok olmadığı anlamına geldiği varsayılıyor (ayrı bir "beden bazlı Unknown" durumu
        // canlı veriyle henüz gözlemlenmedi).
        if (storeSizeEntry is null) return new StockCheckResult(false, null, null);

        // API tam sayısal miktar veriyor (`stock`) — Faz 6.1'de kullanıcı talebiyle StockResultEvent'e
        // taşınıyor. IsLastUnit, Zara'nın API'sinde ayrı bir bayrak olmadığı için miktardan türetiliyor.
        return new StockCheckResult(storeSizeEntry.Stock > 0, storeSizeEntry.Stock, storeSizeEntry.Stock == 1);
    }

    private async Task<SizeEntry?> ResolveSizeEntryAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizes = await GetProductSizesAsync(productUrl, cancellationToken);
        var normalizedSize = size.Trim();
        var targetColorId = productCode.Length >= 3 ? productCode[^3..] : productCode;

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
        var cacheKey = $"zara:pdp-sizes:{productUrl}";

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

        var sizesJson = await _pdpFetcher.FetchProductDataJsonAsync(productUrl, cancellationToken);
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

        // Boş sonucu cache'lemiyoruz — geçici bir Playwright/Akamai hatası olabilir, bir sonraki istek
        // tekrar denesin (15 dakika boyunca "hiç beden yok" diye kilitlenmesin).
        if (sizes.Count > 0)
        {
            await db.StringSetAsync(cacheKey, sizesJson, CacheTtl);
        }

        return sizes;
    }

    // Fallback kaynak: ürün URL'indeki "v1" sorgu parametresi (bkz. sınıf üstündeki yorum — PDP verisindeki
    // `colors[].productId` ile birebir aynı değeri taşıdığı doğrulandı, ama yalnızca PDP hiç çekilemediğinde
    // kullanılıyor). Örnek: https://www.zara.com/tr/tr/...-p05063821.html?v1=547843031&v2=2420417 -> "547843031".
    private static string? ExtractProductId(string productUrl)
    {
        if (!Uri.TryCreate(productUrl, UriKind.Absolute, out var uri)) return null;

        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "v1" && !string.IsNullOrWhiteSpace(parts[1]))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private record SizeEntry(string Name, string Availability, string ColorId, string Sku, string ProductId);

    private record StoreAvailabilityResponseDto(
        [property: JsonPropertyName("sizesAvailableAndLocationsByPhysicalStores")] List<StoreEntryDto>? Stores);

    private record StoreEntryDto(
        [property: JsonPropertyName("physicalStoreId")] long PhysicalStoreId,
        [property: JsonPropertyName("sizesAvailability")] List<SizeAvailabilityDto>? SizesAvailability);

    private record SizeAvailabilityDto(
        [property: JsonPropertyName("size")] string Size,
        [property: JsonPropertyName("stock")] int Stock);
}
