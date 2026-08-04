using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using StockTracker.Notification.Configuration;

namespace StockTracker.Notification.Services;

public interface IEmailSender
{
    Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
}

// Kullanıcı kararı: 3. taraf bir email sağlayıcısı (SendGrid/Postmark/SES vb.) kullanılmıyor — kendi
// SMTP sunucumuz/relay'imiz üzerinden gönderim yapılıyor (MailKit ile, .NET'in kendi SmtpClient'ı Microsoft
// tarafından yeni geliştirme için önerilmiyor). SMTP ayarları (Host/Port/kimlik bilgileri) henüz gerçek bir
// mail sunucusundan gelmediği için (bkz. .claude/PENDING_INPUTS.md) yapılandırılmamışsa gönderim
// denenmeden loglanıp false döner — servis çökmez, yalnızca o bildirim gönderilmemiş sayılır
// (NotificationLog.Success=false).
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpSettings> settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("SMTP ayarları (Host) yapılandırılmamış — email gönderimi atlanıyor (alıcı: {ToEmail}).", toEmail);
            return false;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName ?? _settings.FromEmail, _settings.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        try
        {
            using var client = new SmtpClient();
            var socketOptions = _settings.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(_settings.Host!, _settings.Port, socketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_settings.Username))
                await client.AuthenticateAsync(_settings.Username, _settings.Password ?? string.Empty, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP email gönderimi başarısız — alıcı: {ToEmail}", toEmail);
            return false;
        }
    }
}
