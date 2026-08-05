using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.BershkaScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.BershkaScraper.Tests;

public class BershkaStockCheckServiceTests
{
    private readonly Mock<IBershkaStockApiClient> _apiClient = new();

    private const string ProductUrl = "https://www.bershka.com/tr/test-urun-c0p123456789.html?colorId=676";

    private BershkaStockCheckService CreateSut() =>
        new(_apiClient.Object, Mock.Of<ILogger<BershkaStockCheckService>>());

    private static CheckStockCommand OnlineCommand(string? productUrl = ProductUrl) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "1234567",
        BrandId: Guid.NewGuid(),
        BrandName: "Bershka",
        Size: "38",
        StoreId: null,
        BrandSpecificStoreId: null,
        City: null,
        District: null,
        ProductUrl: productUrl,
        RequestedAt: DateTime.UtcNow
    );

    private static CheckStockCommand StoreCommand(Guid storeId) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "1234567",
        BrandId: Guid.NewGuid(),
        BrandName: "Bershka",
        Size: "38",
        StoreId: storeId,
        BrandSpecificStoreId: "16884",
        City: "Istanbul",
        District: "Kadikoy",
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
        result.ScraperSource.Should().Be("bershka-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdAndBrandSpecificStoreIdPresent_CallsStoreCheck()
    {
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "16884", ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(false, 0, null));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.OutOfStock);
        result.StoreId.Should().Be(storeId);
        result.ScraperSource.Should().Be("bershka-store-api");
        _apiClient.Verify(c => c.CheckOnlineStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreCheckReturnsQuantityAndLastUnit_PropagatesToStockResultEvent()
    {
        // Faz 6.1 — kullanıcı talebiyle eklendi: mağaza sorgusundan gelen gerçek miktar/son-ürün bilgisi
        // StockResultEvent'e taşınmalı, kaybolmamalı.
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "16884", ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, 1, true));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.InStock);
        result.Quantity.Should().Be(1);
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WhenOnlineCheckSucceeds_QuantityAndIsLastUnitAreNull()
    {
        // Online kontrolde Bershka'nın API'si sayısal miktar vermiyor (bkz. BershkaStockApiClient) —
        // Quantity/IsLastUnit her zaman null olmalı, false/0 değil.
        var command = OnlineCommand();
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, null, null));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeNull();
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
        result.CommandId.Should().Be(command.CommandId);
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

        result.ScraperSource.Should().Be("bershka-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
