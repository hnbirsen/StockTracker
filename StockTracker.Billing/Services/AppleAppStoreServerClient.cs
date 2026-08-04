using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StockTracker.Billing.Configuration;

namespace StockTracker.Billing.Services;

public record AppleTransactionInfo(string OriginalTransactionId, string ProductId, DateTimeOffset? ExpiresAt, string? Status);

public interface IAppleAppStoreServerClient
{
    Task<AppleTransactionInfo?> GetTransactionInfoAsync(string transactionId, CancellationToken cancellationToken = default);
}

// App Store Server API (server-to-server) — App Store Server Notifications V2 formatındaki
// signedTransactionInfo ile aynı JWS yapısını kullanır (bkz. AppleJwsVerifier).
// Kimlik doğrulama: Apple'a özel bir JWT (ES256, .p8 anahtarla imzalanmış, kid=KeyId, iss=IssuerId,
// aud="appstoreconnect-v1", bid=BundleId) — her istekte taze üretilir (Apple max 60 dk ömür öneriyor,
// biz 5 dk kullanıyoruz, gereksiz yere uzun ömürlü token taşımamak için).
public class AppleAppStoreServerClient : IAppleAppStoreServerClient
{
    private const string ProductionBaseUrl = "https://api.storekit.itunes.apple.com";
    private const string SandboxBaseUrl = "https://api.storekit-sandbox.itunes.apple.com";

    private readonly HttpClient _httpClient;
    private readonly AppleStoreSettings _settings;
    private readonly IAppleJwsVerifier _jwsVerifier;
    private readonly ILogger<AppleAppStoreServerClient> _logger;

    public AppleAppStoreServerClient(
        HttpClient httpClient,
        IOptions<AppleStoreSettings> settings,
        IAppleJwsVerifier jwsVerifier,
        ILogger<AppleAppStoreServerClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _jwsVerifier = jwsVerifier;
        _logger = logger;
    }

    public async Task<AppleTransactionInfo?> GetTransactionInfoAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Apple App Store Server ayarları yapılandırılmamış — doğrulama atlanıyor.");
            return null;
        }

        var baseUrl = _settings.Environment.Equals("Production", StringComparison.OrdinalIgnoreCase)
            ? ProductionBaseUrl
            : SandboxBaseUrl;

        var jwt = BuildAuthToken();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/inApps/v1/transactions/{transactionId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Apple App Store Server API çağrısı başarısız — statusCode: {StatusCode}", response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        if (!body.TryGetProperty("signedTransactionInfo", out var signedTransactionInfoProp))
        {
            _logger.LogWarning("Apple yanıtında signedTransactionInfo bulunamadı.");
            return null;
        }

        if (!_jwsVerifier.TryVerifyAndDecode(signedTransactionInfoProp.GetString()!, out var transactionPayload))
        {
            _logger.LogWarning("Apple signedTransactionInfo imza doğrulaması başarısız.");
            return null;
        }

        var originalTransactionId = transactionPayload.GetProperty("originalTransactionId").GetString()!;
        var productId = transactionPayload.GetProperty("productId").GetString()!;
        DateTimeOffset? expiresAt = transactionPayload.TryGetProperty("expiresDate", out var expiresProp)
            ? DateTimeOffset.FromUnixTimeMilliseconds(expiresProp.GetInt64())
            : null;

        return new AppleTransactionInfo(originalTransactionId, productId, expiresAt, Status: null);
    }

    private string BuildAuthToken()
    {
        var privateKeyPem = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(_settings.PrivateKeyBase64!));

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        var signingKey = new ECDsaSecurityKey(ecdsa) { KeyId = _settings.KeyId };
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256);

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: _settings.IssuerId,
            audience: "appstoreconnect-v1",
            claims: new[] { new System.Security.Claims.Claim("bid", _settings.BundleId!) },
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
