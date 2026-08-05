using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.MangoScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.MangoScraper.Tests;

public class MangoStockCheckServiceTests
{
    private readonly Mock<IMangoStockApiClient> _apiClient = new();

    private const string ProductUrl = "https://shop.mango.com/tr/tr/p/kad%C4%B1n/elbise-ve-tulum/dugun-davetlisi/asimetrik-saten-elbise/37013869/56/00";

    private MangoStockCheckService CreateSut() =>
        new(_apiClient.Object, Mock.Of<ILogger<MangoStockCheckService>>());

    private static CheckStockCommand OnlineCommand(string? productUrl = ProductUrl) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "37013869/56",
        BrandId: Guid.NewGuid(),
        BrandName: "Mango",
        Size: "S",
        StoreId: null,
        BrandSpecificStoreId: null,
        City: null,
        District: null,
        ProductUrl: productUrl,
        RequestedAt: DateTime.UtcNow
    );

    private static CheckStockCommand StoreCommand(Guid storeId) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "37013869/56",
        BrandId: Guid.NewGuid(),
        BrandName: "Mango",
        Size: "S",
        StoreId: storeId,
        BrandSpecificStoreId: "10389",
        City: "Istanbul",
        District: "Kadikoy",
        ProductUrl: ProductUrl,
        RequestedAt: DateTime.UtcNow,
        StoreLatitude: 40.96,
        StoreLongitude: 29.08
    );

    [Fact]
    public async Task CheckAsync_WhenNoStoreId_CallsOnlineCheckAndReturnsInStock()
    {
        var command = OnlineCommand();
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.InStock);
        result.StoreId.Should().BeNull();
        result.ScraperSource.Should().Be("mango-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdAndCoordinatesPresent_CallsStoreCheck()
    {
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "10389", 40.96, 29.08, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.OutOfStock);
        result.StoreId.Should().Be(storeId);
        result.ScraperSource.Should().Be("mango-store-api");
        _apiClient.Verify(c => c.CheckOnlineStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenApiReturnsNull_MapsToUnknownStatus()
    {
        var command = OnlineCommand();
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync((bool?)null);

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.Unknown);
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
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdPresentButCoordinatesMissing_FallsBackToOnlineCheck()
    {
        // Mango'ya özgü: Zara/Bershka'da yalnızca BrandSpecificStoreId yeterliyken, Mango'nun mağaza
        // sorgusu enlem/boylam da gerektiriyor (bkz. MangoStockApiClient üstündeki yorum) — eski/eksik bir
        // store_db kaydı (koordinatsız) online kontrole düşmeli, hata fırlatmamalı.
        var command = StoreCommand(Guid.NewGuid()) with { StoreLatitude = null, StoreLongitude = null };
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.ScraperSource.Should().Be("mango-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
