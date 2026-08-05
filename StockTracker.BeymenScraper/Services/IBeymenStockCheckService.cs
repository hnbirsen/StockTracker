using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.BeymenScraper.Services;

public interface IBeymenStockCheckService
{
    Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken);
}

public class BeymenStockCheckService : IBeymenStockCheckService
{
    private readonly IBeymenApiClient _apiClient;
    private readonly ILogger<BeymenStockCheckService> _logger;

    public BeymenStockCheckService(IBeymenApiClient apiClient, ILogger<BeymenStockCheckService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken)
    {
        // Diğer markaların aksine Beymen'de ProductUrl'e hiç ihtiyaç yok — productId + beden adı, gerçek stok
        // API'lerini (Playwright/PDP gerektirmeden) çağırmak için yeterli (bkz. BeymenApiClient üstündeki yorum).
        var isPhysicalStoreCheck = command.StoreId.HasValue && !string.IsNullOrWhiteSpace(command.BrandSpecificStoreId);

        var result = isPhysicalStoreCheck
            ? await _apiClient.CheckStoreStockAsync(command.ProductCode, command.Size, command.BrandSpecificStoreId!, cancellationToken)
            : await _apiClient.CheckOnlineStockAsync(command.ProductCode, command.Size, cancellationToken);

        var status = result?.InStock switch
        {
            true => StockStatus.InStock,
            false => StockStatus.OutOfStock,
            null => StockStatus.Unknown
        };

        return BuildResult(command, status, isPhysicalStoreCheck ? "beymen-store-api" : "beymen-online-api", result?.Quantity, result?.IsLastUnit);
    }

    private static StockResultEvent BuildResult(CheckStockCommand command, StockStatus status, string scraperSource, int? quantity = null, bool? isLastUnit = null) => new(
        CommandId: command.CommandId,
        ProductCode: command.ProductCode,
        BrandId: command.BrandId,
        Size: command.Size,
        StoreId: command.StoreId,
        Status: status,
        CheckedAt: DateTime.UtcNow,
        ScraperSource: scraperSource,
        Quantity: quantity,
        IsLastUnit: isLastUnit
    );
}
