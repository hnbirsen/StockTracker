using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.MassimoDuttiScraper.Services;

public interface IMassimoDuttiStockCheckService
{
    Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken);
}

public class MassimoDuttiStockCheckService : IMassimoDuttiStockCheckService
{
    private readonly IMassimoDuttiStockApiClient _apiClient;
    private readonly ILogger<MassimoDuttiStockCheckService> _logger;

    public MassimoDuttiStockCheckService(IMassimoDuttiStockApiClient apiClient, ILogger<MassimoDuttiStockCheckService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken)
    {
        // ProductUrl olmadan online stok sorgusu yapılamaz (bkz. MassimoDuttiStockApiClient üstündeki yorum —
        // PDP'nin #mdfrontw-state state'inden okunan `isBuyable` tek güvenilir kaynak).
        if (string.IsNullOrWhiteSpace(command.ProductUrl))
        {
            _logger.LogWarning(
                "CheckStockCommand'da ProductUrl yok — CommandId {CommandId} için Unknown sonucu yayınlanıyor.",
                command.CommandId);

            return BuildResult(command, StockStatus.Unknown, "no-product-url");
        }

        // Mağaza bazlı sorgu Mango/H&M'in aksine enlem/boylam GEREKTİRMİYOR — Massimo Dutti'nin gerçek stok
        // API'si (bkz. MassimoDuttiStockApiClient üstündeki yorum) doğrudan mağaza ID'siyle çalışıyor
        // (Zara'daki gibi), yalnızca StoreId/BrandSpecificStoreId yeterli.
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

        return BuildResult(command, status, isPhysicalStoreCheck ? "massimodutti-store-api" : "massimodutti-online-api", result?.Quantity, result?.IsLastUnit);
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
