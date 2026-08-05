using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.BeymenScraper.Services;

// Gerçek Beymen entegrasyonu — canlı beymen.com üzerinden keşfedildi (bkz. .claude/ARCHITECTURE.md > Beymen
// Scraper). Bershka/Zara/H&M/Massimo Dutti'den TEMEL FARKI: Beymen'in ana web sitesi (SSR sayfaları)
// Incapsula (Imperva) korumalı — ama bu keşifte kullanılan gerçek stok API'LERİ (`sf-api` ve `/api/store/...`)
// TAMAMEN AYRI ve KORUMASIZ (canlı doğrulandı: çerezsiz düz `curl` ile 200 + gerçek veri). Bu yüzden bu
// scraper'da PLAYWRIGHT HİÇ YOK — Mango'daki gibi hem online hem mağaza stoğu düz, dayanıklılık politikalı
// bir `HttpClient` ile okunuyor; PDP sayfasına navigasyon gerekmiyor, `ProductUrl` bu markada kullanılmıyor.
//
//   - Online stok: `POST /sf-api/api/product/{productId}/productsummary` — body'de sabit bir
//     `X-API-Key`/`X-Client-Id` çifti var (canlı ağ trafiğinden alındı, front-end bundle'ında gömülü genel
//     bir yapılandırma değeri gibi görünüyor, oturuma özel değil — session cookie'siz istekle de aynı sonucu
//     veriyor). Yanıt `result.sizes[]` dizisinde her beden için `inStock`, `stockQuantity` (GERÇEK sayısal
//     miktar) ve `variantBarcode` (mağaza sorgusu için gereken barkod) veriyor. `storeId` body alanı fiziksel
//     mağaza DEĞİL — 1 ve 2 aynı sonucu veriyor (muhtemelen web/mobil kanal ayrımı), 0/3+ boş (204) dönüyor;
//     bu yüzden mağaza bazlı sorgu için AYRI bir endpoint kullanılıyor (aşağıya bkz.).
//   - Mağaza stoğu: `GET /api/store/getstorestock/{variantBarcode}?sellerCode=beymen&isOnlineExclusive=false&languageCode=tr`
//     — o barkodu (yani o SPESİFİK bedeni) taşıyan TÜM fiziksel mağazaların listesini döner (`Name`,
//     `DistrictName`, `CityName`, `Coordinate`, ve o mağazadaki `Variants[]` — hangi bedenlerin orada
//     bulunduğu + `IsAboutToRunOut` bayrağı). Yanıt Zara/Mango/Massimo Dutti gibi SEYREK (sparse): o bedeni
//     TAŞIMAYAN mağazalar dizide hiç yer almıyor. Bu API'de sayısal bir mağaza ID'si YOK — mağazalar kendi
//     `Name` alanlarıyla (ör. "Beymen Suadiye") tanımlanıyor, bu yüzden `BrandSpecificStoreId` bu isim.
//     `IsAboutToRunOut`, Zara'nın `IsLastUnit`'inin AKSİNE tam "son ürün" değil, "azalıyor/tükenmek üzere" bir
//     eşik sinyali — API bu markada mağaza bazında GERÇEK bir sayı vermiyor, bu yüzden Quantity mağaza
//     kontrolünde her zaman null, IsLastUnit ise en yakın mevcut sinyal olarak bu bayraktan taşınıyor
//     (dürüstçe belgelenmiş bir yaklaşıklık, uydurma bir "== 1" varsayımı değil).
//
// PDP/PLAYWRIGHT YOK, REDIS CACHE-ASIDE: productsummary yanıtı (online stok + mağaza sorgusu için gereken
// barkod eşlemesi) productCode başına 15 dakika Redis'te önbelleğe alınıyor (diğer scraper'larla aynı
// gerekçe — API'ye gereksiz tekrar istek atmamak için, bot tespitinden kaçınmak için değil, çünkü zaten
// korumasız).
public class BeymenApiClient : IBeymenApiClient
{
    private const string ScraperName = "beymen";

    // Canlı ağ trafiğinden alınan, front-end'e gömülü sabit değerler — kullanıcıya/oturuma özel değil.
    private const string ApiKey = "d983566a-b528-43da-b711-e473b89d2d3e";
    private const string ClientId = "d782789e-d7b6-4886-bf98-5198107f6af0";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<BeymenApiClient> _logger;

    public BeymenApiClient(
        HttpClient httpClient,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<BeymenApiClient> logger)
    {
        _httpClient = httpClient;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<StockCheckResult?> CheckOnlineStockAsync(string productCode, string size, CancellationToken cancellationToken)
    {
        var summary = await GetProductSummaryAsync(productCode, cancellationToken);
        var sizeEntry = summary?.Sizes?.FirstOrDefault(s => string.Equals(s.SizeName, size, StringComparison.OrdinalIgnoreCase));
        if (sizeEntry is null) return null;

        // API gerçek sayısal miktar veriyor — IsLastUnit, Zara'daki gibi miktardan türetiliyor.
        return new StockCheckResult(sizeEntry.InStock, sizeEntry.StockQuantity, sizeEntry.StockQuantity == 1);
    }

    public async Task<StockCheckResult?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, CancellationToken cancellationToken)
    {
        var summary = await GetProductSummaryAsync(productCode, cancellationToken);
        var sizeEntry = summary?.Sizes?.FirstOrDefault(s => string.Equals(s.SizeName, size, StringComparison.OrdinalIgnoreCase));
        if (sizeEntry?.VariantBarcode is null)
        {
            _logger.LogWarning("productCode={ProductCode} beden={Size} için barkod çözülemedi.", productCode, size);
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var context = $"barcode={sizeEntry.VariantBarcode} store={brandSpecificStoreId}";

        var requestUri = $"/api/store/getstorestock/{Uri.EscapeDataString(sizeEntry.VariantBarcode)}?sellerCode=beymen&isOnlineExclusive=false&languageCode=tr";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Beymen mağaza stok API'sine ({Context}) ulaşılamadı.", context);
            await _healthLog.LogAttemptAsync(ScraperName, "StoreStock", success: false, null, ex.Message, context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }

        await _healthLog.LogAttemptAsync(
            ScraperName, "StoreStock", response.IsSuccessStatusCode, (int)response.StatusCode,
            errorMessage: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
            context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        StoreStockResponseDto? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<StoreStockResponseDto>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Beymen mağaza stok yanıtı ayrıştırılamadı ({Context}).", context);
            return null;
        }

        var stores = payload?.Data ?? [];

        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ (bkz. sınıf üstündeki yorum): dizide hiç yer almayan mağaza, o
        // barkodu (bedeni) TAŞIMADIĞI anlamına geliyor — Unknown değil, False.
        var storeEntry = stores.FirstOrDefault(s => string.Equals(s.Name, brandSpecificStoreId, StringComparison.OrdinalIgnoreCase));
        if (storeEntry is null) return new StockCheckResult(false, null, null);

        var variant = (storeEntry.Variants ?? [])
            .FirstOrDefault(v => string.Equals(v.Barcode, sizeEntry.VariantBarcode, StringComparison.Ordinal));
        if (variant is null) return new StockCheckResult(false, null, null);

        // Mağaza API'si sayısal miktar vermiyor — Quantity her zaman null. IsLastUnit, API'nin kendi
        // `IsAboutToRunOut` bayrağından geliyor (tam "son ürün" değil, "azalıyor" sinyali — bkz. sınıf
        // üstündeki yorum).
        return new StockCheckResult(true, null, variant.IsAboutToRunOut);
    }

    private async Task<ProductSummaryDto?> GetProductSummaryAsync(string productCode, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"beymen:product-summary:{productCode}";

        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            try
            {
                return JsonSerializer.Deserialize<ProductSummaryDto>(cached.ToString(), JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Redis'teki ürün özeti cache verisi ayrıştırılamadı, yeniden çekiliyor ({ProductCode}).", productCode);
            }
        }

        var stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            // "X-API-Key"/"X-Client-Id" gövdede tire içeren gerçek alan adlarıyla bekleniyor (canlı istekte
            // doğrulandı) — anonim tip yerine ham JSON string kullanılıyor, çünkü C# özellik adları tire
            // içeremiyor.
            var json = $$"""{"deviceType":"D","languageCode":"tr","storeId":1,"X-API-Key":"{{ApiKey}}","X-Client-Id":"{{ClientId}}"}""";
            response = await _httpClient.PostAsync(
                $"/sf-api/api/product/{Uri.EscapeDataString(productCode)}/productsummary",
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Beymen ürün özeti API'sine ({ProductCode}) ulaşılamadı.", productCode);
            await _healthLog.LogAttemptAsync(ScraperName, "ProductSummary", success: false, null, ex.Message, productCode, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }

        await _healthLog.LogAttemptAsync(
            ScraperName, "ProductSummary", response.IsSuccessStatusCode, (int)response.StatusCode,
            errorMessage: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
            productCode, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        ProductSummaryEnvelopeDto? envelope;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<ProductSummaryEnvelopeDto>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Beymen ürün özeti yanıtı ayrıştırılamadı ({ProductCode}).", productCode);
            return null;
        }

        var result = envelope?.Result;
        if (result?.Sizes is { Count: > 0 })
        {
            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(result, JsonOptions), CacheTtl);
        }

        return result;
    }

    private record ProductSummaryEnvelopeDto(bool Success, ProductSummaryDto? Result);

    private record ProductSummaryDto(int ProductId, List<SizeEntryDto>? Sizes);

    private record SizeEntryDto(bool InStock, string SizeName, bool IsAboutToRunOutOfStock, string? VariantCode, string? VariantBarcode, int StockQuantity);

    private record StoreStockResponseDto(bool Succeed, List<StoreDto>? Data);

    private record StoreDto(string Name, string? DistrictName, string? CityName, List<StoreVariantDto>? Variants);

    private record StoreVariantDto(string Barcode, string Text, bool IsAboutToRunOut);
}
