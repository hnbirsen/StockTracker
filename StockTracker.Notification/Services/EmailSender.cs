using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StockTracker.Notification.Services;

public interface IEmailSender
{
    Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
}

// SendGrid v3 Mail Send API (https://api.sendgrid.com/v3/mail/send) ile gerçek entegrasyon.
// SENDGRID_API_KEY henüz gerçek bir hesaptan alınmadığı için (bkz. .claude/ARCHITECTURE.md — Notification
// Service) yapılandırılmamışsa gönderim denenmeden false döner ve loglanır — servis bu durumda çökmez,
// yalnızca o kanaldaki bildirim gönderilmemiş sayılır (NotificationLog.Success=false).
public class SendGridEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _fromEmail;
    private readonly ILogger<SendGridEmailSender> _logger;

    public SendGridEmailSender(HttpClient httpClient, string? apiKey, string fromEmail, ILogger<SendGridEmailSender> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _fromEmail = fromEmail;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "REPLACE_WITH_ENV")
        {
            _logger.LogWarning("SENDGRID_API_KEY yapılandırılmamış — email gönderimi atlanıyor (alıcı: {ToEmail}).", toEmail);
            return false;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/v3/mail/send")
        {
            Content = JsonContent.Create(new
            {
                personalizations = new[] { new { to = new[] { new { email = toEmail } } } },
                from = new { email = _fromEmail },
                subject,
                content = new[] { new { type = "text/plain", value = body } }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "SendGrid email gönderimi başarısız — alıcı: {ToEmail}, statusCode: {StatusCode}",
                toEmail, response.StatusCode);
        }

        return response.IsSuccessStatusCode;
    }
}
