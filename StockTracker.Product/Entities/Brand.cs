namespace StockTracker.Product.Entities;

public class Brand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ScraperQueueName { get; set; } = string.Empty;
    public string? SearchEndpoint { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProductBrandMap> ProductBrandMaps { get; set; } = new List<ProductBrandMap>();
}
