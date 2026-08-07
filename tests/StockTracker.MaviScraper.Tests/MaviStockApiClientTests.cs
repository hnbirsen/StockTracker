using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.MaviScraper.Services;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MaviScraper.Tests;

public class MaviStockApiClientTests
{
    private const string ProductUrl = "https://www.mavi.com/florida-iconic-puslu-acik-mavi-jean-pantolon/p/1010381-A4216";

    // PlaywrightMaviFetcher'ın sayfadaki `sizeVariantJson` global değişkeninden çıkardığı, çözümlenmiş
    // beden/boy listesi JSON şekli (bkz. IMaviPdpFetcher.FetchProductSizesJsonAsync).
    private static string SizeJson(params (string Size, string Length, string Barcode, int StockLevel, string StockLevelStatus)[] entries) =>
        "[" + string.Join(",", entries.Select(e =>
            $"{{\"Size\":\"{e.Size}\",\"Length\":\"{e.Length}\",\"Barcode\":\"{e.Barcode}\",\"StockLevel\":{e.StockLevel},\"StockLevelStatus\":\"{e.StockLevelStatus}\"}}")) + "]";

    private static (MaviStockApiClient Sut, Mock<IMaviPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        string? pdpSizesJson = null,
        string? storeAvailabilityJson = null,
        RedisValue cachedValue = default)
    {
        var pdpFetcher = new Mock<IMaviPdpFetcher>();
        pdpFetcher.Setup(f => f.FetchProductSizesJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(pdpSizesJson);
        pdpFetcher.Setup(f => f.FetchStoreAvailabilityJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storeAvailabilityJson);

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new MaviStockApiClient(pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<MaviStockApiClient>>());

        return (sut, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenTwoDimensionalSizeInStock_ReturnsTrueWithRealQuantity()
    {
        var json = SizeJson(("30", "32", "8685099283361", 5, "inStock"));
        var (sut, pdpFetcher, redisDb) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1010381-A4216", "30/32", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().Be(5);
        pdpFetcher.Verify(f => f.FetchProductSizesJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenOutOfStock_ReturnsFalseWithNullQuantity()
    {
        var json = SizeJson(("22", "26", "8685099283286", 0, "outOfStock"));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1010381-A4216", "22/26", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
        result.Quantity.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenQuantityIsOne_DerivesIsLastUnitTrue()
    {
        var json = SizeJson(("24", "30", "8685099283422", 1, "inStock"));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1010381-A4216", "24/30", ProductUrl, CancellationToken.None);

        result!.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSingleDimensionSize_MatchesWithEmptyLength()
    {
        // Tişört gibi tek boyutlu ürünlerde "length" alanı boş string olarak geliyor — çağıran taraf
        // sadece "M" gönderirse (ayraç olmadan) bununla eşleşmeli.
        var json = SizeJson(("M", "", "8685099999999", 3, "inStock"));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1023456-B1234", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFoundInResult_ReturnsNull()
    {
        var json = SizeJson(("30", "32", "8685099283361", 5, "inStock"));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1010381-A4216", "99/99", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenPdpFetchFails_ReturnsNullAndDoesNotCache()
    {
        var (sut, _, redisDb) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckOnlineStockAsync("1010381-A4216", "30/32", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallPdpFetcher()
    {
        var cachedJson = "[{\"Size\":\"30\",\"Length\":\"32\",\"Barcode\":\"8685099283361\",\"StockLevel\":5,\"StockLevelStatus\":\"inStock\"}]";
        var (sut, pdpFetcher, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync("1010381-A4216", "30/32", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductSizesJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTargetStoreInResults_ReturnsTrueWithNullQuantity()
    {
        var json = SizeJson(("30", "32", "8685099283361", 5, "inStock"));
        var storeJson = """{"allStoreData":[{"pagination":{"totalNumberOfResults":1},"results":[{"storeId":"507"}]}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: json, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1010381-A4216", "30/32", "507", 41.063595, 28.992115, ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTargetStoreNotInSparseResults_ReturnsFalseNotUnknown()
    {
        // Canlı doğrulanan davranış: sorgulanan mağaza dizide yoksa o barkodun o mağazada YOK demek.
        var json = SizeJson(("30", "32", "8685099283361", 5, "inStock"));
        var storeJson = """{"allStoreData":[{"pagination":{"totalNumberOfResults":1},"results":[{"storeId":"823"}]}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: json, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1010381-A4216", "30/32", "507", 41.063595, 28.992115, ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStoreQueryFails_ReturnsNull()
    {
        var json = SizeJson(("30", "32", "8685099283361", 5, "inStock"));
        var (sut, _, _) = CreateSut(pdpSizesJson: json, storeAvailabilityJson: null);

        var result = await sut.CheckStoreStockAsync("1010381-A4216", "30/32", "507", 41.063595, 28.992115, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSizeNotFoundInResult_ReturnsNullWithoutCallingStoreApi()
    {
        var json = SizeJson(("30", "32", "8685099283361", 5, "inStock"));
        var (sut, pdpFetcher, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckStoreStockAsync("1010381-A4216", "99/99", "507", 41.063595, 28.992115, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchStoreAvailabilityJsonAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
