namespace StockTracker.Billing.Configuration;

// Gerçek bir Google Play Console hesabı/service account'u bu oturumda yok (bkz. .claude/ARCHITECTURE.md >
// Billing) — yapılandırılmamışsa GooglePlayDeveloperClient gerçek bir çağrı yapmadan loglayıp null döner.
public class GooglePlaySettings
{
    // Service account JSON'ın tamamı (client_email, private_key, token_uri) — .env'e tek satır sığması
    // için Base64 ile saklanır.
    public string? ServiceAccountJsonBase64 { get; set; }
    public string? PackageName { get; set; }

    // Pub/Sub push endpoint'inin OIDC audience'ı (webhook doğrulaması için) — genelde webhook URL'inin kendisi.
    public string? PushAudience { get; set; }

    private static bool IsSet(string? value) => !string.IsNullOrWhiteSpace(value) && value != "REPLACE_WITH_ENV";

    public bool IsConfigured => IsSet(ServiceAccountJsonBase64) && IsSet(PackageName);
}
