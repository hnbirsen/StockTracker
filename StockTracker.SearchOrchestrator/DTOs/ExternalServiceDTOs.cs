namespace StockTracker.SearchOrchestrator.DTOs;

// Product Service GET /lookup/{code} yanıtının ihtiyaç duyulan alt kümesi.
public record ProductLookupResponse(
    string ProductCode,
    bool IsResolved,
    Guid? BrandId,
    string? BrandName,
    string? ScraperQueueName
);

// Brand Detection Service POST /resolve yanıtının ihtiyaç duyulan alt kümesi.
// Confidence enum'u karşı taraftan int olarak serialize edilir (JsonStringEnumConverter yok).
public record BrandCandidateDto(
    Guid BrandId,
    string BrandName,
    int Confidence,
    string MatchedPattern
);

public record ResolveResponse(
    string ProductCode,
    bool IsResolved,
    List<BrandCandidateDto> Candidates
);

// Store Reference Service GET /stores yanıtının ihtiyaç duyulan alt kümesi.
public record StoreDto(
    Guid Id,
    Guid BrandId,
    string City,
    string District,
    string BrandSpecificStoreId
);
