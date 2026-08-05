using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.MangoScraper.Services;

public interface IMangoStockCheckService
{
    Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken);
}

public class MangoStockCheckService : IMangoStockCheckService
{
    private readonly IMangoStockApiClient _apiClient;
    private readonly ILogger<MangoStockCheckService> _logger;

    public MangoStockCheckService(IMangoStockApiClient apiClient, ILogger<MangoStockCheckService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken)
    {
        // ProductUrl olmadan online stok sorgusu yapılamaz (bkz. MangoStockApiClient üstündeki yorum —
        // PDP'nin RSC akışından okunan `available` alanı tek güvenilir kaynak).
        if (string.IsNullOrWhiteSpace(command.ProductUrl))
        {
            _logger.LogWarning(
                "CheckStockCommand'da ProductUrl yok — CommandId {CommandId} için Unknown sonucu yayınlanıyor.",
                command.CommandId);

            return BuildResult(command, StockStatus.Unknown, "no-product-url");
        }

        // Mağaza bazlı sorgu, Zara/Bershka'nın aksine bir mağaza ID'si değil enlem/boylam gerektiriyor
        // (bkz. MangoStockApiClient üstündeki yorum) — StoreId/BrandSpecificStoreId dolu olsa bile
        // koordinatlar eksikse (ör. eski bir store_db kaydı) online kontrole düşülüyor.
        var isPhysicalStoreCheck = command.StoreId.HasValue
            && !string.IsNullOrWhiteSpace(command.BrandSpecificStoreId)
            && command.StoreLatitude.HasValue
            && command.StoreLongitude.HasValue;

        var inStock = isPhysicalStoreCheck
            ? await _apiClient.CheckStoreStockAsync(command.ProductCode, command.Size, command.BrandSpecificStoreId!, command.StoreLatitude!.Value, command.StoreLongitude!.Value, cancellationToken)
            : await _apiClient.CheckOnlineStockAsync(command.ProductCode, command.Size, command.ProductUrl, cancellationToken);

        var status = inStock switch
        {
            true => StockStatus.InStock,
            false => StockStatus.OutOfStock,
            null => StockStatus.Unknown
        };

        return BuildResult(command, status, isPhysicalStoreCheck ? "mango-store-api" : "mango-online-api");
    }

    private static StockResultEvent BuildResult(CheckStockCommand command, StockStatus status, string scraperSource) => new(
        CommandId: command.CommandId,
        ProductCode: command.ProductCode,
        BrandId: command.BrandId,
        Size: command.Size,
        StoreId: command.StoreId,
        Status: status,
        CheckedAt: DateTime.UtcNow,
        ScraperSource: scraperSource
    );
}
