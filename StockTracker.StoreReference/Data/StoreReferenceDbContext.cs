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

        // Bershka mağaza listesi (Faz 2.3, Faz 2.4'te gerçek verilerle güncellendi).
        // BrandSpecificStoreId artık Bershka'nın kendi mağaza bulucu API'sinden (bkz. .claude/ARCHITECTURE.md
        // > Bershka Scraper > Mağaza bulucu) dönen GERÇEK physicalStoreId değerleri — scraper'ın stok API'sine
        // doğrudan bu ID'lerle sorgu atması gerekiyor. Her mağaza, ilgili il/ilçeye en yakın (Kadıköy için
        // Kozyatağı — Kadıköy ilçesi sınırları içinde bir mahalle; Şişli/Bornova için isim birebir eşleşiyor;
        // Çankaya için adreste "ÇANKAYA" geçen mağaza) gerçek Bershka mağazasıdır.
        // Zara mağaza listesi (Faz 6.1) — BrandSpecificStoreId, Zara'nın kendi
        // `z-maazalar-st1404.html` sayfasından (window.zara.viewPayload.physicalStoresList) okunan GERÇEK
        // physicalStoreId değerleri; aynı ID'ler store-product-availability sorgularında doğrudan
        // kullanılabiliyor (bkz. .claude/ARCHITECTURE.md > Zara Scraper). Bershka'nın mevcut il/ilçe
        // seçimiyle birebir eşleşecek şekilde seçildi: Kentpark (Çankaya) ve Forum Bornova (Bornova) isim
        // olarak Bershka'nınkiyle birebir aynı AVM; Kadıköy/Şişli için ilçe sınırları içindeki gerçek Zara
        // mağazaları kullanıldı.
        var bershkaId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var zaraId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
        var seedCreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Store>().HasData(
            new Store
            {
                Id = Guid.Parse("d1111111-0000-0000-0000-000000000001"),
                BrandId = bershkaId,
                BrandName = "Bershka",
                City = "Istanbul",
                District = "Kadikoy",
                StoreName = "City's Kozyatağı",
                BrandSpecificStoreId = "16884",
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
                StoreName = "Cevahir AVM",
                BrandSpecificStoreId = "8359",
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
                StoreName = "Kentpark",
                BrandSpecificStoreId = "6943",
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
                StoreName = "Forum Bornova",
                BrandSpecificStoreId = "8426",
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d2222222-0000-0000-0000-000000000001"),
                BrandId = zaraId,
                BrandName = "Zara",
                City = "Istanbul",
                District = "Kadikoy",
                StoreName = "Bağdat Caddesi",
                BrandSpecificStoreId = "3231",
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d2222222-0000-0000-0000-000000000002"),
                BrandId = zaraId,
                BrandName = "Zara",
                City = "Istanbul",
                District = "Sisli",
                StoreName = "Cevahir AVM",
                BrandSpecificStoreId = "12692",
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d2222222-0000-0000-0000-000000000003"),
                BrandId = zaraId,
                BrandName = "Zara",
                City = "Ankara",
                District = "Cankaya",
                StoreName = "Kentpark",
                BrandSpecificStoreId = "251",
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d2222222-0000-0000-0000-000000000004"),
                BrandId = zaraId,
                BrandName = "Zara",
                City = "Izmir",
                District = "Bornova",
                StoreName = "Forum Bornova",
                BrandSpecificStoreId = "3643",
                IsActive = true,
                CreatedAt = seedCreatedAt
            }
        );
    }
}
