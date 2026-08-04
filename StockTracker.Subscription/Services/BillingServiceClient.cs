using System.Net.Http.Json;
using System.Text.Json;
using StockTracker.Subscription.DTOs;

namespace StockTracker.Subscription.Services;

public interface IBillingServiceClient
{
    Task<UserLimitsResponse?> GetUserLimitsAsync(Guid userId);
}

public class BillingServiceClient : IBillingServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public BillingServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UserLimitsResponse?> GetUserLimitsAsync(Guid userId)
    {
        var response = await _httpClient.GetAsync($"/limits/{userId}");
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<UserLimitsResponse>(JsonOptions);
    }
}
