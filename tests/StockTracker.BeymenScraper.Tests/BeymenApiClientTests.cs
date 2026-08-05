using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.BeymenScraper.Services;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.BeymenScraper.Tests;

public class BeymenApiClientTests
{
    private const string ProductCode = "1661415";

    private static string ProductSummaryJson(params (string SizeName, bool InStock, int StockQuantity, string Barcode)[] sizes) =>
        "{\"success\":true,\"result\":{\"productId\":1661415,\"sizes\":[" +
        string.Join(",", sizes.Select(s =>
            $"{{\"inStock\":{(s.InStock ? "true" : "false")},\"sizeName\":\"{s.SizeName}\",\"isAboutToRunOutOfStock\":false,\"variantCode\":\"vc-{s.Barcode}\",\"variantBarcode\":\"{s.Barcode}\",\"stockQuantity\":{s.StockQuantity}}}")) +
        "]}}";

    private static string StoreStockJson(params (string Name, string Barcode, string Text, bool IsAboutToRunOut)[] entries) =>
        "{\"Succeed\":true,\"Data\":[" +
        string.Join(",", entries.Select(e =>
            $"{{\"Name\":\"{e.Name}\",\"DistrictName\":\"X\",\"CityName\":\"Y\",\"Variants\":[{{\"Barcode\":\"{e.Barcode}\",\"Text\":\"{e.Text}\",\"IsAboutToRunOut\":{(e.IsAboutToRunOut ? "true" : "false")}}}]}}")) +
        "]}";

    private static (BeymenApiClient Sut, FakeHttpMessageHandler Handler, Mock<IDatabase> RedisDb) CreateSut(
        string? productSummaryJson = null,
        string? storeStockJson = null,
        RedisValue cachedValue = default)
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("productsummary"))
            {
                return productSummaryJson is null
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : FakeHttpResponses.Json(HttpStatusCode.OK, productSummaryJson);
            }

            return storeStockJson is null
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : FakeHttpResponses.Json(HttpStatusCode.OK, storeStockJson);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.beymen.com") };

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(cachedValue);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDb.Object);

        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new BeymenApiClient(httpClient, redis.Object, healthLog.Object, Mock.Of<ILogger<BeymenApiClient>>());

        return (sut, handler, redisDb);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenInStock_ReturnsTrueWithRealQuantityAndCaches()
    {
        var json = ProductSummaryJson(("36", true, 15, "8683791639721"));
        var (sut, _, redisDb) = CreateSut(productSummaryJson: json);

        var result = await sut.CheckOnlineStockAsync(ProductCode, "36", CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().Be(15);
        result.IsLastUnit.Should().BeFalse();
        redisDb.Invocations.Count(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenStockQuantityIsOne_IsLastUnitTrue()
    {
        var json = ProductSummaryJson(("34", true, 1, "8683791639714"));
        var (sut, _, _) = CreateSut(productSummaryJson: json);

        var result = await sut.CheckOnlineStockAsync(ProductCode, "34", CancellationToken.None);

        result!.Quantity.Should().Be(1);
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenOutOfStock_ReturnsFalse()
    {
        var json = ProductSummaryJson(("48", false, 0, "8683791639783"));
        var (sut, _, _) = CreateSut(productSummaryJson: json);

        var result = await sut.CheckOnlineStockAsync(ProductCode, "48", CancellationToken.None);

        result!.InStock.Should().BeFalse();
        result.Quantity.Should().Be(0);
    }

    [Fact]
    public async Task CheckOnlineStockAsync_SizeMatchIsCaseInsensitive()
    {
        var json = ProductSummaryJson(("36", true, 15, "8683791639721"));
        var (sut, _, _) = CreateSut(productSummaryJson: json);

        var result = await sut.CheckOnlineStockAsync(ProductCode, "36", CancellationToken.None);

        result!.InStock.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenSizeNotFound_ReturnsNull()
    {
        var json = ProductSummaryJson(("36", true, 15, "8683791639721"));
        var (sut, _, _) = CreateSut(productSummaryJson: json);

        var result = await sut.CheckOnlineStockAsync(ProductCode, "XXL", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenApiFails_ReturnsNullAndDoesNotCache()
    {
        var (sut, _, redisDb) = CreateSut(productSummaryJson: null);

        var result = await sut.CheckOnlineStockAsync(ProductCode, "36", CancellationToken.None);

        result.Should().BeNull();
        redisDb.Invocations.Should().NotContain(i => i.Method.Name == nameof(IDatabaseAsync.StringSetAsync));
    }

    [Fact]
    public async Task CheckOnlineStockAsync_WhenCacheHit_DoesNotCallProductSummary()
    {
        var cachedJson = "{\"productId\":1661415,\"sizes\":[{\"inStock\":true,\"sizeName\":\"36\",\"isAboutToRunOutOfStock\":false,\"variantBarcode\":\"8683791639721\",\"stockQuantity\":15}]}";
        var (sut, handler, _) = CreateSut(cachedValue: cachedJson);

        var result = await sut.CheckOnlineStockAsync(ProductCode, "36", CancellationToken.None);

        result!.InStock.Should().BeTrue();
        handler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStoreCarriesBarcode_ReturnsTrueWithIsAboutToRunOutAsIsLastUnit()
    {
        var summaryJson = ProductSummaryJson(("36", true, 15, "8683791639721"));
        var storeJson = StoreStockJson(("Beymen Suadiye", "8683791639721", "36", true));
        var (sut, handler, _) = CreateSut(productSummaryJson: summaryJson, storeStockJson: storeJson);

        var result = await sut.CheckStoreStockAsync(ProductCode, "36", "Beymen Suadiye", CancellationToken.None);

        result!.InStock.Should().BeTrue();
        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeTrue();
        handler.RequestedUris.Should().Contain(u => u.Contains("getstorestock/8683791639721"));
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenQueriedStoreMissingFromSparseResponse_ReturnsFalseNotUnknown()
    {
        var summaryJson = ProductSummaryJson(("36", true, 15, "8683791639721"));
        var storeJson = StoreStockJson(("Beymen Nişantaşı", "8683791639721", "36", false));
        var (sut, _, _) = CreateSut(productSummaryJson: summaryJson, storeStockJson: storeJson);

        var result = await sut.CheckStoreStockAsync(ProductCode, "36", "Beymen Suadiye", CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenStoreDoesNotCarryThisSpecificBarcode_ReturnsFalse()
    {
        var summaryJson = ProductSummaryJson(("36", true, 15, "8683791639721"));
        var storeJson = StoreStockJson(("Beymen Suadiye", "8683791639714", "34", false));
        var (sut, _, _) = CreateSut(productSummaryJson: summaryJson, storeStockJson: storeJson);

        var result = await sut.CheckStoreStockAsync(ProductCode, "36", "Beymen Suadiye", CancellationToken.None);

        result!.InStock.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenBarcodeCannotBeResolved_ReturnsNullWithoutCallingStoreApi()
    {
        var (sut, handler, _) = CreateSut(productSummaryJson: null);

        var result = await sut.CheckStoreStockAsync(ProductCode, "36", "Beymen Suadiye", CancellationToken.None);

        result.Should().BeNull();
        handler.RequestedUris.Should().NotContain(u => u.Contains("getstorestock"));
    }

    [Fact]
    public async Task CheckStoreStockAsync_WhenApiReturnsNonSuccess_ReturnsNull()
    {
        var summaryJson = ProductSummaryJson(("36", true, 15, "8683791639721"));
        var (sut, _, _) = CreateSut(productSummaryJson: summaryJson, storeStockJson: null);

        var result = await sut.CheckStoreStockAsync(ProductCode, "36", "Beymen Suadiye", CancellationToken.None);

        result.Should().BeNull();
    }
}
