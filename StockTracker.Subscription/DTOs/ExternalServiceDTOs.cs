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
// Latitude/Longitude yalnızca Mango için dolu (bkz. Shared.Contracts.Messages.V2.CheckStockCommand üstündeki not).
public record StoreDto(
    Guid Id,
    Guid BrandId,
    string BrandSpecificStoreId,
    double? Latitude = null,
    double? Longitude = null
);

// Billing Service GET /limits/{userId} yanıtının ihtiyaç duyulan alt kümesi (Faz 4.3).
public record UserLimitsResponse(
    Guid UserId,
    string PlanName,
    int MaxTrackedProducts,
    int CheckFrequencyMinutes
);
