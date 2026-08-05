namespace StockTracker.SearchOrchestrator.DTOs;

// Product Service GET /lookup/{code} yanıtının ihtiyaç duyulan alt kümesi.
// ProductUrl, yalnızca manuel/site-search ile çözülmüş mappinglerde dolu olur (bkz. ProductBrandMap.ProductUrl) —
// yalnızca regex format eşleşmesiyle çözülmüş ürünlerde null'dur ve CheckStockCommand'a null olarak taşınır.
public record ProductLookupResponse(
    string ProductCode,
    bool IsResolved,
    Guid? BrandId,
    string? BrandName,
    string? ScraperQueueName,
    string? ProductUrl
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
// Latitude/Longitude yalnızca Mango için dolu (bkz. Shared.Contracts.Messages.V2.CheckStockCommand üstündeki not).
public record StoreDto(
    Guid Id,
    Guid BrandId,
    string City,
    string District,
    string BrandSpecificStoreId,
    double? Latitude = null,
    double? Longitude = null
);
