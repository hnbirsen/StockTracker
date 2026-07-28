using System.Net.Http.Json;
using System.Text.Json;
using StockTracker.SearchOrchestrator.DTOs;

namespace StockTracker.SearchOrchestrator.Services;

public interface IStoreReferenceServiceClient
{
    Task<List<StoreDto>> GetStoresAsync(Guid brandId, string city, string district);
}

public class StoreReferenceServiceClient : IStoreReferenceServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public StoreReferenceServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<StoreDto>> GetStoresAsync(Guid brandId, string city, string district)
    {
        var url = $"/stores?brandId={brandId}&city={Uri.EscapeDataString(city)}&district={Uri.EscapeDataString(district)}";
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode) return new List<StoreDto>();

        var stores = await response.Content.ReadFromJsonAsync<List<StoreDto>>(JsonOptions);
        return stores ?? new List<StoreDto>();
    }
}
