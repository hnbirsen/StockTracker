using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.BershkaScraper.Services;

public interface IBershkaStockCheckService
{
    Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken);
}

public class BershkaStockCheckService : IBershkaStockCheckService
{
    private readonly IBershkaStockApiClient _apiClient;
    private readonly ILogger<BershkaStockCheckService> _logger;

    public BershkaStockCheckService(IBershkaStockApiClient apiClient, ILogger<BershkaStockCheckService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken)
    {
        // ProductUrl olmadan gerçek Bershka stok API'sine hiç istek atılamaz (bkz. BershkaStockApiClient
        // üstündeki yorum — part-number/campaign yalnızca ürün sayfasından güvenilir şekilde elde edilebiliyor).
        // Şu an yalnızca manuel/site-search ile çözülmüş ProductBrandMap kayıtlarında ProductUrl dolu oluyor.
        if (string.IsNullOrWhiteSpace(command.ProductUrl))
        {
            _logger.LogWarning(
                "CheckStockCommand'da ProductUrl yok — CommandId {CommandId} için Unknown sonucu yayınlanıyor.",
                command.CommandId);

            return BuildResult(command, StockStatus.Unknown, "no-product-url");
        }

        var isPhysicalStoreCheck = command.StoreId.HasValue && !string.IsNullOrWhiteSpace(command.BrandSpecificStoreId);

        var inStock = isPhysicalStoreCheck
            ? await _apiClient.CheckStoreStockAsync(command.ProductCode, command.Size, command.BrandSpecificStoreId!, command.ProductUrl, cancellationToken)
            : await _apiClient.CheckOnlineStockAsync(command.ProductCode, command.Size, command.ProductUrl, cancellationToken);

        var status = inStock switch
        {
            true => StockStatus.InStock,
            false => StockStatus.OutOfStock,
            null => StockStatus.Unknown
        };

        return BuildResult(command, status, isPhysicalStoreCheck ? "bershka-store-api" : "bershka-online-api");
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
