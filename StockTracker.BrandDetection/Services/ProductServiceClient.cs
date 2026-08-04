namespace StockTracker.BrandDetection.Services;

public interface IProductServiceClient
{
    Task SaveMappingAsync(string productCode, Guid brandId, string resolvedVia, string confidence);
}

public class ProductServiceClient : IProductServiceClient
{
    private readonly HttpClient _httpClient;

    public ProductServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task SaveMappingAsync(string productCode, Guid brandId, string resolvedVia, string confidence)
    {
        var resolvedViaInt = resolvedVia switch
        {
            "FormatMatch" => 1,
            "SiteSearch" => 2,
            "SearchEngine" => 3,
            "Manual" => 4,
            _ => 4
        };

        var confidenceInt = confidence switch
        {
            "Low" => 1,
            "Medium" => 2,
            "High" => 3,
            _ => 1
        };

        var payload = new
        {
            productCode,
            brandId,
            resolvedVia = resolvedViaInt,
            confidence = confidenceInt
        };

        var response = await _httpClient.PostAsJsonAsync("/mappings", payload);
        response.EnsureSuccessStatusCode();
    }
}
