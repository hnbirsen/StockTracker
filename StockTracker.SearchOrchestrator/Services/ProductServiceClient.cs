using System.Net.Http.Json;
using System.Text.Json;
using StockTracker.SearchOrchestrator.DTOs;

namespace StockTracker.SearchOrchestrator.Services;

public interface IProductServiceClient
{
    Task<ProductLookupResponse?> LookupAsync(string productCode);
}

public class ProductServiceClient : IProductServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public ProductServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProductLookupResponse?> LookupAsync(string productCode)
    {
        // "/lookup/{**code}" bir catch-all route — kod slash içerebildiği için (örn. Zara "12345/678/123")
        // burada escape edilmeden, ham segment olarak gönderilir.
        var response = await _httpClient.GetAsync($"/lookup/{productCode}");
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<ProductLookupResponse>(JsonOptions);
    }
}
