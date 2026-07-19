public class Brand : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string SearchEndpoint { get; set; } = string.Empty;
    public string ScraperQueueName { get; set; } = string.Empty;
}