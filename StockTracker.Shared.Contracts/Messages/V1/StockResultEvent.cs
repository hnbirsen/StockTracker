namespace StockTracker.Shared.Contracts.Messages.V1;

public enum StockStatus
{
    InStock,
    OutOfStock,
    Unknown
}

// v1 sözleşmesi: breaking değişiklik gerekirse yeni tip V2 namespace'inde eklenir, bu tip değişmez.
// StoreId, ilgili CheckStockCommand'daki gibi nullable — online-only kontrollerde null'dur.
// ScraperSource'ta olduğu gibi (varsayılan değerli, opsiyonel alan) Quantity/IsLastUnit da doğrudan V1'e
// eklendi — MassTransit JSON tabanlı serialize ettiği ve tüm servisler aynı paylaşılan tipi kullandığı
// için bu, eski tüketicileri bozmayan, geriye dönük uyumlu bir ekleme (ayrı bir V2 tipi + tüm
// consumer'ların migrate edilmesi gereken CheckStockCommand örneğinden farklı olarak, burada MEVCUT
// consumer'lar hiçbir değişiklik yapmadan çalışmaya devam eder, yalnızca yeni alanları okumak isteyenler
// güncellenir).
//
// Faz 6.1'de kullanıcı talebiyle eklendi ("stokta son ürün mü, mağazaya gidene kadar bitebilir mi
// bilgisini kullanıcıya göstermek istiyorum") — markalar arası veri şekli FARKLI olduğu için ikisi de
// nullable ve BAĞIMSIZ:
//   - Bershka/Zara mağaza sorgusu: gerçek bir sayısal miktar veriyor (`quantity`/`stock` alanları) ->
//     Quantity dolu, IsLastUnit bu sayıdan türetiliyor (`Quantity == 1`).
//   - Mango: ne online ne mağaza sorgusu sayısal miktar vermiyor, ama İKİSİ DE doğrudan bir
//     `isLastUnit`/`lastUnits` boolean bayrağı veriyor (canlı doğrulandı — mağaza yanıtında
//     `sizesIds[].isLastUnit`, online PDP'de `lastUnits`) -> Quantity her zaman null, IsLastUnit
//     API'nin kendi bayrağından doğrudan geliyor.
//   - Hiçbiri veri vermiyorsa (ör. yalnızca "in_stock" gibi bir string enum) ikisi de null kalır —
//     "bilmiyoruz" ile "son ürün değil" birbirine karıştırılmamalı.
public record StockResultEvent(
    Guid CommandId,
    string ProductCode,
    Guid BrandId,
    string Size,
    Guid? StoreId,
    StockStatus Status,
    DateTime CheckedAt,
    string? ScraperSource = null,
    int? Quantity = null,
    bool? IsLastUnit = null
);
