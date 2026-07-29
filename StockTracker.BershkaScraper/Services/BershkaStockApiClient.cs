using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.BershkaScraper.Services;

// Gerçek Bershka/Inditex entegrasyonu.
//
// İLK TASARIM (artık terk edildi): productCode + beden'den kendimiz bir "part-number" inşa etmeye
// çalışıyorduk (REF ayraçsız + renk + 2 haneli beden kodu). Bu, gerçek verilerle test edilirken İKİ
// AYRI şekilde yanlış çıktı:
//   1. Part-number'ın ilk hanesi sabit "0" değil, ÜRÜN KATEGORİSİNE göre değişiyor (giyim "0", ayakkabı "1", ...).
//   2. Alfabetik bedenlerde (XS/S/M/L) son 2 hane, bedenin kendisinden değil ÜRÜNE ÖZGÜ bir sıra numarasından
//      geliyor — tahmin edilemez, ürünün kendi sayfasından okunmalı.
//   3. Ayrıca "online stok" için ayrı bir e-ticaret depo endpoint'i yok; şehir bazlı fiziksel mağaza
//      örneklemesi, sitede satılabilir görünen ama örneklenen mağazalarda fiziksel stoğu olmayan
//      ürünleri yanlışlıkla OutOfStock gösteriyordu.
//
// GÜNCEL TASARIM: kendimiz hiçbir kod/id üretmiyoruz. Ürünün kendi sayfasını (productUrl, Product
// Service'ten gelir) `IBershkaPdpFetcher` ile çekip, Bershka'nın HER beden için zaten hesapladığı gerçek
// `partnumber`, `mastersSizeId` ve `stock` ("in_stock" / "coming_soon" / "out_of_stock") bilgisini alıyoruz
// — tam olarak sitede "beden seçildiğinde disabled görünme" davranışının kaynağı. Bu veri artık statik
// HTML'den regex ile değil, `PlaywrightPdpFetcher`'ın sayfanın Vue component ağacından okuduğu, zaten
// çözümlenmiş gerçek değerlerden geliyor (bkz. IBershkaPdpFetcher üstündeki not — regex neden terk edildi).
//   - Online stok: o bedenin `stock` alanı "in_stock" mı? (kategori/beden formatından bağımsız, evrensel)
//   - Mağaza stok: o bedenin gerçek `partnumber`'ı (campaign dahil) ile fiziksel stok API'sini sorguluyoruz,
//     dönen `sizeStocks[]` içinden `mastersSizeId`'ye eşit `size` alanını filtreliyoruz (ÖNEMLİ: `sizeId`
//     DEĞİL — bkz. SizeStockDto üstündeki not, gerçek verilerle bulunan kritik bir hataydı).
//
// PLAYWRIGHT + REDIS CACHE-ASIDE: ürün sayfası (PDP) Akamai Bot Manager'ın JS interstitial'ının arkasında
// olduğu için düz HTTP ile çekilemiyor (bkz. IBershkaPdpFetcher) — gerçek bir tarayıcı motoru gerekiyor,
// bu da her istekte çalıştırılamayacak kadar pahalı (saniyeler sürüyor + Akamai'nin "bot itibarını" hacimle
// büyütme riski, bkz. .claude/ARCHITECTURE.md > Ölçeklenme Riski). Bir PDP çekimi ürünün TÜM bedenlerinin
// verisini tek seferde verdiği için, sonucu productUrl başına Redis'te 15 dakika önbelleğe alıyoruz — aynı
// ürüne yapılan art arda aramalar (farklı il/ilçe, farklı kullanıcı) Playwright'ı tekrar tetiklemez.
public class BershkaStockApiClient : IBershkaStockApiClient
{
    private const string ScraperName = "bershka";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _stockApiClient;
    private readonly IBershkaPdpFetcher _pdpFetcher;
    private readonly IConnectionMultiplexer _redis;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<BershkaStockApiClient> _logger;

    public BershkaStockApiClient(
        HttpClient httpClient,
        IBershkaPdpFetcher pdpFetcher,
        IConnectionMultiplexer redis,
        IScraperHealthLogService healthLog,
        ILogger<BershkaStockApiClient> logger)
    {
        _stockApiClient = httpClient;
        _pdpFetcher = pdpFetcher;
        _redis = redis;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<bool?> CheckOnlineStockAsync(string productCode, string size, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(productCode, size, productUrl, cancellationToken);
        return sizeEntry is null ? null : sizeEntry.Stock == "in_stock";
    }

    public async Task<bool?> CheckStoreStockAsync(string productCode, string size, string brandSpecificStoreId, string productUrl, CancellationToken cancellationToken)
    {
        var sizeEntry = await ResolveSizeEntryAsync(productCode, size, productUrl, cancellationToken);
        if (sizeEntry is null) return null;

        if (!TrySplitPartNumber(sizeEntry.PartNumber, out var partNumberDigits, out var campaignId) ||
            !int.TryParse(sizeEntry.MastersSizeId, out var targetSizeId) ||
            partNumberDigits.Length < 2)
        {
            _logger.LogWarning(
                "Ürün sayfasından okunan partnumber/mastersSizeId ayrıştırılamadı: {PartNumber} / {MastersSizeId}",
                sizeEntry.PartNumber, sizeEntry.MastersSizeId);
            return null;
        }

        // GERÇEK SİTE AĞ TRAFİĞİYLE DOĞRULANAN KRİTİK BULGU: aynı renk (ColorId) için PDP'de BİRDEN
        // FAZLA farklı taban SKU "ailesi" (partnumber'ın son 2 haneli konum kodundan önceki kısmı)
        // bulunabiliyor — Bershka/Inditex tarafında bir parti/batch ayrımı. Örn. "Haki" renginde XS/S/L
        // bedenleri "01337711507" ailesinden, M/XL ise "01337015507" ailesinden geliyor. Sitenin kendi
        // "Mağazada mevcudiyet" modalı, hedef bedenin konum kodunu (ör. "02") BİLİNEN TÜM aile
        // prefix'leriyle birleştirip hepsini paralel sorguluyor ve sonuçları birleştiriyor (Playwright ile
        // yakalanan gerçek istekler: aynı store seti için hem `...150701` hem `...550701` sorgulanıyor).
        // Biz sadece hedef bedenin KENDİ partnumber'ını sorgularsak, beden başka bir ailede kayıtlıysa
        // (PDP feed'indeki aile ataması güvenilir değil) gerçek stok kaçırılıyordu — ürün 2 canlı testinde
        // tam olarak bu yaşandı (S bedeni "Şişli"de stokta olduğu halde OutOfStock dönmüştü).
        var ownSuffix = partNumberDigits[^2..];
        var sizes = await GetProductSizesAsync(productUrl, cancellationToken);
        var candidateDigits = sizes
            .Where(e => string.Equals(e.ColorId, sizeEntry.ColorId, StringComparison.Ordinal))
            .Select(e => TrySplitPartNumber(e.PartNumber, out var digits, out _) && digits.Length >= 2 ? digits[..^2] : null)
            .Where(prefix => prefix is not null)
            .Distinct()
            .Select(prefix => prefix + ownSuffix)
            .Distinct()
            .ToList();

        if (candidateDigits.Count == 0) candidateDigits = [partNumberDigits];

        var anyDataFound = false;
        var sizeFoundInAnyFamily = false;
        var totalQuantity = 0;

        foreach (var digits in candidateDigits)
        {
            var requestUri = $"/ocpstiencom-external/common/1/stock/campaign/{Uri.EscapeDataString(campaignId)}/product/part-number/{Uri.EscapeDataString(digits)}?physicalStoreId={Uri.EscapeDataString(brandSpecificStoreId)}";

            var stopwatch = Stopwatch.StartNew();
            var response = await _stockApiClient.GetAsync(requestUri, cancellationToken);
            await _healthLog.LogAttemptAsync(
                ScraperName, "StockApi", response.IsSuccessStatusCode, (int)response.StatusCode,
                errorMessage: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
                context: $"{productUrl} | store={brandSpecificStoreId} partNumber={digits}",
                (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bershka stok API'si beklenmeyen durum kodu döndürdü: {StatusCode} ({Uri})", response.StatusCode, requestUri);
                continue;
            }

            var payload = await response.Content.ReadFromJsonAsync<StockResponseDto>(JsonOptions, cancellationToken);
            var sizeStocks = (payload?.Stocks ?? []).SelectMany(s => s.SizeStocks ?? []).ToList();
            if (sizeStocks.Count == 0) continue;

            anyDataFound = true;
            var matching = sizeStocks.Where(sz => sz.Size == targetSizeId).ToList();
            if (matching.Count == 0) continue;

            sizeFoundInAnyFamily = true;
            totalQuantity += matching.Sum(sz => sz.Quantity);
        }

        // Gerçek verilerle bulunan bir durum: bazı ürünler mağaza stok özelliğini hiç desteklemiyor
        // (sitede "Mağazada mevcudiyet" bölümü bile görünmüyor) — bu durumda API boş bir "stocks": []
        // döndürüyor (sorgulanan TÜM aile prefix'leri için). Bunu "OutOfStock" olarak yorumlamak yanıltıcı:
        // kullanıcı ürünü mağazada gerçekten görebilir, biz sadece hiç veri alamadık. "Bilmiyorum" demek,
        // yanlış "yok" demekten daha güvenli — bu yüzden veri yoksa Unknown (null) dönüyoruz, false değil.
        // Aynı mantık: en az bir aile veri döndürse bile hiçbirinde hedef beden hiç yer almıyorsa da Unknown.
        if (!anyDataFound || !sizeFoundInAnyFamily)
        {
            _logger.LogInformation(
                "Mağaza stok API'si {StoreId} için hedef beden ({TargetSizeId}) hakkında veri döndürmedi — Unknown dönülüyor.",
                brandSpecificStoreId, targetSizeId);
            return null;
        }

        return totalQuantity > 0;
    }

    // productUrl'e ait TÜM bedenlerin listesini (cache-aside ile) getirir, sonra verilen productCode'un
    // son 3 hanesinden çıkardığı ColorId + beden adına (sayısal ya da alfabetik, fark etmez — Bershka'nın
    // kendi görünen adıyla birebir string eşleşmesi) ait olanı bulur.
    //
    // NOT: eskiden ColorId yerine "entry.PartNumber productCode ile mi başlıyor" (StartsWith) kontrolü
    // kullanılıyordu. Bu, TEK bir renk için BİRDEN FAZLA partnumber ailesi olduğu durumlarda (yukarıdaki
    // "Haki" örneği) YANLIŞTI: productCode sadece BİR aileyle eşleşiyordu, o ailede bulunmayan bedenler
    // (ör. M/XL, "01337711507" ailesinde yok) için hiç eşleşme bulunamıyor, Unknown dönüyordu — halbuki
    // o beden AYNI RENGİN diğer ailesinde gerçekten vardı. ColorId, PDP'nin kendi `color.id` alanından
    // geldiği için ailelerden bağımsız, güvenilir bir renk anahtarı.
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
        var cacheKey = $"bershka:pdp-sizes:{productUrl}";

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

    private record SizeEntry(string Name, string Stock, string PartNumber, string MastersSizeId, string ColorId);

    private record StockResponseDto([property: JsonPropertyName("stocks")] List<StoreStockDto>? Stocks);

    private record StoreStockDto([property: JsonPropertyName("sizeStocks")] List<SizeStockDto>? SizeStocks);

    // ÖNEMLİ: "sizeId" ve "size" AYNI ŞEY DEĞİL — gerçek verilerle doğrulanan gerçek bir hata buradaydı.
    // "sizeId", mağaza içindeki bedenlerin sıralı/kompakt bir indeksi (ör. 1,2,3,4 — ürüne özgü, anlamsız).
    // "size" ise PDP'den okuduğumuz `mastersSizeId` ile birebir eşleşen, Bershka'nın evrensel beden kodu
    // (ör. 101,102,103,104). Sayısal bedenlerde (jean: 32,34,36) ikisi tesadüfen eşit çıktığı için bu hata
    // fark edilmemişti — alfabetik bedenlerde (mastersSizeId 101+) `sizeId` ile filtrelemek her zaman boş
    // sonuç veriyordu (gerçek stok varken bile OutOfStock dönüyordu). Filtreleme `Size` alanına göre yapılmalı.
    private record SizeStockDto(
        [property: JsonPropertyName("sizeId")] int SizeId,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("quantity")] int Quantity);
}
