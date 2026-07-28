using Microsoft.EntityFrameworkCore;
using StockTracker.StoreReference.Entities;

namespace StockTracker.StoreReference.Data;

public class StoreReferenceDbContext : DbContext
{
    public StoreReferenceDbContext(DbContextOptions<StoreReferenceDbContext> options) : base(options) { }

    public DbSet<Store> Stores => Set<Store>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Store>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => new { s.BrandId, s.City, s.District });
            entity.Property(s => s.BrandName).IsRequired().HasMaxLength(100);
            entity.Property(s => s.City).IsRequired().HasMaxLength(100);
            entity.Property(s => s.District).IsRequired().HasMaxLength(100);
            entity.Property(s => s.BrandSpecificStoreId).IsRequired().HasMaxLength(100);
        });

        // Bershka mağaza listesi — manuel toplanmış seed data (Faz 2.3).
        // BrandSpecificStoreId değerleri yer tutucudur; gerçek scraper (Faz 2.4) devreye girdiğinde
        // Bershka'nın kendi site/API'sinden gelen gerçek mağaza kodlarıyla güncellenmelidir.
        var bershkaId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var seedCreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Store>().HasData(
            new Store
            {
                Id = Guid.Parse("d1111111-0000-0000-0000-000000000001"),
                BrandId = bershkaId,
                BrandName = "Bershka",
                City = "Istanbul",
                District = "Kadikoy",
                StoreName = "Bershka Kadikoy",
                BrandSpecificStoreId = "BSK-IST-KDK-01",
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d1111111-0000-0000-0000-000000000002"),
                BrandId = bershkaId,
                BrandName = "Bershka",
                City = "Istanbul",
                District = "Sisli",
                StoreName = "Bershka Cevahir AVM",
                BrandSpecificStoreId = "BSK-IST-SSL-01",
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d1111111-0000-0000-0000-000000000003"),
                BrandId = bershkaId,
                BrandName = "Bershka",
                City = "Ankara",
                District = "Cankaya",
                StoreName = "Bershka Armada AVM",
                BrandSpecificStoreId = "BSK-ANK-CNK-01",
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d1111111-0000-0000-0000-000000000004"),
                BrandId = bershkaId,
                BrandName = "Bershka",
                City = "Izmir",
                District = "Bornova",
                StoreName = "Bershka Forum Bornova",
                BrandSpecificStoreId = "BSK-IZM-BRN-01",
                IsActive = true,
                CreatedAt = seedCreatedAt
            }
        );
    }
}
