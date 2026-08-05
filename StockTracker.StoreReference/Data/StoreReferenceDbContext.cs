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
        // Mango mağaza listesi (Faz 6.1) — BrandSpecificStoreId, Mango'nun kendi
        // `store-finder/v2/stores/stock` API'sinden dönen GERÇEK mağaza ID'leri. Bershka/Zara'nın aksine
        // bu API belirli bir mağaza ID'siyle değil enlem/boylam ile "yakındaki mağazalar" sorgusu yapıyor
        // (bkz. Messages.V2.CheckStockCommand üstündeki not) — bu yüzden Latitude/Longitude de dolduruldu
        // (her mağazanın API'nin kendi döndürdüğü gerçek koordinatları). Şişli/Bornova isim olarak
        // Bershka/Zara'nınkiyle birebir aynı AVM (Cevahir, Forum Bornova); Kadıköy için Bağdat Caddesi
        // üzerindeki gerçek Mango mağazası, Çankaya için CEPA AVM (Kentpark'ta Mango mağazası çıkmadı,
        // en yakın gerçek eşleşme CEPA).
        // H&M mağaza listesi (Faz 6.1) — BrandSpecificStoreId, H&M'in kendi `/tr_tr/sis/tr/{productid}/
        // {artid}` mağaza stok API'sinden dönen GERÇEK `storeCode` değerleri. Mango gibi bu API de belirli
        // bir mağaza ID'siyle değil enlem/boylam ile "yakındaki mağazalar" araması yapıyor (bkz.
        // Messages.V2.CheckStockCommand üstündeki not) — Latitude/Longitude dolduruldu. Kadıköy için Bağdat
        // Caddesi üzerindeki gerçek H&M mağazası; Şişli için Cevahir'de H&M çıkmadı, en yakın gerçek eşleşme
        // Özdilek Park AVM (Esentepe/Şişli); Çankaya için CEPA AVM (Mango'nunkiyle birebir aynı mağaza);
        // Bornova için Forum Bornova'da H&M çıkmadı, en yakın gerçek eşleşme İzmir Optimum AVM (gerçekte de
        // Bornova ilçesinde).
        // Massimo Dutti mağaza listesi (Faz 6.1) — BrandSpecificStoreId, Massimo Dutti'nin kendi
        // `itxrest/2/bam/store/{storeId}/physical-store` mağaza BULUCU API'sinden (yalnızca mağaza keşfi için
        // kullanıldı, enlem/boylam ile "yakındaki mağazalar" araması yapıyor) dönen GERÇEK mağaza ID'leri
        // (ör. "4483" = CEVAHIR). Latitude/Longitude yalnızca bu keşif amacıyla dolduruldu — gerçek mağaza
        // stok sorgusu (`api/storefront/1/stores/.../products/.../available-sizes`) çalışma zamanında bu
        // koordinatlara İHTİYAÇ DUYMUYOR, doğrudan BrandSpecificStoreId ile çalışıyor (Zara'daki gibi, bkz.
        // .claude/ARCHITECTURE.md > Massimo Dutti Scraper). Şişli için Cevahir AVM (Bershka/Zara/H&M'inkiyle
        // birebir aynı mağaza); Çankaya için Kentpark AVM (Bershka/Zara'nınkiyle birebir aynı mağaza);
        // Kadıköy'de gerçek bir Massimo Dutti mağazası çıkmadı, Anadolu yakasındaki en yakın gerçek eşleşme
        // Hilltown AVM (Maltepe) kullanıldı; Bornova'da da gerçek bir mağaza çıkmadı, İzmir'deki en yakın
        // gerçek eşleşme Karşıyaka Rönesans AVM kullanıldı. Mağaza bazlı stok sorgusu gerçek sayısal verilerle
        // canlı doğrulandı.
        // Beymen mağaza listesi (Faz 6.1) — BrandSpecificStoreId, diğer markalardan FARKLI olarak sayısal bir
        // ID değil, Beymen'in kendi `GET /api/store/getstorestock/{barcode}` API'sinin döndürdüğü mağazanın
        // KENDİ `Name` alanı (ör. "Beymen Suadiye") — bu API'de ayrı bir sayısal mağaza ID'si yok. Latitude/
        // Longitude API'nin kendi `Coordinate` alanından bilgi amaçlı dolduruldu (çalışma zamanında
        // kullanılmıyor, Zara/Massimo Dutti gibi doğrudan isimle sorgu atılıyor). Kadıköy ve Şişli için
        // gerçek Beymen mağazaları birebir ilçe eşleşmesiyle bulundu (Suadiye/Kadıköy, Nişantaşı/Şişli);
        // Çankaya için iki seçenekten (Panora, Kavaklıdere) Panora seçildi; Bornova'da gerçek bir mağaza
        // çıkmadı, İzmir'deki en yakın gerçek eşleşme Hilltown İzmir (Karşıyaka) kullanıldı (Massimo
        // Dutti'nin Bornova için seçtiği mağazayla aynı gerekçe/mesafe mantığı).
        // Pull&Bear mağaza listesi (Faz 6.1) — BrandSpecificStoreId, Massimo Dutti ile AYNI platformun
        // mağaza bulucu API'sinden (`itxrest/2/bam/store/{storeId}/physical-store`, yalnızca mağaza keşfi
        // için kullanıldı) dönen GERÇEK mağaza ID'leri. Gerçek stok sorgusu (`api/storefront/1/stores/.../
        // products/.../available-sizes`) doğrudan bu ID ile çalışıyor, enlem/boylam çalışma zamanında
        // GEREKMİYOR (Massimo Dutti/Zara'daki gibi). Kadıköy için Kozyatağı'ndaki City's AVM (Bershka'nın
        // seçtiği mahalleyle aynı gerekçe); Şişli için Cevahir AVM (diğer markalarla birebir aynı mağaza);
        // Çankaya için Kentpark AVM (Bershka/Zara/Massimo Dutti'yle birebir aynı mağaza); Bornova için Forum
        // Bornova AVM — bu markada GERÇEK bir Bornova eşleşmesi bulundu (diğer markaların çoğunda en yakın
        // eşleşmeye gidilmesi gerekmişti).
        var bershkaId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var zaraId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
        var pullBearId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012");
        var mangoId = Guid.Parse("d4e5f6a7-b8c9-0123-defa-234567890123");
        var hmId = Guid.Parse("e5f6a7b8-c9d0-1234-eabc-345678901234");
        var massimoDuttiId = Guid.Parse("f6a7b8c9-d0e1-2345-fabc-456789012345");
        var beymenId = Guid.Parse("a7b8c9d0-e1f2-3456-abcd-567890123456");
        var stradivariusId = Guid.Parse("b8c9d0e1-f2a3-4567-bcde-678901234567");
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
            },
            new Store
            {
                Id = Guid.Parse("d3333333-0000-0000-0000-000000000001"),
                BrandId = mangoId,
                BrandName = "Mango",
                City = "Istanbul",
                District = "Kadikoy",
                StoreName = "Bağdat Caddesi (Suadiye)",
                BrandSpecificStoreId = "10389",
                Latitude = 40.959937009724,
                Longitude = 29.080951331352,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d3333333-0000-0000-0000-000000000002"),
                BrandId = mangoId,
                BrandName = "Mango",
                City = "Istanbul",
                District = "Sisli",
                StoreName = "Cevahir AVM",
                BrandSpecificStoreId = "10277",
                Latitude = 41.06278401465,
                Longitude = 28.992832831243,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d3333333-0000-0000-0000-000000000003"),
                BrandId = mangoId,
                BrandName = "Mango",
                City = "Ankara",
                District = "Cankaya",
                StoreName = "CEPA AVM",
                BrandSpecificStoreId = "10403",
                Latitude = 39.90971454185,
                Longitude = 32.778216907751,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d3333333-0000-0000-0000-000000000004"),
                BrandId = mangoId,
                BrandName = "Mango",
                City = "Izmir",
                District = "Bornova",
                StoreName = "Forum Bornova",
                BrandSpecificStoreId = "10711",
                Latitude = 38.450381582438,
                Longitude = 27.209401193083,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d4444444-0000-0000-0000-000000000001"),
                BrandId = hmId,
                BrandName = "H&M",
                City = "Istanbul",
                District = "Kadikoy",
                StoreName = "Bağdat Caddesi",
                BrandSpecificStoreId = "TR0030",
                Latitude = 40.96030285769096,
                Longitude = 29.08093315025326,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d4444444-0000-0000-0000-000000000002"),
                BrandId = hmId,
                BrandName = "H&M",
                City = "Istanbul",
                District = "Sisli",
                StoreName = "Özdilek Park AVM",
                BrandSpecificStoreId = "TR0028",
                Latitude = 41.07764537422764,
                Longitude = 29.01283722778317,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d4444444-0000-0000-0000-000000000003"),
                BrandId = hmId,
                BrandName = "H&M",
                City = "Ankara",
                District = "Cankaya",
                StoreName = "CEPA AVM",
                BrandSpecificStoreId = "TR0007",
                Latitude = 39.90859342561093,
                Longitude = 32.77851787102509,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d4444444-0000-0000-0000-000000000004"),
                BrandId = hmId,
                BrandName = "H&M",
                City = "Izmir",
                District = "Bornova",
                StoreName = "Optimum AVM",
                BrandSpecificStoreId = "TR0075",
                Latitude = 38.338445,
                Longitude = 27.135329,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d5555555-0000-0000-0000-000000000001"),
                BrandId = massimoDuttiId,
                BrandName = "Massimo Dutti",
                City = "Istanbul",
                District = "Kadikoy",
                StoreName = "Hilltown AVM",
                BrandSpecificStoreId = "12013",
                Latitude = 40.953106,
                Longitude = 29.121725,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d5555555-0000-0000-0000-000000000002"),
                BrandId = massimoDuttiId,
                BrandName = "Massimo Dutti",
                City = "Istanbul",
                District = "Sisli",
                StoreName = "Cevahir AVM",
                BrandSpecificStoreId = "4483",
                Latitude = 41.063595,
                Longitude = 28.992115,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d5555555-0000-0000-0000-000000000003"),
                BrandId = massimoDuttiId,
                BrandName = "Massimo Dutti",
                City = "Ankara",
                District = "Cankaya",
                StoreName = "Kentpark AVM",
                BrandSpecificStoreId = "4009",
                Latitude = 39.909011,
                Longitude = 32.77629,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d5555555-0000-0000-0000-000000000004"),
                BrandId = massimoDuttiId,
                BrandName = "Massimo Dutti",
                City = "Izmir",
                District = "Bornova",
                StoreName = "Karşıyaka Rönesans AVM",
                BrandSpecificStoreId = "12840",
                Latitude = 38.4784351,
                Longitude = 27.0743432,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d6666666-0000-0000-0000-000000000001"),
                BrandId = beymenId,
                BrandName = "Beymen",
                City = "Istanbul",
                District = "Kadikoy",
                StoreName = "Beymen Suadiye",
                BrandSpecificStoreId = "Beymen Suadiye",
                Latitude = 40.957216,
                Longitude = 29.087570,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d6666666-0000-0000-0000-000000000002"),
                BrandId = beymenId,
                BrandName = "Beymen",
                City = "Istanbul",
                District = "Sisli",
                StoreName = "Beymen Nişantaşı",
                BrandSpecificStoreId = "Beymen Nişantaşı",
                Latitude = 41.049605,
                Longitude = 28.992227,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d6666666-0000-0000-0000-000000000003"),
                BrandId = beymenId,
                BrandName = "Beymen",
                City = "Ankara",
                District = "Cankaya",
                StoreName = "Beymen Panora",
                BrandSpecificStoreId = "Beymen Panora",
                Latitude = 39.84796,
                Longitude = 32.832813,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d6666666-0000-0000-0000-000000000004"),
                BrandId = beymenId,
                BrandName = "Beymen",
                City = "Izmir",
                District = "Bornova",
                StoreName = "Beymen Hilltown İzmir",
                BrandSpecificStoreId = "Beymen Hilltown İzmir",
                Latitude = 38.478249,
                Longitude = 27.073872,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d7777777-0000-0000-0000-000000000001"),
                BrandId = pullBearId,
                BrandName = "Pull&Bear",
                City = "Istanbul",
                District = "Kadikoy",
                StoreName = "City's Kozyatağı AVM",
                BrandSpecificStoreId = "16941",
                Latitude = 40.9800391,
                Longitude = 29.0993434,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d7777777-0000-0000-0000-000000000002"),
                BrandId = pullBearId,
                BrandName = "Pull&Bear",
                City = "Istanbul",
                District = "Sisli",
                StoreName = "Cevahir AVM",
                BrandSpecificStoreId = "5287",
                Latitude = 41.063595,
                Longitude = 28.992115,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d7777777-0000-0000-0000-000000000003"),
                BrandId = pullBearId,
                BrandName = "Pull&Bear",
                City = "Ankara",
                District = "Cankaya",
                StoreName = "Kentpark AVM",
                BrandSpecificStoreId = "6370",
                Latitude = 39.909011,
                Longitude = 32.77629,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d7777777-0000-0000-0000-000000000004"),
                BrandId = pullBearId,
                BrandName = "Pull&Bear",
                City = "Izmir",
                District = "Bornova",
                StoreName = "Forum Bornova AVM",
                BrandSpecificStoreId = "5334",
                Latitude = 38.45034027,
                Longitude = 27.2086791,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            // Stradivarius: `BrandSpecificStoreId` GERÇEK sayısal mağaza ID'leri (Zara/Massimo Dutti/
            // Pull&Bear'daki gibi) — kullanıcının kendi tarayıcısından paylaştığı gerçek `curl` istekleri
            // sayesinde bulunan `itxrest/2/bam/store/54009571/physical-store` mağaza bulucu API'sinden
            // (enlem/boylam ile "yakındaki mağazalar" araması, yalnızca KEŞİF için) doğrulandı. 4 mağaza da
            // Pull&Bear'ınkiyle BİREBİR AYNI AVM'ler (City's Kozyatağı, Cevahir, Kentpark, Forum Bornova) —
            // aynı bina, aynı enlem/boylam kullanıldı. Bkz. `.claude/ARCHITECTURE.md` > Stradivarius Scraper.
            new Store
            {
                Id = Guid.Parse("d8888888-0000-0000-0000-000000000001"),
                BrandId = stradivariusId,
                BrandName = "Stradivarius",
                City = "Istanbul",
                District = "Kadikoy",
                StoreName = "City's Kozyatağı AVM",
                BrandSpecificStoreId = "16879",
                Latitude = 40.9800391,
                Longitude = 29.0993434,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d8888888-0000-0000-0000-000000000002"),
                BrandId = stradivariusId,
                BrandName = "Stradivarius",
                City = "Istanbul",
                District = "Sisli",
                StoreName = "Cevahir AVM",
                BrandSpecificStoreId = "2859",
                Latitude = 41.063595,
                Longitude = 28.992115,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d8888888-0000-0000-0000-000000000003"),
                BrandId = stradivariusId,
                BrandName = "Stradivarius",
                City = "Ankara",
                District = "Cankaya",
                StoreName = "Kentpark AVM",
                BrandSpecificStoreId = "2968",
                Latitude = 39.909011,
                Longitude = 32.77629,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Store
            {
                Id = Guid.Parse("d8888888-0000-0000-0000-000000000004"),
                BrandId = stradivariusId,
                BrandName = "Stradivarius",
                City = "Izmir",
                District = "Bornova",
                StoreName = "Forum Bornova AVM",
                BrandSpecificStoreId = "2868",
                Latitude = 38.45034027,
                Longitude = 27.2086791,
                IsActive = true,
                CreatedAt = seedCreatedAt
            }
        );
    }
}
