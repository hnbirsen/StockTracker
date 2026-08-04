using System.Net.Http.Json;
using System.Text.Json;

namespace StockTracker.Notification.Services;

public interface ISubscriptionServiceClient
{
    Task<List<Guid>> GetWatcherUserIdsAsync(string productCode, string size, Guid? storeId);
}

public class SubscriptionServiceClient : ISubscriptionServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public SubscriptionServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Guid>> GetWatcherUserIdsAsync(string productCode, string size, Guid? storeId)
    {
        var url = $"/internal/watchers?productCode={Uri.EscapeDataString(productCode)}&size={Uri.EscapeDataString(size)}";
        if (storeId.HasValue)
            url += $"&storeId={storeId.Value}";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return new List<Guid>();

        var userIds = await response.Content.ReadFromJsonAsync<List<Guid>>(JsonOptions);
        return userIds ?? new List<Guid>();
    }
}
