using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.BershkaScraper.Services;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.BershkaScraper.Tests;

public class BershkaStockApiClientTests
{
    private const string ProductUrl = "https://www.bershka.com/tr/test-urun-c0p123456789.html?colorId=676";

    // PlaywrightPdpFetcher'ın Vue component ağacından çıkardığı, çözümlenmiş beden listesi JSON şekli
    // (bkz. IBershkaPdpFetcher.FetchProductSizesJsonAsync). ColorId verilmezse partnumber'ın son 2 hane
    // öncesindeki 3 haneden (yani çağıranın productCode'unun son 3 hanesiyle aynı konvansiyonla) türetilir.
    private static string SizeJson(params (string Name, string Stock, string PartNumber, string MastersSizeId, string? ColorId)[] entries) =>
        "[" + string.Join(",", entries.Select(e =>
        {
            var dash = e.PartNumber.IndexOf('-');
            var digits = dash >= 0 ? e.PartNumber[..dash] : e.PartNumber;
            var colorId = e.ColorId ?? digits[^5..^2];
            return $"{{\"Name\":\"{e.Name}\",\"Stock\":\"{e.Stock}\",\"PartNumber\":\"{e.PartNumber}\",\"MastersSizeId\":\"{e.MastersSizeId}\",\"ColorId\":\"{colorId}\"}}";
        })) + "]";

    private static (BershkaStockApiClient Sut, FakeHttpMessageHandler StockHandler, Mock<IBershkaPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage>? stockResponder = null,
        string? pdpSizesJson = null,
        RedisValue cachedValue = default)
    {
        var stockHandler = new FakeHttpMessageHandler(
            stockResponder ?? (_ => throw new InvalidOperationException("Stok API'si çağrılmamalıydı.")));
        var stockHttpClient = new HttpClient(stockHandler) { BaseAddress = new Uri("https://api.inditex.com") };

        var pdpFetcher = new Mock<IBershkaPdpFetcher>();
        pdpFetcher.Setup(f => f.FetchProductSizesJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(pdpSizesJson);

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new BershkaStockApiClient(stockHttpClient, pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<BershkaStockApiClient>>());

        return (sut, stockHandler, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenNumericSizeInStock_ReturnsTrueAndCachesResult()
    {
        var json = SizeJson(("36", "in_stock", "0289105442636-I2026", "36", null));
        var (sut, _, pdpFetcher, redisDb) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("02891054426", "36", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductSizesJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenAlphaSizeComingSoon_ReturnsFalse()
    {
        // Gerçek örnek: XS stokta, S/M/L "coming_soon" (sitede disabled görünüyor) — kullanıcının bildirdiği senaryo.
        var json = SizeJson(
            ("XS", "in_stock", "0332736067601-I2026", "101", null),
            ("S", "coming_soon", "0332736067602-I2026", "102", null),
            ("M", "coming_soon", "0332736067603-I2026", "103", null),
            ("L", "coming_soon", "0332736067604-I2026", "104", null));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var xsResult = await sut.CheckOnlineStockAsync("03327360676", "XS", ProductUrl, CancellationToken.None);
        var sResult = await sut.CheckOnlineStockAsync("03327360676", "S", ProductUrl, CancellationToken.None);

        xsResult!.InStock.Should().BeTrue();
        sResult!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_OutOfStockValueAlsoTreatedAsUnavailable()
    {
        // Gerçek sitede üç farklı "yok" durumu var: coming_soon, out_of_stock — ikisi de "in_stock" değil.
        var json = SizeJson(("M", "out_of_stock", "0344969271203-I2026", "103", null));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("03449692712", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var json = SizeJson(("XS", "in_stock", "0332736067601-I2026", "101", null));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("03327360676", "xs", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_OnlyMatchesRequestedColorVariant()
    {
        // Aynı sayfada farklı bir rengin (farklı productCode öneki) aynı beden adı için verisi de olabilir —
        // partnumber productCode ile başlamadığı için bu, yanlışlıkla eşleşmemeli.
        var json = SizeJson(
            ("XS", "in_stock", "0332736040001-I2026", "101", null),   // renk 400 (Mavi) — istenmeyen
            ("XS", "coming_soon", "0332736067601-I2026", "101", null)); // renk 676 (Pembe) — istenen
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("03327360676", "XS", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFoundInResult_ReturnsNull()
    {
        var json = SizeJson(("36", "in_stock", "0289105442636-I2026", "36", null));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("02891054426", "50", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenPdpFetchFails_ReturnsNullAndDoesNotCache()
    {
        var (sut, _, _, redisDb) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckOnlineStockAsync("02891054426", "36", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallPdpFetcher()
    {
        var cachedJson = "[{\"Name\":\"36\",\"Stock\":\"in_stock\",\"PartNumber\":\"0289105442636-I2026\",\"MastersSizeId\":\"36\",\"ColorId\":\"426\"}]";
        var (sut, _, pdpFetcher, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync("02891054426", "36", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductSizesJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_UsesRealPartNumberAndFiltersResponseByMastersSizeId_ReturnsTrue()
    {
        var json = SizeJson(("36", "in_stock", "0289105442636-I2026", "36", null));
        var (sut, stockHandler, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK,
                // Gerçek API davranışı: mağaza başına TÜM bedenlerin stoğu dönüyor — istemci sadece
                // hedeflenen mastersSizeId'yi ("size" alanı, 36) filtrelemeli, ilk kaydı almamalı.
                """{"stocks":[{"physicalStoreId":16884,"sizeStocks":[{"sizeId":1,"size":34,"quantity":0},{"sizeId":2,"size":36,"quantity":5},{"sizeId":3,"size":38,"quantity":0}]}]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("02891054426", "36", "16884", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        var stockUri = stockHandler.RequestedUris.Should().ContainSingle().Subject;
        stockUri.Should().Contain("part-number/0289105442636");
        stockUri.Should().Contain("campaign/I2026");
        stockUri.Should().Contain("physicalStoreId=16884");
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenRequestedSizeQuantityIsZeroButOthersArePositive_ReturnsFalse()
    {
        var json = SizeJson(("36", "in_stock", "0289105442636-I2026", "36", null));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK,
                """{"stocks":[{"physicalStoreId":16884,"sizeStocks":[{"sizeId":1,"size":34,"quantity":9},{"sizeId":2,"size":36,"quantity":0}]}]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("02891054426", "36", "16884", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSizeIdDiffersFromSize_FiltersBySizeNotSizeId()
    {
        // Regresyon testi — gerçek verilerle bulunan kritik bir hatayı sabitler: alfabetik bedenli
        // ürünlerde (mastersSizeId 101+) API'nin "sizeId" alanı küçük, sıralı bir indeks (1,2,3,4...),
        // asıl mastersSizeId ile eşleşen alan "size". Sayısal bedenlerde (jean: 32,34,36) ikisi
        // tesadüfen eşit olduğu için bu hata numerik testlerle yakalanamıyordu.
        var json = SizeJson(("M", "in_stock", "0334869212203-I2026", "103", null));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK,
                """{"stocks":[{"physicalStoreId":8359,"sizeStocks":[{"sizeId":1,"size":101,"quantity":5},{"sizeId":2,"size":102,"quantity":10},{"sizeId":3,"size":103,"quantity":4},{"sizeId":4,"size":104,"quantity":3}]}]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("03348692122", "M", "8359", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenApiReturnsEmptyStocksArray_ReturnsUnknownNotFalse()
    {
        // Gerçek verilerle bulunan bir durum: bazı ürünler mağaza stok özelliğini hiç desteklemiyor
        // (sitede "Mağazada mevcudiyet" bölümü bile yok) — API "stocks":[] döner. Bunu "OutOfStock"
        // saymak yanıltıcı (kullanıcı mağazada ürünü gerçekten görebilir) — Unknown dönülmeli.
        var json = SizeJson(("42", "in_stock", "0277774780942-I2026", "174", null));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, """{"stocks":[]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("02777747809", "42", "6943", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTargetSizeMissingFromNonEmptyResponse_ReturnsUnknown()
    {
        var json = SizeJson(("36", "in_stock", "0289105442636-I2026", "36", null));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK,
                """{"stocks":[{"physicalStoreId":16884,"sizeStocks":[{"sizeId":1,"size":32,"quantity":2}]}]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("02891054426", "36", "16884", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStockApiReturnsNonSuccess_ReturnsNull()
    {
        var json = SizeJson(("36", "in_stock", "0289105442636-I2026", "36", null));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("02891054426", "36", "16884", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSizeNotFoundInResult_ReturnsNullWithoutCallingStockApi()
    {
        var json = SizeJson(("36", "in_stock", "0289105442636-I2026", "36", null));
        var (sut, stockHandler, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("02891054426", "50", "16884", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        stockHandler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveSizeEntry_MatchesByColorIdNotPartNumberPrefix_EvenWhenSizeBelongsToADifferentSkuFamily()
    {
        // Gerçek verilerle bulunan durum ("Haki" gömlek): AYNI renk (ColorId) içinde bedenler farklı
        // partnumber "aile" öneklerine dağılmış olabiliyor — XS/S/L "01337711507" ailesinden, M/XL
        // "01337015507" ailesinden. Eskiden productCode'un partnumber'ı STARTSWITH etmesi aranıyordu; bu,
        // caller "01337711507" gönderdiğinde M için hiç eşleşme bulamayıp yanlışlıkla Unknown dönüyordu.
        // ColorId eşleştirmesi renkten bağımsız aile ayrımını ortadan kaldırır.
        var json = SizeJson(
            ("XS", "in_stock", "0133771150701-I2026", "101", "507"),
            ("S", "in_stock", "0133771150702-I2026", "102", "507"),
            ("M", "in_stock", "0133701550703-I2026", "103", "507"),
            ("L", "in_stock", "0133771150704-I2026", "104", "507"),
            ("XL", "in_stock", "0133701550705-I2026", "105", "507"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        // Online kontrol, PDP'nin kendi "M" kaydını (fam2) direkt kullanabilmeli — productCode "01337711507"
        // (fam1'e ait) gönderilse bile, çünkü eşleştirme artık ColorId'ye (son 3 hane: 507) göre yapılıyor.
        var result = await sut.CheckOnlineStockAsync("01337711507", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenColorHasMultiplePartNumberFamilies_QueriesAllFamiliesAndAggregatesStock()
    {
        // Aynı canlı test senaryosu — hedef beden "M" fam2'ye ("01337015507") ait, fakat sitenin kendi
        // "Mağazada mevcudiyet" akışı (Playwright ile yakalanan gerçek ağ trafiğinde doğrulandı) HER İKİ
        // aileyi de aynı beden konumu koduyla (burada "03") sorgulayıp sonuçları birleştiriyor. Tek aile
        // sorgulanırsa (eski davranış) fam1'in o pozisyonda döndürdüğü boş/sıfır sonuç asıl stoğu maskeler.
        var json = SizeJson(
            ("XS", "in_stock", "0133771150701-I2026", "101", "507"),
            ("S", "in_stock", "0133771150702-I2026", "102", "507"),
            ("M", "in_stock", "0133701550703-I2026", "103", "507"),
            ("L", "in_stock", "0133771150704-I2026", "104", "507"),
            ("XL", "in_stock", "0133701550705-I2026", "105", "507"));

        var (sut, stockHandler, _, _) = CreateSut(
            stockResponder: request =>
            {
                var uri = request.RequestUri!.ToString();
                // fam1 ("01337711507") + "03" konum kodu bu mağazada gerçekten boş dönüyor.
                if (uri.Contains("part-number/0133771150703"))
                {
                    return FakeHttpResponses.Json(HttpStatusCode.OK, """{"stocks":[]}""");
                }
                // fam2 ("01337015507") + "03" — asıl stok burada.
                if (uri.Contains("part-number/0133701550703"))
                {
                    return FakeHttpResponses.Json(HttpStatusCode.OK,
                        """{"stocks":[{"physicalStoreId":8359,"sizeStocks":[{"sizeId":1,"size":103,"quantity":7}]}]}""");
                }
                throw new InvalidOperationException("Beklenmeyen istek: " + uri);
            },
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("01337711507", "M", "8359", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        stockHandler.RequestedUris.Should().HaveCount(2);
        stockHandler.RequestedUris.Should().Contain(u => u.Contains("part-number/0133771150703"));
        stockHandler.RequestedUris.Should().Contain(u => u.Contains("part-number/0133701550703"));
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenAllFamiliesReturnEmptyStocks_ReturnsUnknown()
    {
        var json = SizeJson(
            ("M", "in_stock", "0133701550703-I2026", "103", "507"),
            ("L", "in_stock", "0133771150704-I2026", "104", "507"));

        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, """{"stocks":[]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("01337015507", "M", "8359", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }
}
