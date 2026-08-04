using Microsoft.EntityFrameworkCore;
using StockTracker.StoreReference.Data;
using StockTracker.StoreReference.DTOs;

namespace StockTracker.StoreReference.Services;

public interface IStoreReferenceService
{
    Task<List<StoreDto>> GetStoresAsync(Guid? brandId, string? city, string? district);
    Task<StoreDto?> GetStoreByIdAsync(Guid id);
}

public class StoreReferenceService : IStoreReferenceService
{
    private readonly StoreReferenceDbContext _db;

    public StoreReferenceService(StoreReferenceDbContext db)
    {
        _db = db;
    }

    public async Task<List<StoreDto>> GetStoresAsync(Guid? brandId, string? city, string? district)
    {
        var query = _db.Stores.Where(s => s.IsActive);

        if (brandId.HasValue)
            query = query.Where(s => s.BrandId == brandId.Value);

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(s => s.City.ToLower() == city.Trim().ToLower());

        if (!string.IsNullOrWhiteSpace(district))
            query = query.Where(s => s.District.ToLower() == district.Trim().ToLower());

        return await query
            .Select(s => new StoreDto(s.Id, s.BrandId, s.BrandName, s.City, s.District, s.StoreName, s.BrandSpecificStoreId))
            .ToListAsync();
    }

    public async Task<StoreDto?> GetStoreByIdAsync(Guid id)
    {
        return await _db.Stores
            .Where(s => s.Id == id && s.IsActive)
            .Select(s => new StoreDto(s.Id, s.BrandId, s.BrandName, s.City, s.District, s.StoreName, s.BrandSpecificStoreId))
            .FirstOrDefaultAsync();
    }
}
