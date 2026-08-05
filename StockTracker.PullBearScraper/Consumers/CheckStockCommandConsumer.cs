using MassTransit;
using StockTracker.PullBearScraper.Services;
using StockTracker.Shared.Contracts.Messages.V2;

namespace StockTracker.PullBearScraper.Consumers;

public class CheckStockCommandConsumer : IConsumer<CheckStockCommand>
{
    private readonly IPullBearStockCheckService _checkService;
    private readonly ILogger<CheckStockCommandConsumer> _logger;

    public CheckStockCommandConsumer(IPullBearStockCheckService checkService, ILogger<CheckStockCommandConsumer> logger)
    {
        _checkService = checkService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CheckStockCommand> context)
    {
        var command = context.Message;
        _logger.LogInformation(
            "CheckStockCommand alındı — CommandId: {CommandId}, ProductCode: {ProductCode}, StoreId: {StoreId}",
            command.CommandId, command.ProductCode, command.StoreId);

        var result = await _checkService.CheckAsync(command, context.CancellationToken);

        await context.Publish(result);

        _logger.LogInformation(
            "StockResultEvent yayınlandı — CommandId: {CommandId}, Status: {Status}",
            result.CommandId, result.Status);
    }
}
