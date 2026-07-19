public class ProductBrandMap
{
    public string ProductCode { get; set; } = string.Empty;
    public int BrandId { get; set; }
    public decimal Confidence { get; set; }
    public string ResolvedVia { get; set; } = string.Empty;
    public DateTime ResolvedAt { get; set; }
}