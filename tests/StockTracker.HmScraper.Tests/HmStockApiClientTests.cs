using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.HmScraper.Services;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.HmScraper.Tests;

public class HmStockApiClientTests
{
    private const string ProductUrl = "https://www2.hm.com/tr_tr/productpage.1351887001.html";

    private static string SizeJson(params (string Name, string SizeCode)[] entries) =>
        "[" + string.Join(",", entries.Select(e =>
            $"{{\"Name\":\"{e.Name}\",\"SizeCode\":\"{e.SizeCode}\"}}")) + "]";

    private static (HmStockApiClient Sut, FakeHttpMessageHandler AvailabilityHandler, Mock<IHmPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage>? availabilityResponder = null,
        string? pdpSizesJson = null,
        RedisValue cachedValue = default,
        string? storeAvailabilityJson = null)
    {
        var availabilityHandler = new FakeHttpMessageHandler(
            availabilityResponder ?? (_ => throw new InvalidOperationException("Online stok API'si çağrılmamalıydı.")));
        var availabilityHttpClient = new HttpClient(availabilityHandler) { BaseAddress = new Uri("https://ofg.hm.com") };

        var pdpFetcher = new Mock<IHmPdpFetcher>();
        pdpFetcher.Setup(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(pdpSizesJson);
        pdpFetcher.Setup(f => f.FetchStoreAvailabilityJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storeAvailabilityJson);

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new HmStockApiClient(availabilityHttpClient, pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<HmStockApiClient>>());

        return (sut, availabilityHandler, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSkuInAvailabilityList_ReturnsTrueAndCachesSizeMap()
    {
        var pdpJson = SizeJson(("S", "003"));
        var availabilityJson = """{"availability":["1351887001003"],"fewPieceLeft":[]}""";
        var (sut, handler, pdpFetcher, redisDb) = CreateSut(pdpSizesJson: pdpJson, availabilityResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, availabilityJson));

        var result = await sut.CheckOnlineStockAsync("1351887/001", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeFalse();
        result.Quantity.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
        handler.RequestedUris.Should().ContainSingle().Which.Should().Contain("/pdh-availability/v1/product/tr/availability/1351887");
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSkuInFewPieceLeft_ReturnsTrueWithIsLastUnit()
    {
        // Canlı doğrulandı: "fewPieceLeft" dizisi, "availability" dizisinin bir alt kümesi — hâlâ satın
        // alınabilir ama az kaldı uyarısı taşıyor.
        var pdpJson = SizeJson(("XS", "002"));
        var availabilityJson = """{"availability":["1351887001002"],"fewPieceLeft":["1351887001002"]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, availabilityResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, availabilityJson));

        var result = await sut.CheckOnlineStockAsync("1351887/001", "XS", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeTrue();
        result.Quantity.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSkuNotInAvailabilityList_ReturnsFalse()
    {
        var pdpJson = SizeJson(("M", "004"));
        var availabilityJson = """{"availability":["1351887001003"],"fewPieceLeft":[]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, availabilityResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, availabilityJson));

        var result = await sut.CheckOnlineStockAsync("1351887/001", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var pdpJson = SizeJson(("S", "003"));
        var availabilityJson = """{"availability":["1351887001003"],"fewPieceLeft":[]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, availabilityResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, availabilityJson));

        var result = await sut.CheckOnlineStockAsync("1351887/001", "s", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFoundInPdpMap_ReturnsNullWithoutCallingAvailabilityApi()
    {
        var pdpJson = SizeJson(("S", "003"));
        var (sut, handler, _, _) = CreateSut(pdpSizesJson: pdpJson);

        var result = await sut.CheckOnlineStockAsync("1351887/001", "XXL", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        handler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenPdpFetchFails_ReturnsNullAndDoesNotCache()
    {
        var (sut, _, _, redisDb) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckOnlineStockAsync("1351887/001", "S", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallPdpFetcher()
    {
        var cachedJson = "[{\"Name\":\"S\",\"SizeCode\":\"003\"}]";
        var availabilityJson = """{"availability":["1351887001003"],"fewPieceLeft":[]}""";
        var (sut, _, pdpFetcher, _) = CreateSut(cachedValue: cachedJson, availabilityResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, availabilityJson));

        var result = await sut.CheckOnlineStockAsync("1351887/001", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenApiReturnsNonSuccess_ReturnsNull()
    {
        var pdpJson = SizeJson(("S", "003"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, availabilityResponder: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sut.CheckOnlineStockAsync("1351887/001", "S", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTrafficLightGreen_ReturnsTrue()
    {
        var pdpJson = SizeJson(("S", "003"));
        var storeJson = """{"stores":[{"storeCode":"TR0030","sizes":{"size":[{"sizeCode":"003","avaiQty":1000,"traffLightInd":"G"}]}}]}""";
        var (sut, _, pdpFetcher, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeFalse();
        // Faz 6.1 — kritik: avaiQty gerçek bir miktar değil (canlı doğrulandı, yalnızca 0/1000/2000/3000
        // gözlemlendi), bu yüzden Quantity HER ZAMAN null kalmalı, 1000 değil.
        result.Quantity.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchStoreAvailabilityJsonAsync(ProductUrl, "1351887", "001", 40.96, 29.08, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTrafficLightYellow_ReturnsTrueWithIsLastUnit()
    {
        var pdpJson = SizeJson(("S", "003"));
        var storeJson = """{"stores":[{"storeCode":"TR0030","sizes":{"size":[{"sizeCode":"003","avaiQty":0,"traffLightInd":"Y"}]}}]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTrafficLightRed_ReturnsFalse()
    {
        var pdpJson = SizeJson(("S", "003"));
        var storeJson = """{"stores":[{"storeCode":"TR0030","sizes":{"size":[{"sizeCode":"003","avaiQty":0,"traffLightInd":"R"}]}}]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
        result.IsLastUnit.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTargetStoreMissingFromResponse_ReturnsUnknownNotFalse()
    {
        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ (Zara'nın TERSİ): H&M yanıtı seyrek değil, yarıçap içindeki
        // TÜM mağazaları (stoksuz olanlar dahil, açık R ile) döner. Hedef mağaza hiç yoksa bu bir sorgu
        // sorunudur (yanlış yarıçap/koordinat) — "o mağazada yok" değil "bilmiyoruz" anlamına gelir.
        var pdpJson = SizeJson(("S", "003"));
        var storeJson = """{"stores":[{"storeCode":"TR9999","sizes":{"size":[{"sizeCode":"003","avaiQty":1000,"traffLightInd":"G"}]}}]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSizeCodeMissingFromStoreEntry_ReturnsUnknown()
    {
        var pdpJson = SizeJson(("S", "003"));
        var storeJson = """{"stores":[{"storeCode":"TR0030","sizes":{"size":[{"sizeCode":"004","avaiQty":1000,"traffLightInd":"G"}]}}]}""";
        var (sut, _, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenProductCodeMalformed_ReturnsNullWithoutCallingFetcher()
    {
        var (sut, _, pdpFetcher, _) = CreateSut();

        var result = await sut.CheckStoreStockAsync("not-a-valid-code", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchStoreAvailabilityJsonAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenPdpSizeUnresolvable_ReturnsNullWithoutCallingStoreApi()
    {
        var (sut, _, pdpFetcher, _) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchStoreAvailabilityJsonAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
