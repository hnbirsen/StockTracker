namespace StockTracker.Subscription.DTOs;

// Product Service GET /lookup/{code} yanıtının ihtiyaç duyulan alt kümesi (SearchOrchestrator'daki
// ProductLookupResponse ile aynı sözleşme — bkz. StockTracker.SearchOrchestrator/DTOs/ExternalServiceDTOs.cs).
public record ProductLookupResponse(
    string ProductCode,
    bool IsResolved,
    Guid? BrandId,
    string? BrandName,
    string? ScraperQueueName,
    string? ProductUrl
);

// Store Reference Service GET /stores/{id} yanıtının ihtiyaç duyulan alt kümesi.
public record StoreDto(
    Guid Id,
    Guid BrandId,
    string BrandSpecificStoreId
);
