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

        // Seed: Bershka — 11 haneli sayısal kod (önde sıfır + 4 haneli model + 3 haneli varyant + 3 haneli renk,
        // ör. REF "2891/054/426" -> "02891054426"). Faz 2.4'te gerçek bershka.com ürün sayfaları/asset URL'leri
        // üzerinden doğrulandı (bkz. .claude/ARCHITECTURE.md > Bershka Scraper) — BershkaStockApiClient'ın
        // stok API'sine gönderdiği "productCode" ile birebir aynı format olmalı. Eski `^\d{7,9}$` deseni
        // doğrulanmamış bir tahmindi ve gerçek formatla uyuşmuyordu.
        // Seed: Zara — 4 rakam / 3 rakam / 3 rakam formatı (displayReference + colorId, ör. "5063/821/802").
        // Faz 6.1'de gerçek zara.com ürün sayfaları üzerinden doğrulandı (bkz. .claude/ARCHITECTURE.md >
        // Zara Scraper) — çoklu gerçek örnekle (9083/479, 5372/323, 0962/307, 6224/308, ...) doğrulandı.
        // Eski `^\d{5}/\d{3}/\d{2,3}$` deseni doğrulanmamış bir tahmindi, gerçek formatla uyuşmuyordu
        // (ilk grup 5 değil 4 rakam, renk kodu her zaman 3 rakam).
        // Seed: Pull&Bear — 8 haneli sayısal
        // Seed: Mango — 8 haneli ürün referansı / 2 haneli renk kodu (ör. "37013869/56"). Faz 6.1'de
        // shop.mango.com'un gerçek `online-orchestrator.mango.com/v4/products` API'sinden ("reference":
        // "37013869") ve ürün sayfası URL yapısından (.../37013869/56/00) doğrulandı — 8 haneli temel
        // referans kesin, ama fiziksel ürün etiketindeki TAM görünen format (ayraç dahil) doğrulanamadı
        // (bu yüzden Medium tutuldu, Zara/Bershka gibi High değil). Bilinçli olarak "/" ayraçlı bileşik
        // kod seçildi — yalnızca `^\d{8}$` olsaydı Pull&Bear'ın (henüz doğrulanmamış) `^\d{8}$` deseniyle
        // birebir çakışırdı.
        // Seed: H&M — 7 haneli ürün kodu / 3 haneli renk kodu (ör. "1351887/001"). Faz 6.1'de
        // www2.hm.com'un gerçek `/tr_tr/sis/tr/{productid}/{artid}` mağaza stok endpoint'inin URL
        // yapısından VE PDP'nin `__NEXT_DATA__.props.pageProps.productPageProps.articleCode` alanının
        // ("1351887001" = productid+artid birleşik) bölünmesinden doğrulandı — Mango'daki gibi temel
        // format API üzerinden kesin ama fiziksel ürün etiketindeki TAM ayraçlı görünüm doğrulanamadı,
        // bu yüzden Medium tutuldu.
        var bershkaId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var zaraId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
        var pullbearId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012");
        var mangoId = Guid.Parse("d4e5f6a7-b8c9-0123-defa-234567890123");
        var hmId = Guid.Parse("e5f6a7b8-c9d0-1234-eabc-345678901234");

        modelBuilder.Entity<BrandCodeSignature>().HasData(
            new BrandCodeSignature
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                BrandId = bershkaId,
                BrandName = "Bershka",
                RegexPattern = @"^\d{11}$",
                // 11 haneli, önde sıfırlı format diğer markalarla çakışmıyor (Zara ayraçlı, Pull&Bear 8 haneli)
                // — Medium'dan High'a çıkarıldı, gerçek site verisiyle doğrulandığı için.
                Confidence = ConfidenceLevel.High,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new BrandCodeSignature
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                BrandId = zaraId,
                BrandName = "Zara",
                RegexPattern = @"^\d{4}/\d{3}/\d{3}$",
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
                // Low tutuldu — Pull&Bear'ın gerçek kod formatı henüz bir ürün sayfası üzerinden doğrulanmadı
                // (Bershka'nın artık 11 haneli olduğu doğrulandığı için önceki çakışma riski ortadan kalktı).
                Confidence = ConfidenceLevel.Low,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new BrandCodeSignature
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                BrandId = mangoId,
                BrandName = "Mango",
                RegexPattern = @"^\d{8}/\d{2}$",
                Confidence = ConfidenceLevel.Medium,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new BrandCodeSignature
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                BrandId = hmId,
                BrandName = "H&M",
                RegexPattern = @"^\d{7}/\d{3}$",
                Confidence = ConfidenceLevel.Medium,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
