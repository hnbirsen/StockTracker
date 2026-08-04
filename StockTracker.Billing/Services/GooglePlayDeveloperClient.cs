using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StockTracker.Billing.Configuration;

namespace StockTracker.Billing.Services;

public record GoogleSubscriptionInfo(DateTimeOffset? ExpiryTime, bool AutoRenewing, int? CancelReason);

public interface IGooglePlayDeveloperClient
{
    Task<GoogleSubscriptionInfo?> GetSubscriptionAsync(string subscriptionId, string purchaseToken, CancellationToken cancellationToken = default);
}

// Play Developer API (server-to-server) — kimlik doğrulama, service account'un JWT-bearer OAuth2 akışıyla
// (RS256, service account JSON'daki private_key ile imzalanır) elde edilen bir access_token üzerinden.
public class GooglePlayDeveloperClient : IGooglePlayDeveloperClient
{
    private const string TokenScope = "https://www.googleapis.com/auth/androidpublisher";

    private readonly HttpClient _httpClient;
    private readonly GooglePlaySettings _settings;
    private readonly ILogger<GooglePlayDeveloperClient> _logger;

    public GooglePlayDeveloperClient(HttpClient httpClient, IOptions<GooglePlaySettings> settings, ILogger<GooglePlayDeveloperClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<GoogleSubscriptionInfo?> GetSubscriptionAsync(string subscriptionId, string purchaseToken, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Google Play ayarları yapılandırılmamış — doğrulama atlanıyor.");
            return null;
        }

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        if (accessToken is null)
            return null;

        var url = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{_settings.PackageName}"
            + $"/purchases/subscriptions/{subscriptionId}/tokens/{purchaseToken}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google Play Developer API çağrısı başarısız — statusCode: {StatusCode}", response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        DateTimeOffset? expiryTime = body.TryGetProperty("expiryTimeMillis", out var expiryProp) && long.TryParse(expiryProp.GetString(), out var millis)
            ? DateTimeOffset.FromUnixTimeMilliseconds(millis)
            : null;
        var autoRenewing = body.TryGetProperty("autoRenewing", out var renewProp) && renewProp.GetBoolean();
        int? cancelReason = body.TryGetProperty("cancelReason", out var cancelProp) ? cancelProp.GetInt32() : null;

        return new GoogleSubscriptionInfo(expiryTime, autoRenewing, cancelReason);
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var serviceAccountJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(_settings.ServiceAccountJsonBase64!));
            using var doc = JsonDocument.Parse(serviceAccountJson);
            var root = doc.RootElement;
            var clientEmail = root.GetProperty("client_email").GetString()!;
            var privateKeyPem = root.GetProperty("private_key").GetString()!;
            var tokenUri = root.TryGetProperty("token_uri", out var tokenUriProp)
                ? tokenUriProp.GetString()!
                : "https://oauth2.googleapis.com/token";

            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var signingKey = new RsaSecurityKey(rsa);
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

            var now = DateTime.UtcNow;
            var assertion = new JwtSecurityToken(
                issuer: clientEmail,
                audience: tokenUri,
                claims: new[] { new Claim("scope", TokenScope) },
                notBefore: now,
                expires: now.AddHours(1),
                signingCredentials: credentials
            );
            var assertionJwt = new JwtSecurityTokenHandler().WriteToken(assertion);

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertionJwt
                })
            };

            var tokenResponse = await _httpClient.SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google OAuth2 token exchange başarısız — statusCode: {StatusCode}", tokenResponse.StatusCode);
                return null;
            }

            var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return tokenBody.GetProperty("access_token").GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google service account JWT-bearer token exchange sırasında hata.");
            return null;
        }
    }
}
