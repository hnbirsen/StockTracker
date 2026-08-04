using MassTransit;
using StockTracker.Notification.Services;
using StockTracker.Shared.Contracts.Messages.V1;

namespace StockTracker.Notification.Consumers;

public class StockResultEventConsumer : IConsumer<StockResultEvent>
{
    private readonly INotificationProcessingService _processingService;

    public StockResultEventConsumer(INotificationProcessingService processingService)
    {
        _processingService = processingService;
    }

    public Task Consume(ConsumeContext<StockResultEvent> context) =>
        _processingService.ProcessAsync(context.Message, context.CancellationToken);
}
