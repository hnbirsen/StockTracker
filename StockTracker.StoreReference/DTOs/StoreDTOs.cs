namespace StockTracker.StoreReference.DTOs;

public record StoreDto(
    Guid Id,
    Guid BrandId,
    string BrandName,
    string City,
    string District,
    string? StoreName,
    string BrandSpecificStoreId
);
