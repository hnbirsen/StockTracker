public class Plan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int MaxTrackedProducts { get; set; }
    public int CheckFrequencyInMinutes { get; set; }
    public decimal Price { get; set; }
}