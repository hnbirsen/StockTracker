namespace StockTracker.Billing.Configuration;

// Gerçek bir Apple Developer Program hesabı/anahtarı bu oturumda yok (bkz. .claude/ARCHITECTURE.md >
// Billing) — tüm alanlar null olabilir, bu durumda AppleAppStoreServerClient gerçek bir çağrı yapmadan
// loglayıp null döner (Faz 3.3'teki SendGrid/FCM ile aynı "graceful degrade" deseni).
public class AppleStoreSettings
{
    public string? IssuerId { get; set; }
    public string? KeyId { get; set; }

    // .p8 private key içeriği (PEM), tek satırlık .env formatına sığması için Base64 ile saklanır.
    public string? PrivateKeyBase64 { get; set; }
    public string? BundleId { get; set; }

    // "Sandbox" | "Production" — Faz 5.4 öncesi gerçek satın alma testleri yalnızca Sandbox'ta yapılabilir.
    public string Environment { get; set; } = "Sandbox";

    private static bool IsSet(string? value) => !string.IsNullOrWhiteSpace(value) && value != "REPLACE_WITH_ENV";

    public bool IsConfigured =>
        IsSet(IssuerId) && IsSet(KeyId) && IsSet(PrivateKeyBase64) && IsSet(BundleId);
}
