using MassTransit;
using StockTracker.SearchOrchestrator.DTOs;
using StockTracker.Shared.Contracts.Messages.V2;
using StockTracker.Shared.Contracts.Messaging;

namespace StockTracker.SearchOrchestrator.Services;

public interface ISearchOrchestratorService
{
    Task<SearchResponse> SearchAsync(SearchRequest request);
}

public class SearchOrchestratorService : ISearchOrchestratorService
{
    private readonly IProductServiceClient _productClient;
    private readonly IBrandDetectionServiceClient _brandDetectionClient;
    private readonly IStoreReferenceServiceClient _storeReferenceClient;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly ILogger<SearchOrchestratorService> _logger;

    public SearchOrchestratorService(
        IProductServiceClient productClient,
        IBrandDetectionServiceClient brandDetectionClient,
        IStoreReferenceServiceClient storeReferenceClient,
        ISendEndpointProvider sendEndpointProvider,
        ILogger<SearchOrchestratorService> logger)
    {
        _productClient = productClient;
        _brandDetectionClient = brandDetectionClient;
        _storeReferenceClient = storeReferenceClient;
        _sendEndpointProvider = sendEndpointProvider;
        _logger = logger;
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request)
    {
        var productCode = request.ProductCode.Trim();
        var lookup = await _productClient.LookupAsync(productCode);

        if (lookup is null || !lookup.IsResolved)
        {
            var resolve = await _brandDetectionClient.ResolveAsync(productCode);

            if (resolve.Candidates.Count == 0)
            {
                return new SearchResponse(
                    Guid.NewGuid(),
                    "BrandUnknown",
                    "Ürün kodu için marka tespit edilemedi. Lütfen kodu kontrol edin.",
                    null
                );
            }

            // Tek + yüksek güvenilirlikli eşleşme Brand Detection tarafından otomatik kaydedilmiş olabilir —
            // güncel durumu görmek için Product Service'i tekrar sorguluyoruz.
            lookup = await _productClient.LookupAsync(productCode);

            if (lookup is null || !lookup.IsResolved)
            {
                return new SearchResponse(
                    Guid.NewGuid(),
                    "BrandUnknown",
                    "Birden fazla marka adayı bulundu. Lütfen /api/brand-detection/resolve/manual ile manuel seçim yapın.",
                    resolve.Candidates
                        .Select(c => new BrandCandidateResponse(c.BrandId, c.BrandName, ConfidenceLevelName(c.Confidence), c.MatchedPattern))
                        .ToList()
                );
            }
        }

        var searchId = Guid.NewGuid();
        var locations = request.Locations is { Count: > 0 } ? request.Locations : null;

        if (locations is null)
        {
            await SendCheckStockCommandAsync(lookup, request.Size, storeId: null, brandSpecificStoreId: null, city: null, district: null);
        }
        else
        {
            foreach (var location in locations)
            {
                var stores = await _storeReferenceClient.GetStoresAsync(lookup.BrandId!.Value, location.City, location.District);

                if (stores.Count == 0)
                {
                    // Store Reference'ta bu marka/il/ilçe için kayıtlı mağaza yok — StoreId'siz gönder,
                    // scraper en azından online stok kontrolü yapabilsin (bkz. .claude/ARCHITECTURE.md).
                    await SendCheckStockCommandAsync(lookup, request.Size, storeId: null, brandSpecificStoreId: null, location.City, location.District);
                    continue;
                }

                foreach (var store in stores)
                {
                    await SendCheckStockCommandAsync(lookup, request.Size, store.Id, store.BrandSpecificStoreId, location.City, location.District, store.Latitude, store.Longitude);
                }
            }
        }

        return new SearchResponse(
            searchId,
            "Queued",
            "İsteğiniz alındı, stok sonucu bildirim ile iletilecek.",
            null
        );
    }

    private async Task SendCheckStockCommandAsync(ProductLookupResponse lookup, string size, Guid? storeId, string? brandSpecificStoreId, string? city, string? district, double? storeLatitude = null, double? storeLongitude = null)
    {
        var queueName = QueueNaming.StockCheckQueue(lookup.ScraperQueueName!);
        var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{queueName}"));

        await sendEndpoint.Send(new CheckStockCommand(
            CommandId: Guid.NewGuid(),
            ProductCode: lookup.ProductCode,
            BrandId: lookup.BrandId!.Value,
            BrandName: lookup.BrandName!,
            Size: size,
            StoreId: storeId,
            BrandSpecificStoreId: brandSpecificStoreId,
            City: city,
            District: district,
            ProductUrl: lookup.ProductUrl,
            RequestedAt: DateTime.UtcNow,
            StoreLatitude: storeLatitude,
            StoreLongitude: storeLongitude
        ));

        _logger.LogInformation(
            "CheckStockCommand gönderildi — queue: {Queue}, product: {ProductCode}, storeId: {StoreId}, city: {City}, district: {District}",
            queueName, lookup.ProductCode, storeId, city, district);
    }

    private static string ConfidenceLevelName(int confidence) => confidence switch
    {
        1 => "Low",
        2 => "Medium",
        3 => "High",
        _ => "Unknown"
    };
}
