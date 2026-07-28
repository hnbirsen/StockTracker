using Microsoft.EntityFrameworkCore;
using StockTracker.BrandDetection.Entities;

namespace StockTracker.BrandDetection.Data;

public class BrandDetectionDbContext : DbContext
{
    public BrandDetectionDbContext(DbContextOptions<BrandDetectionDbContext> options) : base(options) { }

    public DbSet<BrandCodeSignature> BrandCodeSignatures => Set<BrandCodeSignature>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BrandCodeSignature>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.BrandName).IsRequired().HasMaxLength(100);
            entity.Property(b => b.RegexPattern).IsRequired().HasMaxLength(200);
        });

        // Seed: Bershka — 7-9 haneli sayısal iç kod
        // Seed: Zara — 5 rakam / 3 rakam / 3 rakam formatı (örn. 12345/678/123)
        // Seed: Pull&Bear — 8 haneli sayısal
        var bershkaId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var zaraId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
        var pullbearId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012");

        modelBuilder.Entity<BrandCodeSignature>().HasData(
            new BrandCodeSignature
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                BrandId = bershkaId,
                BrandName = "Bershka",
                RegexPattern = @"^\d{7,9}$",
                Confidence = ConfidenceLevel.Medium,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new BrandCodeSignature
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                BrandId = zaraId,
                BrandName = "Zara",
                RegexPattern = @"^\d{5}/\d{3}/\d{2,3}$",
                Confidence = ConfidenceLevel.High,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new BrandCodeSignature
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                BrandId = pullbearId,
                BrandName = "Pull&Bear",
                RegexPattern = @"^\d{8}$",
                Confidence = ConfidenceLevel.Low, // Bershka ile çakışma riski var
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
