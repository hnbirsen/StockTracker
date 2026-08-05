using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.PullBearScraper.Services;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.PullBearScraper.Tests;

public class PullBearStockApiClientTests
{
    private const string ProductUrl = "https://www.pullandbear.com/tr/dantel-detayli-beyaz-bluz-l07460338?cS=250&pelement=750312599";

    private static string SizeJson(params (string Name, string ColorId, string CatEntryId, string MastersSizeId, bool IsBuyable)[] entries) =>
        "[" + string.Join(",", entries.Select(e =>
            $"{{\"Name\":\"{e.Name}\",\"ColorId\":\"{e.ColorId}\",\"CatEntryId\":\"{e.CatEntryId}\",\"MastersSizeId\":\"{e.MastersSizeId}\",\"IsBuyable\":{(e.IsBuyable ? "true" : "false")},\"BackSoon\":\"0\"}}")) + "]";

    private static (PullBearStockApiClient Sut, FakeHttpMessageHandler StoreHandler, Mock<IPullBearPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage>? storeResponder = null,
        string? pdpSizesJson = null,
        RedisValue cachedValue = default)
    {
        var storeHandler = new FakeHttpMessageHandler(
            storeResponder ?? (_ => throw new InvalidOperationException("Mağaza stok API'si çağrılmamalıydı.")));
        var storeHttpClient = new HttpClient(storeHandler) { BaseAddress = new Uri("https://www.pullandbear.com") };

        var pdpFetcher = new Mock<IPullBearPdpFetcher>();
        pdpFetcher.Setup(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(pdpSizesJson);

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new PullBearStockApiClient(storeHttpClient, pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<PullBearStockApiClient>>());

        return (sut, storeHandler, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenIsBuyableTrue_ReturnsTrueAndCachesResult()
    {
        var json = SizeJson(("S", "250", "750312599", "102", true));
        var (sut, _, pdpFetcher, redisDb) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("07460338/250", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenIsBuyableFalse_ReturnsFalse()
    {
        var json = SizeJson(("M", "250", "750312599", "103", false));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("07460338/250", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var json = SizeJson(("S", "250", "750312599", "102", true));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("07460338/250", "s", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_OnlyMatchesRequestedColorVariant()
    {
        var json = SizeJson(("S", "800", "750312600", "102", true), ("S", "250", "750312599", "102", false));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("07460338/250", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFoundInResult_ReturnsNull()
    {
        var json = SizeJson(("S", "250", "750312599", "102", true));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("07460338/250", "XXL", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenProductCodeMalformed_ReturnsNull()
    {
        var (sut, _, _, _) = CreateSut();

        var result = await sut.CheckOnlineStockAsync("not-a-valid-code", "S", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenPdpFetchFails_ReturnsNullAndDoesNotCache()
    {
        var (sut, _, _, redisDb) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckOnlineStockAsync("07460338/250", "S", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallPdpFetcher()
    {
        var cachedJson = "[{\"Name\":\"S\",\"ColorId\":\"250\",\"CatEntryId\":\"750312599\",\"MastersSizeId\":\"102\",\"IsBuyable\":true,\"BackSoon\":\"0\"}]";
        var (sut, _, pdpFetcher, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync("07460338/250", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStoreCarriesSize_ReturnsTrueWithRealQuantity()
    {
        var pdpJson = SizeJson(("M", "250", "750312599", "103", true));
        var storeJson = """{"productId":750312599,"sizesAvailableByPhysicalStores":[{"physicalStoreId":5287,"sizesAvailability":[{"sizeId":"103","size":"M","stock":6}]}]}""";
        var (sut, handler, _, _) = CreateSut(pdpSizesJson: pdpJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("07460338/250", "M", "5287", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().Be(6);
        result.IsLastUnit.Should().BeFalse();
        var requestedUri = handler.RequestedUris.Should().ContainSingle().Subject;
        requestedUri.Should().Contain("/products/750312599/available-sizes");
        requestedUri.Should().Contain("physicalStoreIds=5287");
        requestedUri.Should().Contain("sizeIds=103");
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStockIsOne_IsLastUnitTrue()
    {
        var pdpJson = SizeJson(("M", "250", "750312599", "103", true));
        var storeJson = """{"productId":750312599,"sizesAvailableByPhysicalStores":[{"physicalStoreId":5287,"sizesAvailability":[{"sizeId":"103","size":"M","stock":1}]}]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("07460338/250", "M", "5287", ProductUrl, CancellationToken.None);

        result!.Quantity.Should().Be(1);
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenQueriedStoreMissingFromSparseResponse_ReturnsFalseNotUnknown()
    {
        var pdpJson = SizeJson(("M", "250", "750312599", "103", true));
        var storeJson = """{"productId":750312599,"sizesAvailableByPhysicalStores":[]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("07460338/250", "M", "16941", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenApiReturnsNonSuccess_ReturnsNull()
    {
        var pdpJson = SizeJson(("M", "250", "750312599", "103", true));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeResponder: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sut.CheckStoreStockAsync("07460338/250", "M", "5287", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSizeEntryCannotBeResolved_ReturnsNullWithoutCallingApi()
    {
        var (sut, handler, _, _) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckStoreStockAsync("07460338/250", "M", "5287", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        handler.RequestedUris.Should().BeEmpty();
    }
}
