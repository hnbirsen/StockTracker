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
            // URL bazlı arama için (bkz. IProductLookupService.LookupByUrlAsync) — unique DEĞİL, çünkü
            // teorik olarak aynı ürün sayfası farklı sorgu parametreleriyle (ör. Zara'nın v1/v2'si) birden
            // fazla kayda denk gelebilir; pratikte nadir ama kısıtlamayı gereksiz sıkı tutmuyoruz.
            entity.HasIndex(p => p.ProductUrl);
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
        // Faz 6.1'de eklenen markalar — BrandDetection/StoreReference'ta zaten mevcuttu ama Product
        // Service'in kendi Brands tablosuna hiç eklenmemişti (AddMissingBrandSeeds migration'ı yalnızca
        // Zara/Pull&Bear'ı eklemişti). Bu, ProductServiceClient.LookupAsync/SaveMappingAsync'in bu 6 marka
        // için hep IsResolved=false dönmesine (ya da SaveMapping'de FK hatasına) yol açan, canlı simülasyon
        // sırasında bulunan GERÇEK bir eksiklik — bkz. `.claude/PENDING_INPUTS.md`.
        var mangoId = Guid.Parse("d4e5f6a7-b8c9-0123-defa-234567890123");
        var hmId = Guid.Parse("e5f6a7b8-c9d0-1234-eabc-345678901234");
        var massimoDuttiId = Guid.Parse("f6a7b8c9-d0e1-2345-fabc-456789012345");
        var beymenId = Guid.Parse("a7b8c9d0-e1f2-3456-abcd-567890123456");
        var stradivariusId = Guid.Parse("b8c9d0e1-f2a3-4567-bcde-678901234567");
        var oyshoId = Guid.Parse("c9d0e1f2-a3b4-5678-cdef-789012345678");
        var maviId = Guid.Parse("d0e1f2a3-b4c5-6789-defa-890123456789");

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
            },
            new Brand
            {
                Id = mangoId,
                Name = "Mango",
                ScraperQueueName = "mango",
                SearchEndpoint = "https://shop.mango.com/tr/search",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Brand
            {
                Id = hmId,
                Name = "H&M",
                ScraperQueueName = "hm",
                SearchEndpoint = "https://www2.hm.com/tr_tr/search-results.html",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Brand
            {
                Id = massimoDuttiId,
                Name = "Massimo Dutti",
                ScraperQueueName = "massimodutti",
                SearchEndpoint = "https://www.massimodutti.com/tr/search",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Brand
            {
                Id = beymenId,
                Name = "Beymen",
                ScraperQueueName = "beymen",
                SearchEndpoint = "https://www.beymen.com/tr/arama",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Brand
            {
                Id = stradivariusId,
                Name = "Stradivarius",
                ScraperQueueName = "stradivarius",
                SearchEndpoint = "https://www.stradivarius.com/tr/search",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Brand
            {
                Id = oyshoId,
                Name = "Oysho",
                ScraperQueueName = "oysho",
                SearchEndpoint = "https://www.oysho.com/tr/search",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Brand
            {
                Id = maviId,
                Name = "Mavi",
                ScraperQueueName = "mavi",
                SearchEndpoint = "https://www.mavi.com/arama",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
