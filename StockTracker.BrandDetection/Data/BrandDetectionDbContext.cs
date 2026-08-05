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
        // Seed: Mango — 8 haneli ürün referansı / 2 haneli renk kodu (ör. "37013869/56"). Faz 6.1'de
        // shop.mango.com'un gerçek `online-orchestrator.mango.com/v4/products` API'sinden ("reference":
        // "37013869") ve ürün sayfası URL yapısından (.../37013869/56/00) doğrulandı — 8 haneli temel
        // referans kesin, ama fiziksel ürün etiketindeki TAM görünen format (ayraç dahil) doğrulanamadı
        // (bu yüzden Medium tutuldu, Zara/Bershka gibi High değil). Bilinçli olarak "/" ayraçlı bileşik
        // kod seçildi.
        // Seed: H&M — 7 haneli ürün kodu / 3 haneli renk kodu (ör. "1351887/001"). Faz 6.1'de
        // www2.hm.com'un gerçek `/tr_tr/sis/tr/{productid}/{artid}` mağaza stok endpoint'inin URL
        // yapısından VE PDP'nin `__NEXT_DATA__.props.pageProps.productPageProps.articleCode` alanının
        // ("1351887001" = productid+artid birleşik) bölünmesinden doğrulandı — Mango'daki gibi temel
        // format API üzerinden kesin ama fiziksel ürün etiketindeki TAM ayraçlı görünüm doğrulanamadı,
        // bu yüzden Medium tutuldu.
        // Seed: Massimo Dutti — 8 haneli temel referans / 3 haneli renk kodu (ör. "06244810/251"). Faz 6.1'de
        // massimodutti.com'un gerçek `#mdfrontw-state` SSR verisinden (`reference: "06244810-I2026"`,
        // `colors[].reference: "C06244810251-I2026"` — renk kodu `colors[].id`) doğrulandı. H&M'in 7+3
        // deseniyle ÇAKIŞMIYOR (ilk grup 8 hane, H&M'de 7) — Mango/H&M gibi temel format kesin ama fiziksel
        // ürün etiketindeki TAM ayraçlı görünüm doğrulanamadığı için Medium tutuldu.
        // Seed: Beymen — diğer markalardan FARKLI olarak ayraçsız, düz 7 haneli sayısal ürün ID'si (ör.
        // "1661415") — renk/varyant ayrı bir ürün sayfası/ID'si olarak modellendiği için (bkz.
        // `otherColorList`), tek bir productId zaten benzersiz. Faz 6.1'de gerçek
        // `sf-api/api/product/{id}/productsummary` API'sinden ve ürün URL yapısından (`p_{slug}_{id}`)
        // doğrulandı, birden fazla gerçek örnekle (1661415, 1884189, 2049912, 1652585, 1937139, ...) 7 haneli
        // olduğu teyit edildi. **Medium** tutuldu — ayraçsız 7 haneli bir desen diğer markaların ayraçlı
        // desenleriyle ÇAKIŞMIYOR ama tek bir marka örneği üzerinden genellendiği için High'a çıkarılmadı.
        // Seed: Pull&Bear — 8 haneli temel referans / 3 haneli renk kodu (ör. "07460338/250"). Faz 6.1'de
        // pullandbear.com'un gerçek `<product-modular>` custom element'inin `__product.detail` verisinden
        // (`reference: "07460338-I2026"`, `colors[].reference: "C07460338250-I2026"`) doğrulandı.
        // ⚠️ **BİLİNÇLİ, BELGELENEN ÇAKIŞMA**: bu desen Massimo Dutti'ninkiyle (`^\d{8}/\d{3}$`) BİREBİR AYNI
        // — iki marka aynı alt-yapıyı (aynı "MD Front" tarzı Inditex platformu) paylaştığı için. Saf regex
        // tabanlı `BrandCodeSignature` eşleşmesi bu iki markayı codE formatından AYIRT EDEMEZ; bir kod her
        // ikisiyle de eşleşecek ve BrandDetection Service'in "birden fazla aday → manuel çözüm" akışı
        // (zaten var olan mekanizma) devreye girecek. Bu bir hata değil, gerçek bir platform-paylaşımı
        // sonucu — bkz. `.claude/ARCHITECTURE.md` > Pull&Bear Scraper, `.claude/PENDING_INPUTS.md`. Medium
        // tutuldu (fiziksel etiket formatı ayrıca doğrulanmadığı için, Massimo Dutti'yle aynı gerekçe).
        var bershkaId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var zaraId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
        var pullbearId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012");
        var mangoId = Guid.Parse("d4e5f6a7-b8c9-0123-defa-234567890123");
        var hmId = Guid.Parse("e5f6a7b8-c9d0-1234-eabc-345678901234");
        var massimoDuttiId = Guid.Parse("f6a7b8c9-d0e1-2345-fabc-456789012345");
        var beymenId = Guid.Parse("a7b8c9d0-e1f2-3456-abcd-567890123456");
        var stradivariusId = Guid.Parse("b8c9d0e1-f2a3-4567-bcde-678901234567");

        modelBuilder.Entity<BrandCodeSignature>().HasData(
            new BrandCodeSignature
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                BrandId = bershkaId,
                BrandName = "Bershka",
                RegexPattern = @"^\d{11}$",
                // 11 haneli, önde sıfırlı, ayraçsız format diğer markalarla çakışmıyor (hepsi ayraçlı ya da
                // farklı hane sayısında) — Medium'dan High'a çıkarıldı, gerçek site verisiyle doğrulandığı için.
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
                RegexPattern = @"^\d{8}/\d{3}$",
                // Faz 6.1'de gerçek pullandbear.com verisiyle doğrulandı (bkz. sınıf üstündeki yorum) —
                // eski `^\d{8}$` (Low, tahminî) deseni gerçek formatla uyuşmuyordu. Massimo Dutti'yle
                // BİLİNÇLİ OLARAK ÇAKIŞAN bir desen (aynı platform) — Medium tutuldu.
                Confidence = ConfidenceLevel.Medium,
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
            },
            new BrandCodeSignature
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                BrandId = massimoDuttiId,
                BrandName = "Massimo Dutti",
                RegexPattern = @"^\d{8}/\d{3}$",
                Confidence = ConfidenceLevel.Medium,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new BrandCodeSignature
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                BrandId = beymenId,
                BrandName = "Beymen",
                RegexPattern = @"^\d{7}$",
                Confidence = ConfidenceLevel.Medium,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new BrandCodeSignature
            {
                Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                BrandId = stradivariusId,
                BrandName = "Stradivarius",
                RegexPattern = @"^\d{8}$",
                // Stradivarius'un URL yapısındaki ürün kodu (ör. "/tr/asimetrik-kareli-midi-elbise-l06383188"
                // -> "06383188") ayraçsız, düz 8 haneli sayısal — REF görünümündeki "6383/188/450"
                // (base/renk/ekstra) değerinin "0" + base(4) + renk(3) birleştirilmiş hali. Faz 6.1'de gerçek
                // stradivarius.com PDP'sinden doğrulandı. Diğer 8 haneli markalarla (Massimo Dutti, Pull&Bear
                // — ikisi de "^\d{8}/\d{3}$" ayraçlı) ÇAKIŞMIYOR çünkü Stradivarius'ta ayraç yok — ama tek bir
                // gerçek örnek üzerinden genellendiği ve fiziksel ürün etiketi formatı doğrulanmadığı için
                // Medium tutuldu (Beymen'deki gerekçeyle aynı).
                Confidence = ConfidenceLevel.Medium,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
