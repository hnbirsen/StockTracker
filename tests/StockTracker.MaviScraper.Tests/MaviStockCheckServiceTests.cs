using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.MaviScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.MaviScraper.Tests;

public class MaviStockCheckServiceTests
{
    private readonly Mock<IMaviStockApiClient> _apiClient = new();

    private const string ProductUrl = "https://www.mavi.com/florida-iconic-puslu-acik-mavi-jean-pantolon/p/1010381-A4216";

    private MaviStockCheckService CreateSut() =>
        new(_apiClient.Object, Mock.Of<ILogger<MaviStockCheckService>>());

    private static CheckStockCommand OnlineCommand(string? productUrl = ProductUrl) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "1010381-A4216",
        BrandId: Guid.NewGuid(),
        BrandName: "Mavi",
        Size: "30/32",
        StoreId: null,
        BrandSpecificStoreId: null,
        City: null,
        District: null,
        ProductUrl: productUrl,
        RequestedAt: DateTime.UtcNow
    );

    private static CheckStockCommand StoreCommand(Guid storeId) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "1010381-A4216",
        BrandId: Guid.NewGuid(),
        BrandName: "Mavi",
        Size: "30/32",
        StoreId: storeId,
        BrandSpecificStoreId: "507",
        City: "Istanbul",
        District: "Kadikoy",
        ProductUrl: ProductUrl,
        RequestedAt: DateTime.UtcNow,
        StoreLatitude: 41.063595,
        StoreLongitude: 28.992115
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
        result.ScraperSource.Should().Be("mavi-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdAndCoordinatesPresent_CallsStoreCheck()
    {
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "507", 41.063595, 28.992115, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(false, null, null));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.OutOfStock);
        result.StoreId.Should().Be(storeId);
        result.ScraperSource.Should().Be("mavi-store-api");
        _apiClient.Verify(c => c.CheckOnlineStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreCheckReturnsIsLastUnit_PropagatesToStockResultEventWithNullQuantity()
    {
        // Faz 6.1 — kullanıcı talebiyle eklendi: Mavi sayısal miktar vermiyor (Quantity her zaman null),
        // ama API'nin kendi `isLastUnit` bayrağı StockResultEvent'e taşınmalı.
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "507", 41.063595, 28.992115, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, null, true));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Quantity.Should().BeNull();
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
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdPresentButCoordinatesMissing_FallsBackToOnlineCheck()
    {
        // Mavi'ya özgü: Zara/Bershka'da yalnızca BrandSpecificStoreId yeterliyken, Mavi'nun mağaza
        // sorgusu enlem/boylam da gerektiriyor (bkz. MaviStockApiClient üstündeki yorum) — eski/eksik bir
        // store_db kaydı (koordinatsız) online kontrole düşmeli, hata fırlatmamalı.
        var command = StoreCommand(Guid.NewGuid()) with { StoreLatitude = null, StoreLongitude = null };
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, null, null));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.ScraperSource.Should().Be("mavi-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
