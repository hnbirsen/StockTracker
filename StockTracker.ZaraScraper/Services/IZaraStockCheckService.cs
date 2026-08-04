using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.ZaraScraper.Services;

public interface IZaraStockCheckService
{
    Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken);
}

public class ZaraStockCheckService : IZaraStockCheckService
{
    private readonly IZaraStockApiClient _apiClient;
    private readonly ILogger<ZaraStockCheckService> _logger;

    public ZaraStockCheckService(IZaraStockApiClient apiClient, ILogger<ZaraStockCheckService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken)
    {
        // ProductUrl olmadan gerçek Zara stok verisine hiç erişilemez (bkz. ZaraStockApiClient üstündeki
        // yorum — hem online availability hem de mağaza sorgusu için gereken productId yalnızca ürün
        // sayfasından/URL'inden güvenilir şekilde elde edilebiliyor).
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

        return BuildResult(command, status, isPhysicalStoreCheck ? "zara-store-api" : "zara-online-api");
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
