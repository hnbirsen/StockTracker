using Quartz;
using StockTracker.Subscription.Services;

namespace StockTracker.Subscription.Jobs;

public class StockPollerJob : IJob
{
    private readonly IStockPollerService _pollerService;
    private readonly ILogger<StockPollerJob> _logger;

    public StockPollerJob(IStockPollerService pollerService, ILogger<StockPollerJob> logger)
    {
        _pollerService = pollerService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await _pollerService.RunPollCycleAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            // Bir döngüdeki hata (ör. Product Service geçici erişilemez) sonraki tetiklemeleri etkilememeli —
            // Quartz'a fırlatmak yerine loglanıp yutuluyor.
            _logger.LogError(ex, "Stock poller döngüsünde beklenmeyen hata.");
        }
    }
}
