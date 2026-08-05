using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.PullBearScraper.Services;

public interface IPullBearStockCheckService
{
    Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken);
}

public class PullBearStockCheckService : IPullBearStockCheckService
{
    private readonly IPullBearStockApiClient _apiClient;
    private readonly ILogger<PullBearStockCheckService> _logger;

    public PullBearStockCheckService(IPullBearStockApiClient apiClient, ILogger<PullBearStockCheckService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken)
    {
        // ProductUrl olmadan online stok sorgusu yapılamaz (bkz. PullBearStockApiClient üstündeki yorum —
        // PDP'nin `product-modular.__product` state'inden okunan `isBuyable` tek güvenilir kaynak).
        if (string.IsNullOrWhiteSpace(command.ProductUrl))
        {
            _logger.LogWarning(
                "CheckStockCommand'da ProductUrl yok — CommandId {CommandId} için Unknown sonucu yayınlanıyor.",
                command.CommandId);

            return BuildResult(command, StockStatus.Unknown, "no-product-url");
        }

        // Mağaza bazlı sorgu Mango/H&M'in aksine enlem/boylam GEREKMİYOR — Pull&Bear'ın gerçek stok API'si
        // (bkz. PullBearStockApiClient üstündeki yorum) doğrudan mağaza ID'siyle çalışıyor (Zara/Massimo
        // Dutti'deki gibi), yalnızca StoreId/BrandSpecificStoreId yeterli.
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

        return BuildResult(command, status, isPhysicalStoreCheck ? "pullbear-store-api" : "pullbear-online-api", result?.Quantity, result?.IsLastUnit);
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
