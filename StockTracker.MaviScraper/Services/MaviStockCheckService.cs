using StockTracker.Shared.Contracts.Messages.V1;
using CheckStockCommand = StockTracker.Shared.Contracts.Messages.V2.CheckStockCommand;

namespace StockTracker.MaviScraper.Services;

public interface IMaviStockCheckService
{
    Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken);
}

public class MaviStockCheckService : IMaviStockCheckService
{
    private readonly IMaviStockApiClient _apiClient;
    private readonly ILogger<MaviStockCheckService> _logger;

    public MaviStockCheckService(IMaviStockApiClient apiClient, ILogger<MaviStockCheckService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<StockResultEvent> CheckAsync(CheckStockCommand command, CancellationToken cancellationToken)
    {
        // ProductUrl olmadan gerçek Mavi stok verisine hiç ulaşılamaz (bkz. MaviStockApiClient üstündeki
        // yorum — barkod/stok bilgisi yalnızca ürün sayfasından güvenilir şekilde elde edilebiliyor).
        if (string.IsNullOrWhiteSpace(command.ProductUrl))
        {
            _logger.LogWarning(
                "CheckStockCommand'da ProductUrl yok — CommandId {CommandId} için Unknown sonucu yayınlanıyor.",
                command.CommandId);

            return BuildResult(command, StockStatus.Unknown, "no-product-url");
        }

        // Mağaza sorgusu Mavi'de enlem/boylam gerektiriyor (Mango/H&M'deki gibi) — ikisi de yoksa online
        // kontrole düşülür.
        var isPhysicalStoreCheck = command.StoreId.HasValue && !string.IsNullOrWhiteSpace(command.BrandSpecificStoreId)
            && command.StoreLatitude.HasValue && command.StoreLongitude.HasValue;

        var result = isPhysicalStoreCheck
            ? await _apiClient.CheckStoreStockAsync(command.ProductCode, command.Size, command.BrandSpecificStoreId!, command.StoreLatitude!.Value, command.StoreLongitude!.Value, command.ProductUrl, cancellationToken)
            : await _apiClient.CheckOnlineStockAsync(command.ProductCode, command.Size, command.ProductUrl, cancellationToken);

        var status = result?.InStock switch
        {
            true => StockStatus.InStock,
            false => StockStatus.OutOfStock,
            null => StockStatus.Unknown
        };

        return BuildResult(command, status, isPhysicalStoreCheck ? "mavi-store-api" : "mavi-online-api", result?.Quantity, result?.IsLastUnit);
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
