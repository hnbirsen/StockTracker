using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.StradivariusScraper.Consumers;
using StockTracker.StradivariusScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.StradivariusScraper.Tests;

public class CheckStockCommandConsumerTests
{
    [Fact]
    public async Task Consume_PublishesResultFromCheckService()
    {
        var command = new CheckStockCommand(
            CommandId: Guid.NewGuid(),
            ProductCode: "06383188",
            BrandId: Guid.NewGuid(),
            BrandName: "Stradivarius",
            Size: "M",
            StoreId: null,
            BrandSpecificStoreId: null,
            City: null,
            District: null,
            ProductUrl: "https://www.stradivarius.com/tr/asimetrik-kareli-midi-elbise-l06383188",
            RequestedAt: DateTime.UtcNow
        );

        var expectedResult = new StockResultEvent(
            command.CommandId, command.ProductCode, command.BrandId, command.Size,
            null, StockStatus.InStock, DateTime.UtcNow, "stradivarius-online-ssr");

        var checkService = new Mock<IStradivariusStockCheckService>();
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
