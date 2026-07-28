using Microsoft.EntityFrameworkCore;
using StockTracker.Product.Entities;

namespace StockTracker.Product.Data;

public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<ProductBrandMap> ProductBrandMaps => Set<ProductBrandMap>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.HasIndex(b => b.Name).IsUnique();
            entity.Property(b => b.Name).IsRequired().HasMaxLength(100);
            entity.Property(b => b.ScraperQueueName).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<ProductBrandMap>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => p.ProductCode).IsUnique();
            entity.Property(p => p.ProductCode).IsRequired().HasMaxLength(100);

            entity.HasOne(p => p.Brand)
                  .WithMany(b => b.ProductBrandMaps)
                  .HasForeignKey(p => p.BrandId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Bershka seed data
        var bershkaId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var zaraId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
        var pullbearId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012");
        
        modelBuilder.Entity<Brand>().HasData(
            new Brand
            {
                Id = bershkaId,
                Name = "Bershka",
                ScraperQueueName = "bershka",
                SearchEndpoint = "https://www.bershka.com/tr/search",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Brand
            {
                Id = zaraId,
                Name = "Zara",
                ScraperQueueName = "zara",
                SearchEndpoint = "https://www.zara.com/tr/search",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Brand
            {
                Id = pullbearId,
                Name = "Pull&Bear",
                ScraperQueueName = "pullbear",
                SearchEndpoint = "https://www.pullandbear.com/tr/search",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
