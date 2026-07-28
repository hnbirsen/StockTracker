using System.Net.Http.Json;
using System.Text.Json;
using StockTracker.SearchOrchestrator.DTOs;

namespace StockTracker.SearchOrchestrator.Services;

public interface IBrandDetectionServiceClient
{
    Task<ResolveResponse> ResolveAsync(string productCode);
}

public class BrandDetectionServiceClient : IBrandDetectionServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public BrandDetectionServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ResolveResponse> ResolveAsync(string productCode)
    {
        var response = await _httpClient.PostAsJsonAsync("/resolve", new { productCode });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ResolveResponse>(JsonOptions);
        return result ?? new ResolveResponse(productCode, false, new List<BrandCandidateDto>());
    }
}
