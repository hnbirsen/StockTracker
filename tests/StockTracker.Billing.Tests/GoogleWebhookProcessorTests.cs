using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Billing.Entities;
using StockTracker.Billing.Services;

namespace StockTracker.Billing.Tests;

public class GoogleWebhookProcessorTests
{
    private readonly Mock<IGoogleOidcTokenValidator> _tokenValidator = new();
    private readonly Mock<IGooglePlayDeveloperClient> _playClient = new();
    private readonly Mock<IPaymentEventProcessor> _paymentEventProcessor = new();

    private GoogleWebhookProcessor CreateSut() => new(
        _tokenValidator.Object, _playClient.Object, _paymentEventProcessor.Object, Mock.Of<ILogger<GoogleWebhookProcessor>>());

    private static string BuildPubSubEnvelope(int notificationType, string purchaseToken = "token-1", string subscriptionId = "sub-1", string messageId = "msg-1")
    {
        var data = JsonSerializer.Serialize(new
        {
            packageName = "com.stocktracker.app",
            eventTimeMillis = "1700000000000",
            subscriptionNotification = new { version = "1.0", notificationType, purchaseToken, subscriptionId }
        });
        var dataB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(data));

        return JsonSerializer.Serialize(new
        {
            message = new { data = dataB64, messageId, publishTime = "2026-01-01T00:00:00Z" },
            subscription = "projects/x/subscriptions/y"
        });
    }

    [Fact]
    public async Task ProcessAsync_WhenTokenMissing_ReturnsUnauthorizedWithoutCallingProcessor()
    {
        var sut = CreateSut();
        var result = await sut.ProcessAsync(null, "{}", CancellationToken.None);

        result.Should().Be(GoogleWebhookResult.Unauthorized);
        _paymentEventProcessor.Verify(p => p.ProcessAsync(
            It.IsAny<Platform>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<SubscriptionStatus>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenTokenInvalid_ReturnsUnauthorized()
    {
        _tokenValidator.Setup(v => v.ValidateAsync("bad-token", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var sut = CreateSut();

        var result = await sut.ProcessAsync("bad-token", "{}", CancellationToken.None);

        result.Should().Be(GoogleWebhookResult.Unauthorized);
    }

    [Fact]
    public async Task ProcessAsync_WithSubscriptionRenewedNotification_MapsToActiveAndCallsProcessor()
    {
        _tokenValidator.Setup(v => v.ValidateAsync("good-token", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _playClient
            .Setup(c => c.GetSubscriptionAsync("sub-1", "token-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleSubscriptionInfo(DateTimeOffset.UtcNow.AddMonths(1), true, null));

        var body = BuildPubSubEnvelope(notificationType: 2); // SUBSCRIPTION_RENEWED
        var sut = CreateSut();

        var result = await sut.ProcessAsync("good-token", body, CancellationToken.None);

        result.Should().Be(GoogleWebhookResult.Processed);
        _paymentEventProcessor.Verify(p => p.ProcessAsync(
            Platform.Google, "msg-1", "2", body, "token-1", SubscriptionStatus.Active, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithUnmappedNotificationType_ReturnsSkippedWithoutCallingProcessor()
    {
        _tokenValidator.Setup(v => v.ValidateAsync("good-token", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var body = BuildPubSubEnvelope(notificationType: 8); // PRICE_CHANGE_CONFIRMED — haritada yok
        var sut = CreateSut();

        var result = await sut.ProcessAsync("good-token", body, CancellationToken.None);

        result.Should().Be(GoogleWebhookResult.Skipped);
        _paymentEventProcessor.Verify(p => p.ProcessAsync(
            It.IsAny<Platform>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<SubscriptionStatus>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithMalformedJson_ReturnsInvalidPayload()
    {
        _tokenValidator.Setup(v => v.ValidateAsync("good-token", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = CreateSut();

        var result = await sut.ProcessAsync("good-token", "not-json", CancellationToken.None);

        result.Should().Be(GoogleWebhookResult.InvalidPayload);
    }
}
