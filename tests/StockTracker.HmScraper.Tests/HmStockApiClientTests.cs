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

    private static string SizeJson(params (string Name, string SizeCode, bool Available, bool FewPieceLeft)[] entries) =>
        "[" + string.Join(",", entries.Select(e =>
            $"{{\"Name\":\"{e.Name}\",\"SizeCode\":\"{e.SizeCode}\",\"Available\":{(e.Available ? "true" : "false")},\"FewPieceLeft\":{(e.FewPieceLeft ? "true" : "false")}}}")) + "]";

    private static (HmStockApiClient Sut, Mock<IHmPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        string? pdpSizesJson = null,
        RedisValue cachedValue = default,
        string? storeAvailabilityJson = null)
    {
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

        var sut = new HmStockApiClient(pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<HmStockApiClient>>());

        return (sut, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenAvailable_ReturnsTrueAndCachesResult()
    {
        var json = SizeJson(("S", "003", true, false));
        var (sut, pdpFetcher, redisDb) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1351887/001", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeFalse();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenFewPieceLeft_ReturnsTrueWithIsLastUnit()
    {
        // Faz 6.1 — canlı doğrulandı: "fewPieceLeft" dizisi, "availability" dizisinin bir alt kümesi —
        // hâlâ satın alınabilir (Available=true) ama az kaldı uyarısı taşıyor.
        var json = SizeJson(("XS", "002", true, true));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1351887/001", "XS", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeTrue();
        result.Quantity.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenNotAvailable_ReturnsFalse()
    {
        var json = SizeJson(("M", "004", false, false));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1351887/001", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var json = SizeJson(("S", "003", true, false));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1351887/001", "s", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFoundInResult_ReturnsNull()
    {
        var json = SizeJson(("S", "003", true, false));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("1351887/001", "XXL", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenPdpFetchFails_ReturnsNullAndDoesNotCache()
    {
        var (sut, _, redisDb) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckOnlineStockAsync("1351887/001", "S", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallPdpFetcher()
    {
        var cachedJson = "[{\"Name\":\"S\",\"SizeCode\":\"003\",\"Available\":true,\"FewPieceLeft\":false}]";
        var (sut, pdpFetcher, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync("1351887/001", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTrafficLightGreen_ReturnsTrue()
    {
        var pdpJson = SizeJson(("S", "003", true, false));
        var storeJson = """{"stores":[{"storeCode":"TR0030","sizes":{"size":[{"sizeCode":"003","avaiQty":1000,"traffLightInd":"G"}]}}]}""";
        var (sut, pdpFetcher, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

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
        var pdpJson = SizeJson(("S", "003", true, false));
        var storeJson = """{"stores":[{"storeCode":"TR0030","sizes":{"size":[{"sizeCode":"003","avaiQty":0,"traffLightInd":"Y"}]}}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenTrafficLightRed_ReturnsFalse()
    {
        var pdpJson = SizeJson(("S", "003", true, false));
        var storeJson = """{"stores":[{"storeCode":"TR0030","sizes":{"size":[{"sizeCode":"003","avaiQty":0,"traffLightInd":"R"}]}}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

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
        var pdpJson = SizeJson(("S", "003", true, false));
        var storeJson = """{"stores":[{"storeCode":"TR9999","sizes":{"size":[{"sizeCode":"003","avaiQty":1000,"traffLightInd":"G"}]}}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenSizeCodeMissingFromStoreEntry_ReturnsUnknown()
    {
        var pdpJson = SizeJson(("S", "003", true, false));
        var storeJson = """{"stores":[{"storeCode":"TR0030","sizes":{"size":[{"sizeCode":"004","avaiQty":1000,"traffLightInd":"G"}]}}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenProductCodeMalformed_ReturnsNullWithoutCallingFetcher()
    {
        var (sut, pdpFetcher, _) = CreateSut();

        var result = await sut.CheckStoreStockAsync("not-a-valid-code", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchStoreAvailabilityJsonAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenPdpSizeUnresolvable_ReturnsNullWithoutCallingStoreApi()
    {
        var (sut, pdpFetcher, _) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckStoreStockAsync("1351887/001", "S", "TR0030", 40.96, 29.08, ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchStoreAvailabilityJsonAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
