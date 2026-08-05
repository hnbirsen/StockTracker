using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.StradivariusScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.StradivariusScraper.Tests;

public class StradivariusStockCheckServiceTests
{
    private readonly Mock<IStradivariusStockApiClient> _apiClient = new();

    private const string ProductUrl = "https://www.stradivarius.com/tr/asimetrik-kareli-midi-elbise-l06383188";

    private StradivariusStockCheckService CreateSut() =>
        new(_apiClient.Object, Mock.Of<ILogger<StradivariusStockCheckService>>());

    private static CheckStockCommand OnlineCommand(string? productUrl = ProductUrl) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "06383188",
        BrandId: Guid.NewGuid(),
        BrandName: "Stradivarius",
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
        ProductCode: "06383188",
        BrandId: Guid.NewGuid(),
        BrandName: "Stradivarius",
        Size: "M",
        StoreId: storeId,
        BrandSpecificStoreId: "2859",
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
        result.ScraperSource.Should().Be("stradivarius-online-ssr");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdPresent_CallsStoreCheck()
    {
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "2859", ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(false, null, null));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.OutOfStock);
        result.StoreId.Should().Be(storeId);
        result.ScraperSource.Should().Be("stradivarius-store-modal");
        _apiClient.Verify(c => c.CheckOnlineStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

        result.ScraperSource.Should().Be("stradivarius-online-ssr");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
