using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.BershkaScraper.Consumers;
using StockTracker.BershkaScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.BershkaScraper.Tests;

public class CheckStockCommandConsumerTests
{
    [Fact]
    public async Task Consume_PublishesResultFromCheckService()
    {
        var command = new CheckStockCommand(
            CommandId: Guid.NewGuid(),
            ProductCode: "1234567",
            BrandId: Guid.NewGuid(),
            BrandName: "Bershka",
            Size: "38",
            StoreId: null,
            BrandSpecificStoreId: null,
            City: null,
            District: null,
            ProductUrl: "https://www.bershka.com/tr/test-urun-c0p123456789.html?colorId=676",
            RequestedAt: DateTime.UtcNow
        );

        var expectedResult = new StockResultEvent(
            command.CommandId, command.ProductCode, command.BrandId, command.Size,
            null, StockStatus.InStock, DateTime.UtcNow, "bershka-online-api");

        var checkService = new Mock<IBershkaStockCheckService>();
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
