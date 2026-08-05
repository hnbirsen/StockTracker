using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;
using StockTracker.StradivariusScraper.Services;

namespace StockTracker.StradivariusScraper.Tests;

public class StradivariusStockApiClientTests
{
    private const string ProductUrl = "https://www.stradivarius.com/tr/asimetrik-kareli-midi-elbise-l06383188";

    private static string PdpJson(string? accessToken, params (string Name, string? Sku, bool Disabled, bool LowStock)[] sizes) =>
        $$"""
        {"AccessToken":{{(accessToken is null ? "null" : $"\"{accessToken}\"")}},"Sizes":[{{string.Join(",", sizes.Select(e =>
            $"{{\"Name\":\"{e.Name}\",\"Sku\":{(e.Sku is null ? "null" : $"\"{e.Sku}\"")},\"Disabled\":{(e.Disabled ? "true" : "false")},\"LowStock\":{(e.LowStock ? "true" : "false")}}}"))}}]}
        """;

    private static (StradivariusStockApiClient Sut, FakeHttpMessageHandler StoreHandler, Mock<IStradivariusPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage>? storeResponder = null,
        string? pdpJson = null,
        RedisValue cachedValue = default)
    {
        var storeHandler = new FakeHttpMessageHandler(
            storeResponder ?? (_ => throw new InvalidOperationException("Mağaza stok API'si çağrılmamalıydı.")));
        var storeHttpClient = new HttpClient(storeHandler) { BaseAddress = new Uri("https://www.stradivarius.com") };

        var pdpFetcher = new Mock<IStradivariusPdpFetcher>();
        pdpFetcher.Setup(f => f.FetchOnlineSizesJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(pdpJson);

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new StradivariusStockApiClient(storeHttpClient, pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<StradivariusStockApiClient>>());

        return (sut, storeHandler, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotDisabled_ReturnsInStockAndCaches()
    {
        var json = PdpJson("tok", ("M", "511320507", false, false));
        var (sut, _, fetcher, redisDb) = CreateSut(pdpJson: json);

        var result = await sut.CheckOnlineStockAsync("06383188", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeFalse();
        fetcher.Verify(f => f.FetchOnlineSizesJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeDisabled_ReturnsOutOfStock()
    {
        var json = PdpJson("tok", ("S", "511320506", true, false));
        var (sut, _, _, _) = CreateSut(pdpJson: json);

        var result = await sut.CheckOnlineStockAsync("06383188", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenLowStockWarningPresent_SetsIsLastUnitTrue()
    {
        var json = PdpJson("tok", ("XS", "511320508", false, true));
        var (sut, _, _, _) = CreateSut(pdpJson: json);

        var result = await sut.CheckOnlineStockAsync("06383188", "XS", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var json = PdpJson("tok", ("M", "511320507", false, false));
        var (sut, _, _, _) = CreateSut(pdpJson: json);

        var result = await sut.CheckOnlineStockAsync("06383188", "m", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFound_ReturnsNull()
    {
        var json = PdpJson("tok", ("M", "511320507", false, false));
        var (sut, _, _, _) = CreateSut(pdpJson: json);

        var result = await sut.CheckOnlineStockAsync("06383188", "XXL", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenFetchFails_ReturnsNullAndDoesNotCache()
    {
        var (sut, _, _, redisDb) = CreateSut(pdpJson: null);

        var result = await sut.CheckOnlineStockAsync("06383188", "M", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallFetcher()
    {
        var cachedJson = PdpJson("tok", ("M", "511320507", false, false));
        var (sut, _, fetcher, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync("06383188", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        fetcher.Verify(f => f.FetchOnlineSizesJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStoreCarriesSku_ReturnsTrueWithRealQuantityAndBearerToken()
    {
        var pdpJson = PdpJson("guest-token-123", ("M", "511311780", false, false));
        var storeJson = """{"16879":{"511311780":{"size":"M","stock":5,"isOnSale":false,"stockByLocation":{"INTERNAL_WAREHOUSE":5}}}}""";
        var (sut, handler, _, _) = CreateSut(pdpJson: pdpJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("06383188", "M", "16879", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().Be(5);
        result.IsLastUnit.Should().BeFalse();
        var requestedUri = handler.RequestedUris.Should().ContainSingle().Subject;
        requestedUri.Should().Contain("/skus-availability-in-stores/actions/filter");
        handler.RequestedBodies.Single().Should().Contain("16879").And.Contain("511311780");
        handler.AuthorizationHeaders.Single().Should().Be("Bearer guest-token-123");
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStockIsOne_IsLastUnitTrue()
    {
        var pdpJson = PdpJson("tok", ("M", "511311780", false, false));
        var storeJson = """{"16879":{"511311780":{"size":"M","stock":1,"isOnSale":false,"stockByLocation":{"SALES_AREA":1}}}}""";
        var (sut, _, _, _) = CreateSut(pdpJson: pdpJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("06383188", "M", "16879", ProductUrl, CancellationToken.None);

        result!.Quantity.Should().Be(1);
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenQueriedStoreMissingFromSparseResponse_ReturnsFalseNotUnknown()
    {
        var pdpJson = PdpJson("tok", ("M", "511311780", false, false));
        var storeJson = """{"2859":{"511311780":{"size":"M","stock":3,"isOnSale":false,"stockByLocation":{"INTERNAL_WAREHOUSE":3}}}}""";
        var (sut, _, _, _) = CreateSut(pdpJson: pdpJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("06383188", "M", "16879", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenApiReturnsNonSuccess_ReturnsNull()
    {
        var pdpJson = PdpJson("tok", ("M", "511311780", false, false));
        var (sut, _, _, _) = CreateSut(pdpJson: pdpJson, storeResponder: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sut.CheckStoreStockAsync("06383188", "M", "16879", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenAccessTokenMissing_ReturnsNullWithoutCallingApi()
    {
        var pdpJson = PdpJson(null, ("M", "511311780", false, false));
        var (sut, handler, _, _) = CreateSut(pdpJson: pdpJson);

        var result = await sut.CheckStoreStockAsync("06383188", "M", "16879", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        handler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSkuCannotBeResolved_ReturnsNullWithoutCallingApi()
    {
        var (sut, handler, _, _) = CreateSut(pdpJson: null);

        var result = await sut.CheckStoreStockAsync("06383188", "M", "16879", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        handler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckStoreStockAsync_ReusesCachedPdpData_DoesNotCallFetcherTwice()
    {
        var cachedJson = PdpJson("tok", ("M", "511311780", false, false));
        var storeJson = """{"16879":{"511311780":{"size":"M","stock":5,"isOnSale":false,"stockByLocation":{"INTERNAL_WAREHOUSE":5}}}}""";
        var (sut, _, fetcher, _) = CreateSut(cachedValue: cachedJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("06383188", "M", "16879", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        fetcher.Verify(f => f.FetchOnlineSizesJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
