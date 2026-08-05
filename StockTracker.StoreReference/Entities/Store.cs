namespace StockTracker.StoreReference.Entities;

public class Store
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // product_db.Brands.Id ile eşleşen convention-based referans, FK değil (bkz. .claude/ARCHITECTURE.md).
    public Guid BrandId { get; set; }
    public string BrandName { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string? StoreName { get; set; }

    // Markanın kendi sitesi/API'sinde bu fiziksel mağazayı tanımlayan kod — scraper bunu kullanır.
    public string BrandSpecificStoreId { get; set; } = string.Empty;

    // Yalnızca Mango için dolu (Faz 6.1) — Mango'nun mağaza stok API'si mağaza ID'si değil enlem/boylam
    // ile "yakındaki mağazalar" sorgusu yapıyor; mağazanın KENDİ koordinatları verilirse güvenilir şekilde
    // sonuçta çıkıyor (bkz. Messages.V2.CheckStockCommand üstündeki not). Diğer markalarda null.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
