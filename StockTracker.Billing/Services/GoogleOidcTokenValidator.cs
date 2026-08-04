using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StockTracker.Billing.Configuration;

namespace StockTracker.Billing.Services;

public interface IGoogleOidcTokenValidator
{
    Task<bool> ValidateAsync(string bearerToken, CancellationToken cancellationToken = default);
}

// Google Cloud Pub/Sub push abonelikleri, her push isteğinde bir OIDC ID token'ı (Authorization: Bearer)
// gönderir — webhook endpoint'inin gerçekten Google'dan geldiğini doğrulamanın standart yolu bu.
// Google'ın kendi imzalama anahtarları (JWKS) canlı olarak çekiliyor (sabit/gömülü bir sertifika YOK —
// Apple'ın x5c doğrulamasının aksine burada gerçek, resmi bir Google endpoint'ine karşı doğrulama yapmak
// mümkün ve güvenli, bkz. .claude/ARCHITECTURE.md > Billing).
public class GoogleOidcTokenValidator : IGoogleOidcTokenValidator
{
    private const string CertsUrl = "https://www.googleapis.com/oauth2/v3/certs";
    private static readonly string[] ValidIssuers = { "https://accounts.google.com", "accounts.google.com" };

    private readonly HttpClient _httpClient;
    private readonly GooglePlaySettings _settings;
    private readonly ILogger<GoogleOidcTokenValidator> _logger;

    public GoogleOidcTokenValidator(HttpClient httpClient, IOptions<GooglePlaySettings> settings, ILogger<GoogleOidcTokenValidator> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> ValidateAsync(string bearerToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.PushAudience) || _settings.PushAudience == "REPLACE_WITH_ENV")
        {
            _logger.LogWarning("GooglePlaySettings.PushAudience yapılandırılmamış — webhook doğrulaması atlanıyor, istek reddediliyor.");
            return false;
        }

        try
        {
            var jwksJson = await _httpClient.GetStringAsync(CertsUrl, cancellationToken);
            var jwks = new JsonWebKeySet(jwksJson);

            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(bearerToken, new TokenValidationParameters
            {
                ValidIssuers = ValidIssuers,
                ValidAudience = _settings.PushAudience,
                IssuerSigningKeys = jwks.Keys,
                ValidateLifetime = true
            }, out _);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Pub/Sub OIDC token doğrulaması başarısız.");
            return false;
        }
    }
}
