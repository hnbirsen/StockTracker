using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.StradivariusScraper.Services;

public interface IStradivariusStockCheckService
{
    Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken);
}

public class StradivariusStockCheckService : IStradivariusStockCheckService
{
    private readonly IStradivariusStockApiClient _apiClient;
    private readonly ILogger<StradivariusStockCheckService> _logger;

    public StradivariusStockCheckService(IStradivariusStockApiClient apiClient, ILogger<StradivariusStockCheckService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken)
    {
        // ProductUrl olmadan ne online ne mağaza kontrolü yapılamaz — ikisi de PDP'nin kendisini
        // (SSR HTML veya mağaza modalı) gerektiriyor (bkz. StradivariusStockApiClient üstündeki yorum).
        if (string.IsNullOrWhiteSpace(command.ProductUrl))
        {
            _logger.LogWarning(
                "CheckStockCommand'da ProductUrl yok — CommandId {CommandId} için Unknown sonucu yayınlanıyor.",
                command.CommandId);

            return BuildResult(command, StockStatus.Unknown, "no-product-url");
        }

        var isPhysicalStoreCheck = command.StoreId.HasValue && !string.IsNullOrWhiteSpace(command.BrandSpecificStoreId);

        var result = isPhysicalStoreCheck
            ? await _apiClient.CheckStoreStockAsync(command.ProductCode, command.Size, command.BrandSpecificStoreId!, command.ProductUrl, cancellationToken)
            : await _apiClient.CheckOnlineStockAsync(command.ProductCode, command.Size, command.ProductUrl, cancellationToken);

        var status = result?.InStock switch
        {
            true => StockStatus.InStock,
            false => StockStatus.OutOfStock,
            null => StockStatus.Unknown
        };

        return BuildResult(command, status, isPhysicalStoreCheck ? "stradivarius-store-modal" : "stradivarius-online-ssr", result?.Quantity, result?.IsLastUnit);
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
