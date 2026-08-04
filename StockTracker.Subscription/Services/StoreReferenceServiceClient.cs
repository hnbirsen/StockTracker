using System.Net.Http.Json;
using System.Text.Json;
using StockTracker.Subscription.DTOs;

namespace StockTracker.Subscription.Services;

public interface IStoreReferenceServiceClient
{
    Task<StoreDto?> GetStoreByIdAsync(Guid storeId);
}

public class StoreReferenceServiceClient : IStoreReferenceServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public StoreReferenceServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<StoreDto?> GetStoreByIdAsync(Guid storeId)
    {
        var response = await _httpClient.GetAsync($"/stores/{storeId}");
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<StoreDto>(JsonOptions);
    }
}
