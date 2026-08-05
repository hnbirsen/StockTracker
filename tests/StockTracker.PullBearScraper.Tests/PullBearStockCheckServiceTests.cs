using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.PullBearScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.PullBearScraper.Tests;

public class PullBearStockCheckServiceTests
{
    private readonly Mock<IPullBearStockApiClient> _apiClient = new();

    private const string ProductUrl = "https://www.pullandbear.com/tr/dantel-detayli-beyaz-bluz-l07460338?cS=250&pelement=750312599";

    private PullBearStockCheckService CreateSut() =>
        new(_apiClient.Object, Mock.Of<ILogger<PullBearStockCheckService>>());

    private static CheckStockCommand OnlineCommand(string? productUrl = ProductUrl) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "07460338/250",
        BrandId: Guid.NewGuid(),
        BrandName: "Pull&Bear",
        Size: "M",
        StoreId: null,
        BrandSpecificStoreId: null,
        City: null,
        District: null,
        ProductUrl: productUrl,
        RequestedAt: DateTime.UtcNow
    );

    private static CheckStockCommand StoreCommand(Guid storeId) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "07460338/250",
        BrandId: Guid.NewGuid(),
        BrandName: "Pull&Bear",
        Size: "M",
        StoreId: storeId,
        BrandSpecificStoreId: "5287",
        City: "Istanbul",
        District: "Sisli",
        ProductUrl: ProductUrl,
        RequestedAt: DateTime.UtcNow
    );

    [Fact]
    public async Task CheckAsync_WhenNoStoreId_CallsOnlineCheckAndReturnsInStock()
    {
        var command = OnlineCommand();
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, null, null));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.InStock);
        result.StoreId.Should().BeNull();
        result.ScraperSource.Should().Be("pullbear-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdPresent_CallsStoreCheck()
    {
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "5287", ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(false, null, null));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.OutOfStock);
        result.StoreId.Should().Be(storeId);
        result.ScraperSource.Should().Be("pullbear-store-api");
        _apiClient.Verify(c => c.CheckOnlineStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreCheckReturnsRealQuantityAndIsLastUnit_PropagatesToStockResultEvent()
    {
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "5287", ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, 1, true));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.InStock);
        result.Quantity.Should().Be(1);
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WhenApiReturnsNull_MapsToUnknownStatus()
    {
        var command = OnlineCommand();
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockCheckResult?)null);

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.Unknown);
        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenProductUrlMissing_ReturnsUnknownWithoutCallingApiClient()
    {
        var command = OnlineCommand(productUrl: null);

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.Unknown);
        result.ScraperSource.Should().Be("no-product-url");
        _apiClient.Verify(c => c.CheckOnlineStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdPresentButBrandSpecificStoreIdMissing_FallsBackToOnlineCheck()
    {
        var command = StoreCommand(Guid.NewGuid()) with { BrandSpecificStoreId = null };
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, null, null));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.ScraperSource.Should().Be("pullbear-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
