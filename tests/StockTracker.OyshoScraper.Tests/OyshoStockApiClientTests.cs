using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.OyshoScraper.Services;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.OyshoScraper.Tests;

public class OyshoStockApiClientTests
{
    private const string ProductUrl = "https://www.oysho.com/tr/test-urun-l36613922";

    // PlaywrightOyshoFetcher'ın #oyshoServer-state script etiketinden çıkardığı, çözümlenmiş beden listesi
    // JSON şekli (bkz. IOyshoPdpFetcher.FetchProductSizesJsonAsync).
    private static string SizeJson(params (string Name, string Availability, bool HasFewUnits, string PartNumber, string MasterSizeId, string ColorId)[] entries) =>
        "[" + string.Join(",", entries.Select(e =>
            $"{{\"Name\":\"{e.Name}\",\"Availability\":\"{e.Availability}\",\"HasFewUnits\":{(e.HasFewUnits ? "true" : "false")},\"PartNumber\":\"{e.PartNumber}\",\"MasterSizeId\":\"{e.MasterSizeId}\",\"ColorId\":\"{e.ColorId}\"}}")) + "]";

    private static (OyshoStockApiClient Sut, FakeHttpMessageHandler StockHandler, Mock<IOyshoPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage>? stockResponder = null,
        string? pdpSizesJson = null,
        RedisValue cachedValue = default)
    {
        var stockHandler = new FakeHttpMessageHandler(
            stockResponder ?? (_ => throw new InvalidOperationException("Stok API'si çağrılmamalıydı.")));
        var stockHttpClient = new HttpClient(stockHandler) { BaseAddress = new Uri("https://api.inditex.com") };

        var pdpFetcher = new Mock<IOyshoPdpFetcher>();
        pdpFetcher.Setup(f => f.FetchProductSizesJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(pdpSizesJson);

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new OyshoStockApiClient(stockHttpClient, pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<OyshoStockApiClient>>());

        return (sut, stockHandler, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenInStock_ReturnsTrueAndCachesResult()
    {
        var json = SizeJson(("XXS", "in_stock", false, "3661392281408-I2026", "123", "814"));
        var (sut, _, pdpFetcher, redisDb) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("36613922/814", "XXS", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeFalse();
        pdpFetcher.Verify(f => f.FetchProductSizesJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenComingSoon_ReturnsFalse()
    {
        var json = SizeJson(("S", "coming_soon", false, "3661392281508-I2026", "102", "814"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("36613922/814", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenOutOfStock_ReturnsFalse()
    {
        var json = SizeJson(("M", "out_of_stock", false, "3661392281608-I2026", "103", "814"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("36613922/814", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenHasFewUnits_PropagatesIsLastUnitDirectlyFromApiFlag()
    {
        // Oysho'nun kendi API'si bir "az kaldı" bayrağı veriyor (Mango'nun lastUnits'iyle aynı desen) —
        // biz miktardan türetmiyoruz, doğrudan taşıyoruz.
        var json = SizeJson(("L", "in_stock", true, "3661392281708-I2026", "104", "814"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("36613922/814", "L", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeTrue();
        result.Quantity.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var json = SizeJson(("XS", "in_stock", false, "3661392281408-I2026", "101", "814"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("36613922/814", "xs", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_OnlyMatchesRequestedColorVariant()
    {
        // Aynı sayfada farklı bir rengin aynı beden adı için verisi de olabilir — ColorId farklı olduğu
        // için yanlışlıkla eşleşmemeli.
        var json = SizeJson(
            ("XS", "in_stock", false, "3661392200101-I2026", "101", "400"),   // renk 400 — istenmeyen
            ("XS", "out_of_stock", false, "3661392281401-I2026", "101", "814")); // renk 814 — istenen
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("36613922/814", "XS", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFoundInResult_ReturnsNull()
    {
        var json = SizeJson(("XXS", "in_stock", false, "3661392281408-I2026", "123", "814"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("36613922/814", "XXL", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenPdpFetchFails_ReturnsNullAndDoesNotCache()
    {
        var (sut, _, _, redisDb) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckOnlineStockAsync("36613922/814", "XXS", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallPdpFetcher()
    {
        var cachedJson = "[{\"Name\":\"XXS\",\"Availability\":\"in_stock\",\"HasFewUnits\":false,\"PartNumber\":\"3661392281408-I2026\",\"MasterSizeId\":\"123\",\"ColorId\":\"814\"}]";
        var (sut, _, pdpFetcher, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync("36613922/814", "XXS", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductSizesJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_UsesRealPartNumberAndFiltersResponseByMasterSizeId_ReturnsTrue()
    {
        var json = SizeJson(("XXS", "in_stock", false, "3661392281408-I2026", "123", "814"));
        var (sut, stockHandler, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK,
                // Gerçek API davranışı: mağaza başına TÜM bedenlerin stoğu dönüyor — istemci sadece
                // hedeflenen masterSizeId'yi ("size" alanı, 123) filtrelemeli.
                """{"stocks":[{"physicalStoreId":2371,"sizeStocks":[{"sizeId":1,"size":101,"quantity":10},{"sizeId":8,"size":123,"quantity":3}]}]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("36613922/814", "XXS", "2371", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().Be(3);
        var stockUri = stockHandler.RequestedUris.Should().ContainSingle().Subject;
        stockUri.Should().Contain("part-number/3661392281408");
        stockUri.Should().Contain("campaign/I2026");
        stockUri.Should().Contain("physicalStoreId=2371");
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenRequestedSizeQuantityIsZeroButOthersArePositive_ReturnsFalse()
    {
        var json = SizeJson(("XXS", "in_stock", false, "3661392281408-I2026", "123", "814"));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK,
                """{"stocks":[{"physicalStoreId":2371,"sizeStocks":[{"sizeId":1,"size":101,"quantity":9},{"sizeId":8,"size":123,"quantity":0}]}]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("36613922/814", "XXS", "2371", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSizeIdDiffersFromSize_FiltersBySizeNotSizeId()
    {
        // Regresyon testi — Bershka'da gerçek verilerle bulunan hatayla aynı gerekçe: "sizeId" küçük,
        // sıralı bir indeks; asıl eşleşme alanı PDP'nin masterSizeId'siyle birebir aynı olan "size".
        var json = SizeJson(("M", "in_stock", false, "3661392281608-I2026", "103", "814"));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK,
                """{"stocks":[{"physicalStoreId":7329,"sizeStocks":[{"sizeId":1,"size":101,"quantity":5},{"sizeId":3,"size":103,"quantity":4}]}]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("36613922/814", "M", "7329", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().Be(4);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenApiReturnsEmptyStocksArray_ReturnsUnknownNotFalse()
    {
        var json = SizeJson(("XXS", "in_stock", false, "3661392281408-I2026", "123", "814"));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, """{"stocks":[]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("36613922/814", "XXS", "2410", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTargetSizeMissingFromNonEmptyResponse_ReturnsUnknown()
    {
        var json = SizeJson(("XXS", "in_stock", false, "3661392281408-I2026", "123", "814"));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK,
                """{"stocks":[{"physicalStoreId":2371,"sizeStocks":[{"sizeId":1,"size":101,"quantity":2}]}]}"""),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("36613922/814", "XXS", "2371", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStockApiReturnsNonSuccess_ReturnsNull()
    {
        var json = SizeJson(("XXS", "in_stock", false, "3661392281408-I2026", "123", "814"));
        var (sut, _, _, _) = CreateSut(
            stockResponder: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("36613922/814", "XXS", "2371", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSizeNotFoundInResult_ReturnsNullWithoutCallingStockApi()
    {
        var json = SizeJson(("XXS", "in_stock", false, "3661392281408-I2026", "123", "814"));
        var (sut, stockHandler, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("36613922/814", "XXL", "2371", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        stockHandler.RequestedUris.Should().BeEmpty();
    }
}
