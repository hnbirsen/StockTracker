using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.MassimoDuttiScraper.Services;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MassimoDuttiScraper.Tests;

public class MassimoDuttiStockApiClientTests
{
    private const string ProductUrl = "https://www.massimodutti.com/tr/100-pamuklu-uzun-kollu-tshirt-l06244810?pelement=62327597";

    private static string SizeJson(params (string Name, string ColorId, string CatEntryId, string MastersSizeId, bool IsBuyable)[] entries) =>
        "[" + string.Join(",", entries.Select(e =>
            $"{{\"Name\":\"{e.Name}\",\"ColorId\":\"{e.ColorId}\",\"CatEntryId\":\"{e.CatEntryId}\",\"MastersSizeId\":\"{e.MastersSizeId}\",\"IsBuyable\":{(e.IsBuyable ? "true" : "false")},\"BackSoon\":\"0\"}}")) + "]";

    private static (MassimoDuttiStockApiClient Sut, FakeHttpMessageHandler StoreHandler, Mock<IMassimoDuttiPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage>? storeResponder = null,
        string? pdpSizesJson = null,
        RedisValue cachedValue = default)
    {
        var storeHandler = new FakeHttpMessageHandler(
            storeResponder ?? (_ => throw new InvalidOperationException("Mağaza stok API'si çağrılmamalıydı.")));
        var storeHttpClient = new HttpClient(storeHandler) { BaseAddress = new Uri("https://www.massimodutti.com") };

        var pdpFetcher = new Mock<IMassimoDuttiPdpFetcher>();
        pdpFetcher.Setup(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(pdpSizesJson);

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new MassimoDuttiStockApiClient(storeHttpClient, pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<MassimoDuttiStockApiClient>>());

        return (sut, storeHandler, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenIsBuyableTrue_ReturnsTrueAndCachesResult()
    {
        var json = SizeJson(("S", "251", "62327597", "101", true));
        var (sut, _, pdpFetcher, redisDb) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("06244810/251", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenIsBuyableFalse_ReturnsFalse()
    {
        var json = SizeJson(("M", "251", "62327597", "102", false));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("06244810/251", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var json = SizeJson(("S", "251", "62327597", "101", true));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("06244810/251", "s", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_OnlyMatchesRequestedColorVariant()
    {
        var json = SizeJson(("S", "800", "62327598", "101", true), ("S", "251", "62327597", "101", false));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("06244810/251", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFoundInResult_ReturnsNull()
    {
        var json = SizeJson(("S", "251", "62327597", "101", true));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("06244810/251", "XXL", ProductUrl, CancellationToken.None);

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

        var result = await sut.CheckOnlineStockAsync("06244810/251", "S", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallPdpFetcher()
    {
        var cachedJson = "[{\"Name\":\"S\",\"ColorId\":\"251\",\"CatEntryId\":\"62327597\",\"MastersSizeId\":\"101\",\"IsBuyable\":true,\"BackSoon\":\"0\"}]";
        var (sut, _, pdpFetcher, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync("06244810/251", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStoreCarriesSize_ReturnsTrueWithRealQuantity()
    {
        // CANLI VERİYLE DOĞRULANAN GERÇEK ŞEKİL (bkz. MassimoDuttiStockApiClient üstündeki yorum) — API gerçek
        // sayısal stok adedi veriyor.
        var pdpJson = SizeJson(("36", "700", "64522552", "36", true));
        var storeJson = """{"productId":64522552,"sizesAvailableByPhysicalStores":[{"physicalStoreId":12013,"sizesAvailability":[{"sizeId":"36","size":"36","stock":1}]}]}""";
        var (sut, handler, _, _) = CreateSut(pdpSizesJson: pdpJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("06652511/700", "36", "12013", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().Be(1);
        result.IsLastUnit.Should().BeTrue();
        var requestedUri = handler.RequestedUris.Should().ContainSingle().Subject;
        requestedUri.Should().Contain("/products/64522552/available-sizes");
        requestedUri.Should().Contain("physicalStoreIds=12013");
        requestedUri.Should().Contain("sizeIds=36");
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStockGreaterThanOne_IsLastUnitFalse()
    {
        var pdpJson = SizeJson(("36", "700", "64522552", "36", true));
        var storeJson = """{"productId":64522552,"sizesAvailableByPhysicalStores":[{"physicalStoreId":12013,"sizesAvailability":[{"sizeId":"36","size":"36","stock":3}]}]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("06652511/700", "36", "12013", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().Be(3);
        result.IsLastUnit.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenQueriedStoreMissingFromSparseResponse_ReturnsFalseNotUnknown()
    {
        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ (Cevahir/Şişli örneği — bkz. .claude/ARCHITECTURE.md > Massimo
        // Dutti Scraper): dizide hiç yer almayan bir mağaza, o bedenin o mağazada YOK demek — Unknown değil.
        var pdpJson = SizeJson(("36", "700", "64522552", "36", true));
        var storeJson = """{"productId":64522552,"sizesAvailableByPhysicalStores":[]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("06652511/700", "36", "4483", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenApiReturnsNonSuccess_ReturnsNull()
    {
        var pdpJson = SizeJson(("36", "700", "64522552", "36", true));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeResponder: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sut.CheckStoreStockAsync("06652511/700", "36", "12013", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSizeEntryCannotBeResolved_ReturnsNullWithoutCallingApi()
    {
        var (sut, handler, _, _) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckStoreStockAsync("06652511/700", "36", "12013", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        handler.RequestedUris.Should().BeEmpty();
    }
}
