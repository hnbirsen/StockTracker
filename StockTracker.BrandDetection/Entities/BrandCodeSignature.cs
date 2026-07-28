namespace StockTracker.BrandDetection.Entities;

public class BrandCodeSignature
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Hangi markaya ait (Product Service'teki Brand.Id ile eşleşir)
    public Guid BrandId { get; set; }
    public string BrandName { get; set; } = string.Empty;

    // Regex pattern (örn. ^\d{7}$ → Bershka iç kodu)
    public string RegexPattern { get; set; } = string.Empty;

    public ConfidenceLevel Confidence { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ConfidenceLevel
{
    Low = 1,
    Medium = 2,
    High = 3
}
