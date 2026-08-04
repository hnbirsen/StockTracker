using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StockTracker.Notification.Services;

public interface IPushSender
{
    Task<bool> SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default);
}

// FCM legacy HTTP API (https://fcm.googleapis.com/fcm/send, "Authorization: key=<ServerKey>") ile gerçek
// entegrasyon — roadmap'in "FCM Server Key al" adımıyla birebir eşleşen, OAuth2/service-account akışı
// gerektirmeyen basit yöntem. FCM_SERVER_KEY henüz gerçek bir Firebase projesinden alınmadığı için (bkz.
// .claude/ARCHITECTURE.md — Notification Service) yapılandırılmamışsa gönderim denenmeden false döner.
// Pratikte bu fazda hiç çağrılmıyor: cihaz token'ı hiçbir yerde saklanmıyor (bkz. NoOpDeviceTokenProvider,
// Faz 5.4 — React Native — gerçek implementasyonu getirecek), ama sınıf kendi başına tam ve test edilebilir.
public class FcmPushSender : IPushSender
{
    private readonly HttpClient _httpClient;
    private readonly string? _serverKey;
    private readonly ILogger<FcmPushSender> _logger;

    public FcmPushSender(HttpClient httpClient, string? serverKey, ILogger<FcmPushSender> logger)
    {
        _httpClient = httpClient;
        _serverKey = serverKey;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_serverKey) || _serverKey == "REPLACE_WITH_ENV")
        {
            _logger.LogWarning("FCM_SERVER_KEY yapılandırılmamış — push gönderimi atlanıyor.");
            return false;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/fcm/send")
        {
            Content = JsonContent.Create(new
            {
                to = deviceToken,
                notification = new { title, body }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("key", _serverKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("FCM push gönderimi başarısız — statusCode: {StatusCode}", response.StatusCode);
        }

        return response.IsSuccessStatusCode;
    }
}
