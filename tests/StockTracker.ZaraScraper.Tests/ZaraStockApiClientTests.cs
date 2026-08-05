using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;
using StockTracker.ZaraScraper.Services;

namespace StockTracker.ZaraScraper.Tests;

public class ZaraStockApiClientTests
{
    private const string ProductUrl = "https://www.zara.com/tr/tr/dantel-detayli-kisa-t-shirt-p05063821.html?v1=547843031&v2=2420417";

    // PlaywrightZaraFetcher'ın window.zara.viewPayload'dan çıkardığı, çözümlenmiş beden listesi JSON şekli
    // (bkz. IZaraPdpFetcher.FetchProductDataJsonAsync). ProductId verilmezse ProductUrl'deki v1 ile aynı
    // değer kullanılır (canlı veriyle doğrulanan "iki eşdeğer kaynak" davranışını mirror eder).
    private static string SizeJson(params (string Name, string Availability, string ColorId, string Sku, string? ProductId)[] entries) =>
        "[" + string.Join(",", entries.Select(e =>
        {
            var productId = e.ProductId ?? "547843031";
            return $"{{\"Name\":\"{e.Name}\",\"Availability\":\"{e.Availability}\",\"ColorId\":\"{e.ColorId}\",\"Sku\":\"{e.Sku}\",\"ProductId\":\"{productId}\"}}";
        })) + "]";

    private static (ZaraStockApiClient Sut, Mock<IZaraPdpFetcher> PdpFetcher, Mock<IDatabase> RedisDb) CreateSut(
        string? pdpSizesJson = null,
        RedisValue cachedValue = default,
        string? storeAvailabilityJson = null)
    {
        var pdpFetcher = new Mock<IZaraPdpFetcher>();
        pdpFetcher.Setup(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(pdpSizesJson);
        pdpFetcher.Setup(f => f.FetchStoreAvailabilityJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storeAvailabilityJson);

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new ZaraStockApiClient(pdpFetcher.Object, redis.Object, healthLog.Object, Mock.Of<ILogger<ZaraStockApiClient>>());

        return (sut, pdpFetcher, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeInStock_ReturnsTrueAndCachesResult()
    {
        var json = SizeJson(("S", "in_stock", "802", "547828483", null));
        var (sut, pdpFetcher, redisDb) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("5063/821/802", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(ProductUrl, It.IsAny<CancellationToken>()), Times.Once);
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenLowOnStock_ReturnsTrue()
    {
        // CANLI 10 ÜRÜNLÜK TEST TURUNDA BULUNAN KRİTİK REGRESYON: "availability" yalnızca "in_stock" değil,
        // üçüncü bir değer olan "low_on_stock" da alıyor (az sayıda kaldı ama satın alınabilir). İlk
        // tasarımda yalnızca "in_stock" true kabul ediliyordu — bu gerçekten stokta olan bir ürünü
        // yanlışlıkla OutOfStock gösteriyordu.
        var json = SizeJson(("XS", "low_on_stock", "620", "111", null));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("7196/399/620", "XS", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenOutOfStock_ReturnsFalse()
    {
        var json = SizeJson(("M", "out_of_stock", "802", "547828484", null));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("5063/821/802", "M", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var json = SizeJson(("S", "in_stock", "802", "547828483", null));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("5063/821/802", "s", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_OnlyMatchesRequestedColorVariant()
    {
        // Aynı PDP'de birden fazla renk (color.id) yer alabiliyor — productCode'un son 3 hanesiyle
        // (ColorId) eşleşmeyen renklerin verisi yanlışlıkla kullanılmamalı.
        var json = SizeJson(
            ("S", "in_stock", "700", "111", null),      // renk 700 (Kahverengi) — istenmeyen
            ("S", "out_of_stock", "802", "222", null)); // renk 802 (Gri) — istenen
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("5063/821/802", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFoundInResult_ReturnsNull()
    {
        var json = SizeJson(("S", "in_stock", "802", "547828483", null));
        var (sut, _, _) = CreateSut(pdpSizesJson: json);

        var result = await sut.CheckOnlineStockAsync("5063/821/802", "XXL", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenPdpFetchFails_ReturnsNullAndDoesNotCache()
    {
        var (sut, _, redisDb) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckOnlineStockAsync("5063/821/802", "S", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallPdpFetcher()
    {
        var cachedJson = "[{\"Name\":\"S\",\"Availability\":\"in_stock\",\"ColorId\":\"802\",\"Sku\":\"547828483\",\"ProductId\":\"547843031\"}]";
        var (sut, pdpFetcher, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync("5063/821/802", "S", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchProductDataJsonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_ResolvesProductIdFromPdpColorsData_AndReturnsTrueWhenStockPositive()
    {
        // CANLI 10 ÜRÜNLÜK TEST TURUNDA BULUNAN İYİLEŞTİRME: productId artık öncelikle PDP verisinin kendi
        // `colors[].productId` alanından (ColorId eşleşmesiyle) çözülüyor — URL'in `v1` içermesine bağımlı
        // değil (bare bir ürün URL'inde bile bu alan mevcut, canlı veriyle doğrulandı).
        var pdpJson = SizeJson(("S", "in_stock", "802", "547828483", "999888777"));
        var storeJson = """{"productId":999888777,"sizesAvailableAndLocationsByPhysicalStores":[{"physicalStoreId":1236,"sizesAvailability":[{"sizeId":"102","size":"S","stock":6}]}]}""";
        var (sut, pdpFetcher, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("5063/821/802", "S", "1236", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchStoreAvailabilityJsonAsync(ProductUrl, "999888777", "1236", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenPdpUnresolvable_FallsBackToUrlV1Param()
    {
        var storeJson = """{"productId":547843031,"sizesAvailableAndLocationsByPhysicalStores":[{"physicalStoreId":1236,"sizesAvailability":[{"sizeId":"102","size":"S","stock":6}]}]}""";
        var (sut, pdpFetcher, _) = CreateSut(pdpSizesJson: null, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("5063/821/802", "S", "1236", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
        pdpFetcher.Verify(f => f.FetchStoreAvailabilityJsonAsync(ProductUrl, "547843031", "1236", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStoreQuantityIsZero_ReturnsFalse()
    {
        var pdpJson = SizeJson(("S", "in_stock", "802", "547828483", null));
        var storeJson = """{"productId":547843031,"sizesAvailableAndLocationsByPhysicalStores":[{"physicalStoreId":1236,"sizesAvailability":[{"sizeId":"102","size":"S","stock":0}]}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("5063/821/802", "S", "1236", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenQueriedStoreMissingFromSparseResponse_ReturnsFalseNotUnknown()
    {
        // CANLI VERİYLE DOĞRULANAN KRİTİK REGRESYON: aynı istekte hem store 251 hem 1236 sorgulanınca
        // yalnızca stoğu OLAN mağaza (1236) dizide dönmüştü — store 251 (gerçek, var olan bir mağaza)
        // hiç yer almamıştı. Bershka'nın "boş stocks[] = Unknown" davranışının AKSİNE, burada dizide
        // mağazanın olmaması "bu üründen o mağazada yok" anlamına geliyor — Unknown değil, False dönmeli.
        // 10 ürünlük ikinci canlı test turunda da (gerçek boş `[]` yanıtıyla) doğrulandı.
        var pdpJson = SizeJson(("S", "in_stock", "802", "547828483", null));
        var storeJson = """{"productId":547843031,"sizesAvailableAndLocationsByPhysicalStores":[{"physicalStoreId":1236,"sizesAvailability":[{"sizeId":"102","size":"S","stock":6}]}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("5063/821/802", "S", "251", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenRequestedSizeNotListedForStore_ReturnsFalse()
    {
        var pdpJson = SizeJson(("S", "in_stock", "802", "547828483", null));
        var storeJson = """{"productId":547843031,"sizesAvailableAndLocationsByPhysicalStores":[{"physicalStoreId":1236,"sizesAvailability":[{"sizeId":"103","size":"M","stock":4}]}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("5063/821/802", "S", "1236", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenFetcherReturnsNull_ReturnsUnknown()
    {
        var pdpJson = SizeJson(("S", "in_stock", "802", "547828483", null));
        var (sut, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: null);

        var result = await sut.CheckStoreStockAsync("5063/821/802", "S", "1236", ProductUrl, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenNoPdpDataAndNoV1Param_ReturnsNullWithoutCallingFetcher()
    {
        var (sut, pdpFetcher, _) = CreateSut(pdpSizesJson: null);

        var result = await sut.CheckStoreStockAsync("5063/821/802", "S", "1236", "https://www.zara.com/tr/tr/urun.html", CancellationToken.None);

        result.Should().BeNull();
        pdpFetcher.Verify(f => f.FetchStoreAvailabilityJsonAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStoreStockAsync_SizeMatchIsCaseInsensitive()
    {
        var pdpJson = SizeJson(("S", "in_stock", "802", "547828483", null));
        var storeJson = """{"productId":547843031,"sizesAvailableAndLocationsByPhysicalStores":[{"physicalStoreId":1236,"sizesAvailability":[{"sizeId":"102","size":"S","stock":6}]}]}""";
        var (sut, _, _) = CreateSut(pdpSizesJson: pdpJson, storeAvailabilityJson: storeJson);

        var result = await sut.CheckStoreStockAsync("5063/821/802", "s", "1236", ProductUrl, CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }
}
