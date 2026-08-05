using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.PullBearScraper.Consumers;
using StockTracker.PullBearScraper.Services;
using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.PullBearScraper.Tests;

public class CheckStockCommandConsumerTests
{
    [Fact]
    public async Task Consume_PublishesResultFromCheckService()
    {
        var command = new CheckStockCommand(
            CommandId: Guid.NewGuid(),
            ProductCode: "07460338/250",
            BrandId: Guid.NewGuid(),
            BrandName: "Pull&Bear",
            Size: "M",
            StoreId: null,
            BrandSpecificStoreId: null,
            City: null,
            District: null,
            ProductUrl: "https://www.pullandbear.com/tr/dantel-detayli-beyaz-bluz-l07460338?cS=250&pelement=750312599",
            RequestedAt: DateTime.UtcNow
        );

        var expectedResult = new StockResultEvent(
            command.CommandId, command.ProductCode, command.BrandId, command.Size,
            null, StockStatus.InStock, DateTime.UtcNow, "pullbear-online-api");

        var checkService = new Mock<IPullBearStockCheckService>();
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
