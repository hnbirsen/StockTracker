using System.Net.Http.Json;
using System.Text.Json;
using StockTracker.Notification.DTOs;

namespace StockTracker.Notification.Services;

public interface IIdentityServiceClient
{
    Task<string?> GetUserEmailAsync(Guid userId);
}

public class IdentityServiceClient : IIdentityServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public IdentityServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> GetUserEmailAsync(Guid userId)
    {
        var response = await _httpClient.GetAsync($"/internal/users/{userId}");
        if (!response.IsSuccessStatusCode) return null;

        var user = await response.Content.ReadFromJsonAsync<UserLookupResponse>(JsonOptions);
        return user?.Email;
    }
}
