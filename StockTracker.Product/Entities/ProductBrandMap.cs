namespace StockTracker.Product.Entities;

public class ProductBrandMap
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductCode { get; set; } = string.Empty;
    public Guid BrandId { get; set; }
    public ResolvedVia ResolvedVia { get; set; }
    public ConfidenceLevel Confidence { get; set; }
    public string? ProductUrl { get; set; }
    public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;

    public Brand Brand { get; set; } = null!;
}

public enum ResolvedVia
{
    FormatMatch = 1,
    SiteSearch = 2,
    SearchEngine = 3,
    Manual = 4
}

public enum ConfidenceLevel
{
    Low = 1,
    Medium = 2,
    High = 3
}
