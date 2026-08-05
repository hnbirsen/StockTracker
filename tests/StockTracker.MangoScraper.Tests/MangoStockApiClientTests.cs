using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.MangoScraper.Services;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MangoScraper.Tests;

public class MangoStockApiClientTests
{
    private const string ProductUrl = "https://shop.mango.com/tr/tr/p/kad%C4%B1n/elbise-ve-tulum/dugun-davetlisi/asimetrik-saten-elbise/37013869/56/00";

    private static string SizeJson(params (string Name, bool Available, string ColorId)[] entries) =>
        "[" + string.Join(",", entries.Select(e =>
            $"{{\"Name\":\"{e.Name}\",\"Available\":{(e.Available ? "true" : "false")},\"ColorId\":\"{e.ColorId}\"}}")) + "]";

    private static (MangoStockApiClient Sut, FakeHttpMessageHandler StoreHandler, Mock<IMangoPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage>? storeResponder = null,
        string? pdpSizesJson = null,
        RedisValue cachedValue = default)
    {
        var storeHandler = new FakeHttpMessageHandler(
            storeResponder ?? (_ => throw new InvalidOperationException("Mağaza API'si çağrılmamalıydı.")));
        var storeHttpClient = new HttpClient(storeHandler) { BaseAddress = new Uri("https://api.shop.mango.com") };

        var pdpFetcher = new Mock<IMangoPdpFetcher>();
        pdpFetcher.Setup(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(pdpSizesJson);

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new MangoStockApiClient(storeHttpClient, pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<MangoStockApiClient>>());

        return (sut, storeHandler, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenAvailableTrue_ReturnsTrueAndCachesResult()
    {
        var json = SizeJson(("S", true, "56"));
        var (sut, _, pdpFetcher, redisDb) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("37013869/56", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenAvailableFalse_ReturnsFalse()
    {
        var json = SizeJson(("M", false, "56"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("37013869/56", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var json = SizeJson(("S", true, "56"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("37013869/56", "s", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_OnlyMatchesRequestedColorVariant()
    {
        var json = SizeJson(("S", true, "77"), ("S", false, "56"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("37013869/56", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFoundInResult_ReturnsNull()
    {
        var json = SizeJson(("S", true, "56"));
        var (sut, _, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("37013869/56", "XXL", ProductUrl, CancellationToken.None);

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

        var result = await sut.CheckOnlineStockAsync("37013869/56", "S", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallPdpFetcher()
    {
        var cachedJson = "[{\"Name\":\"S\",\"Available\":true,\"ColorId\":\"56\"}]";
        var (sut, _, pdpFetcher, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync("37013869/56", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStoreCarriesSize_ReturnsTrue()
    {
        var storeJson = """{"stores":[{"id":"10389","sizes":["S","M"]}]}""";
        var (sut, handler, _, _) = CreateSut(storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("37013869/56", "S", "10389", 40.96, 29.08, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        var requestedUri = handler.RequestedUris.Should().ContainSingle().Subject;
        requestedUri.Should().Contain("colorId=56");
        requestedUri.Should().Contain("productId=37013869");
        requestedUri.Should().Contain("latitude=40.96");
        requestedUri.Should().Contain("longitude=29.08");
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenApiReturnsIsLastUnitFlag_PropagatesItAndQuantityStaysNull()
    {
        // Faz 6.1 — kullanıcı talebiyle eklendi: Mango sayısal miktar vermiyor ama `sizesIds[].isLastUnit`
        // adında doğrudan bir bayrak veriyor (canlı doğrulandı) — Quantity her zaman null kalmalı.
        var storeJson = """{"stores":[{"id":"10389","sizes":["S"],"sizesIds":[{"id":"20","name":"S","isLastUnit":true}]}]}""";
        var (sut, _, _, _) = CreateSut(storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("37013869/56", "S", "10389", 40.96, 29.08, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStoreDoesNotCarrySize_ReturnsFalse()
    {
        var storeJson = """{"stores":[{"id":"10389","sizes":["M"]}]}""";
        var (sut, _, _, _) = CreateSut(storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("37013869/56", "S", "10389", 40.96, 29.08, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenQueriedStoreMissingFromSparseResponse_ReturnsFalseNotUnknown()
    {
        // CANLI VERİYLE DOĞRULANAN DAVRANIŞ (Cevahir AVM örneği — bkz. .claude/ARCHITECTURE.md > Mango
        // Scraper): dizide hiç yer almayan, ama GERÇEKTEN VAR OLAN bir mağaza, o ürünü/rengi taşımadığı
        // anlamına geliyor — Unknown değil, False.
        var storeJson = """{"stores":[{"id":"99999","sizes":["S"]}]}""";
        var (sut, _, _, _) = CreateSut(storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("37013869/56", "S", "10389", 40.96, 29.08, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_SizeMatchIsCaseInsensitive()
    {
        var storeJson = """{"stores":[{"id":"10389","sizes":["S"]}]}""";
        var (sut, _, _, _) = CreateSut(storeResponder: _ => FakeHttpResponses.Json(HttpStatusCode.OK, storeJson));

        var result = await sut.CheckStoreStockAsync("37013869/56", "s", "10389", 40.96, 29.08, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenApiReturnsNonSuccess_ReturnsNull()
    {
        var (sut, _, _, _) = CreateSut(storeResponder: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sut.CheckStoreStockAsync("37013869/56", "S", "10389", 40.96, 29.08, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenProductCodeMalformed_ReturnsNullWithoutCallingApi()
    {
        var (sut, handler, _, _) = CreateSut();

        var result = await sut.CheckStoreStockAsync("not-a-valid-code", "S", "10389", 40.96, 29.08, CancellationToken.None);

        result.Should().BeNull();
        handler.RequestedUris.Should().BeEmpty();
    }
}
