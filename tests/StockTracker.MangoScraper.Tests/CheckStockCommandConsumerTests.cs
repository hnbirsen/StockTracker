using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.MangoScraper.Consumers;
using StockTracker.MangoScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.MangoScraper.Tests;

public class CheckStockCommandConsumerTests
{
    [Fact]
    public async Task Consume_PublishesResultFromCheckService()
    {
        var command = new CheckStockCommand(
            CommandId: Guid.NewGuid(),
            ProductCode: "37013869/56",
            BrandId: Guid.NewGuid(),
            BrandName: "Mango",
            Size: "S",
            StoreId: null,
            BrandSpecificStoreId: null,
            City: null,
            District: null,
            ProductUrl: "https://shop.mango.com/tr/tr/p/kad%C4%B1n/elbise-ve-tulum/dugun-davetlisi/asimetrik-saten-elbise/37013869/56/00",
            RequestedAt: DateTime.UtcNow
        );

        var expectedResult = new StockResultEvent(
            command.CommandId, command.ProductCode, command.BrandId, command.Size,
            null, StockStatus.InStock, DateTime.UtcNow, "mango-online-api");

        var checkService = new Mock<IMangoStockCheckService>();
        checkService.Setup(s => s.CheckAsync(command, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        var context = new Mock<ConsumeContext<CheckStockCommand>>();
        context.SetupGet(c => c.Message).Returns(command);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.Setup(c => c.Publish(It.IsAny<StockResultEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new CheckStockCommandConsumer(checkService.Object, Mock.Of<ILogger<CheckStockCommandConsumer>>());
        await sut.Consume(context.Object);

        context.Verify(c => c.Publish(
            It.Is<StockResultEvent>(r => r.CommandId == command.CommandId && r.Status == StockStatus.InStock),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
