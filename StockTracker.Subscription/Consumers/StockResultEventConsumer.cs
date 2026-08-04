using MassTransit;
using StockTracker.Shared.Contracts.Messages.V1;
using StockTracker.Subscription.Services;

namespace StockTracker.Subscription.Consumers;

public class StockResultEventConsumer : IConsumer<StockResultEvent>
{
    private readonly IWatchGroupStatusUpdater _statusUpdater;

    public StockResultEventConsumer(IWatchGroupStatusUpdater statusUpdater)
    {
        _statusUpdater = statusUpdater;
    }

    public Task Consume(ConsumeContext<StockResultEvent> context) =>
        _statusUpdater.UpdateFromStockResultAsync(context.Message, context.CancellationToken);
}
