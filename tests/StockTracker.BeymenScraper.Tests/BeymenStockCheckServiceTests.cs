using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.BeymenScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.BeymenScraper.Tests;

public class BeymenStockCheckServiceTests
{
    private readonly Mock<IBeymenApiClient> _apiClient = new();

    private BeymenStockCheckService CreateSut() =>
        new(_apiClient.Object, Mock.Of<ILogger<BeymenStockCheckService>>());

    private static CheckStockCommand OnlineCommand() => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "1661415",
        BrandId: Guid.NewGuid(),
        BrandName: "Beymen",
        Size: "36",
        StoreId: null,
        BrandSpecificStoreId: null,
        City: null,
        District: null,
        ProductUrl: null,
        RequestedAt: DateTime.UtcNow
    );

    private static CheckStockCommand StoreCommand(Guid storeId) => new(
        CommandId: Guid.NewGuid(),
        ProductCode: "1661415",
        BrandId: Guid.NewGuid(),
        BrandName: "Beymen",
        Size: "36",
        StoreId: storeId,
        BrandSpecificStoreId: "Beymen Suadiye",
        City: "Istanbul",
        District: "Kadikoy",
        ProductUrl: null,
        RequestedAt: DateTime.UtcNow
    );

    [Fact]
    public async Task CheckAsync_WhenNoStoreId_CallsOnlineCheckAndReturnsInStock()
    {
        var command = OnlineCommand();
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, 15, false));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.InStock);
        result.Quantity.Should().Be(15);
        result.ScraperSource.Should().Be("beymen-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdPresent_CallsStoreCheck()
    {
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "Beymen Suadiye", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(false, null, null));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.OutOfStock);
        result.StoreId.Should().Be(storeId);
        result.ScraperSource.Should().Be("beymen-store-api");
        _apiClient.Verify(c => c.CheckOnlineStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenStoreCheckReturnsIsAboutToRunOutAsIsLastUnit_Propagates()
    {
        var storeId = Guid.NewGuid();
        var command = StoreCommand(storeId);
        _apiClient.Setup(c => c.CheckStoreStockAsync(command.ProductCode, command.Size, "Beymen Suadiye", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, null, true));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.InStock);
        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WhenApiReturnsNull_MapsToUnknownStatus()
    {
        var command = OnlineCommand();
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockCheckResult?)null);

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.Status.Should().Be(StockStatus.Unknown);
        result.Quantity.Should().BeNull();
        result.IsLastUnit.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenStoreIdPresentButBrandSpecificStoreIdMissing_FallsBackToOnlineCheck()
    {
        var command = StoreCommand(Guid.NewGuid()) with { BrandSpecificStoreId = null };
        _apiClient.Setup(c => c.CheckOnlineStockAsync(command.ProductCode, command.Size, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCheckResult(true, 5, false));

        var sut = CreateSut();
        var result = await sut.CheckAsync(command, CancellationToken.None);

        result.ScraperSource.Should().Be("beymen-online-api");
        _apiClient.Verify(c => c.CheckStoreStockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
