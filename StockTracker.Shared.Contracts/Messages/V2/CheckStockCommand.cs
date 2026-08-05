namespace StockTracker.Shared.Contracts.Messages.V2;

// V1'e eklenen tek alan: ProductUrl. V1 değiştirilmedi (bkz. Messages.V1.CheckStockCommand) — bu,
// dokümante edilen versiyonlama stratejisinin ("breaking değişiklik gerekirse yeni tip V2 namespace'inde
// eklenir") ilk kullanımı.
//
// ProductUrl gerekli oldu çünkü Bershka'nın gerçek stok API'si "productCode" ve "size"tan bir
// part-number/campaign inşa etmeye izin vermiyor: ürün kategorisine göre değişen bir önek hanesi
// (giyim "0", ayakkabı "1", ...) ve alfabetik bedenlerde (XS/S/M/L) ürüne özgü, tahmin edilemeyen bir
// sıra numarası var. Tek güvenilir kaynak, ürünün kendi sayfasında Bershka'nın halihazırda hesapladığı
// gerçek partnumber/stok bilgisi — bu da sayfanın URL'sini gerektiriyor (bkz. BershkaStockApiClient).
// ProductUrl, Product Service'in ProductBrandMap.ProductUrl alanından geliyor; henüz doldurulmamışsa
// (örn. yalnızca regex format eşleşmesiyle çözülmüş ürünler) null kalır ve scraper Unknown döner.
//
// StoreLatitude/StoreLongitude (Faz 6.1, Mango Scraper): Mango'nun mağaza bazlı stok API'si
// (store-finder/v2/stores/stock) Zara/Bershka'nın aksine belirli bir mağaza ID'siyle değil, bir
// enlem/boylam etrafında "yakındaki mağazalar" araması yapıyor — sorgulanan mağazanın KENDİ
// koordinatları verilirse o mağaza (mesafe ≈ 0) güvenilir şekilde sonuç listesinde çıkıyor (canlı
// veriyle doğrulandı). Bu yüzden `Store` entity'sine (`store_db`) nullable Latitude/Longitude eklendi;
// yalnızca Mango için dolu, diğer markalar için null kalır (`BrandSpecificStoreId` zaten yeterli).
public record CheckStockCommand(
    Guid CommandId,
    string ProductCode,
    Guid BrandId,
    string BrandName,
    string Size,
    Guid? StoreId,
    string? BrandSpecificStoreId,
    string? City,
    string? District,
    string? ProductUrl,
    DateTime RequestedAt,
    double? StoreLatitude = null,
    double? StoreLongitude = null
);
