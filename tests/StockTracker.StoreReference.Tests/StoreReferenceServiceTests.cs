using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTracker.StoreReference.Data;
using StockTracker.StoreReference.Entities;
using StockTracker.StoreReference.Services;

namespace StockTracker.StoreReference.Tests;

public class StoreReferenceServiceTests
{
    private static StoreReferenceDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<StoreReferenceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Store CreateStore(Guid brandId, string city, string district, bool isActive = true) => new()
    {
        BrandId = brandId,
        BrandName = "Bershka",
        City = city,
        District = district,
        BrandSpecificStoreId = $"BSK-{city}-{district}",
        IsActive = isActive
    };

    [Fact]
    public async Task GetStoresAsync_WithoutFilters_ReturnsAllActiveStores()
    {
        await using var db = CreateDbContext();
        var brandId = Guid.NewGuid();
        db.Stores.AddRange(
            CreateStore(brandId, "Istanbul", "Kadikoy"),
            CreateStore(brandId, "Ankara", "Cankaya"));
        await db.SaveChangesAsync();

        var sut = new StoreReferenceService(db);
        var result = await sut.GetStoresAsync(null, null, null);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetStoresAsync_FiltersByBrandId()
    {
        await using var db = CreateDbContext();
        var bershkaId = Guid.NewGuid();
        var zaraId = Guid.NewGuid();
        db.Stores.AddRange(
            CreateStore(bershkaId, "Istanbul", "Kadikoy"),
            CreateStore(zaraId, "Istanbul", "Besiktas"));
        await db.SaveChangesAsync();

        var sut = new StoreReferenceService(db);
        var result = await sut.GetStoresAsync(bershkaId, null, null);

        result.Should().ContainSingle();
        result[0].BrandId.Should().Be(bershkaId);
    }

    [Fact]
    public async Task GetStoresAsync_FiltersByCityAndDistrict_CaseInsensitive()
    {
        await using var db = CreateDbContext();
        var brandId = Guid.NewGuid();
        db.Stores.Add(CreateStore(brandId, "Istanbul", "Kadikoy"));
        await db.SaveChangesAsync();

        var sut = new StoreReferenceService(db);
        var result = await sut.GetStoresAsync(null, "istanbul", "KADIKOY");

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetStoresAsync_ExcludesInactiveStores()
    {
        await using var db = CreateDbContext();
        var brandId = Guid.NewGuid();
        db.Stores.Add(CreateStore(brandId, "Istanbul", "Kadikoy", isActive: false));
        await db.SaveChangesAsync();

        var sut = new StoreReferenceService(db);
        var result = await sut.GetStoresAsync(null, null, null);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStoresAsync_WhenNoMatch_ReturnsEmptyList()
    {
        await using var db = CreateDbContext();
        db.Stores.Add(CreateStore(Guid.NewGuid(), "Istanbul", "Kadikoy"));
        await db.SaveChangesAsync();

        var sut = new StoreReferenceService(db);
        var result = await sut.GetStoresAsync(null, "Antalya", "Muratpasa");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStoreByIdAsync_WhenActiveStoreExists_ReturnsStore()
    {
        await using var db = CreateDbContext();
        var store = CreateStore(Guid.NewGuid(), "Istanbul", "Kadikoy");
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sut = new StoreReferenceService(db);
        var result = await sut.GetStoreByIdAsync(store.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(store.Id);
        result.BrandSpecificStoreId.Should().Be(store.BrandSpecificStoreId);
    }

    [Fact]
    public async Task GetStoreByIdAsync_WhenStoreIsInactive_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var store = CreateStore(Guid.NewGuid(), "Istanbul", "Kadikoy", isActive: false);
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sut = new StoreReferenceService(db);
        var result = await sut.GetStoreByIdAsync(store.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetStoreByIdAsync_WhenIdDoesNotExist_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var sut = new StoreReferenceService(db);

        var result = await sut.GetStoreByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
