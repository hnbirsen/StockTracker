using StockTracker.Product.Entities;

namespace StockTracker.Product.DTOs;

public record ProductLookupResult(
    string ProductCode,
    ProductCodeType CodeType,
    bool IsResolved,
    Guid? BrandId,
    string? BrandName,
    string? ScraperQueueName,
    string? ProductUrl,
    ConfidenceLevel? Confidence,
    ResolvedVia? ResolvedVia,
    bool FromCache
);

public record SaveMappingRequest(
    string ProductCode,
    Guid BrandId,
    ResolvedVia ResolvedVia,
    ConfidenceLevel Confidence,
    string? ProductUrl
);

public record BrandDto(
    Guid Id,
    string Name,
    string ScraperQueueName,
    bool IsActive
);
