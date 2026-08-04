namespace StockTracker.Notification.Configuration;

// Kullanıcı kararı: email gönderimi 3. taraf bir sağlayıcı (SendGrid/Postmark/SES vb.) üzerinden değil,
// kendi SMTP sunucumuz/relay'imiz üzerinden yapılır (bkz. .claude/ARCHITECTURE.md — Notification Service).
// Host/port/kullanıcı bilgileri henüz gerçek bir sunucudan gelmediği için (bu proje henüz kendi mail
// altyapısını kurmadı) tüm alanlar null olabilir — bu durumda SmtpEmailSender gerçek bir bağlantı
// denemeden loglayıp false döner (Faz 3.3/4.2'deki diğer sağlayıcılarla aynı graceful-degrade deseni).
public class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseSsl { get; set; } = true;
    public string FromEmail { get; set; } = "notifications@stocktracker.local";
    public string? FromName { get; set; }

    private static bool IsSet(string? value) => !string.IsNullOrWhiteSpace(value) && value != "REPLACE_WITH_ENV";

    // Username/Password bilinçli olarak IsConfigured'a dahil değil — bazı SMTP relay'leri (ör. yerel
    // ağdaki bir mail sunucusu, kimlik doğrulamasız bir relay) auth gerektirmeyebilir. Zorunlu olan tek
    // şey gerçek bir Host'un tanımlı olması.
    public bool IsConfigured => IsSet(Host);
}
