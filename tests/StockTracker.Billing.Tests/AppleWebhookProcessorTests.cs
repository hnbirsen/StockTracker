using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Billing.Entities;
using StockTracker.Billing.Services;

namespace StockTracker.Billing.Tests;

public class AppleWebhookProcessorTests
{
    private readonly Mock<IPaymentEventProcessor> _paymentEventProcessor = new();

    private AppleWebhookProcessor CreateSut() => new(
        new AppleJwsVerifier(Mock.Of<ILogger<AppleJwsVerifier>>()),
        _paymentEventProcessor.Object,
        Mock.Of<ILogger<AppleWebhookProcessor>>());

    private static string BuildOuterEnvelope(string notificationType, string? subtype, string innerJws) =>
        TestJwsBuilder.CreateSignedJws(new
        {
            notificationType,
            subtype,
            notificationUUID = "uuid-1",
            data = new { signedTransactionInfo = innerJws }
        }, out _);

    [Fact]
    public async Task ProcessAsync_WithInvalidOuterSignature_ReturnsFalseAndDoesNotCallProcessor()
    {
        var sut = CreateSut();
        var result = await sut.ProcessAsync("not-a-jws", CancellationToken.None);

        result.Should().BeFalse();
        _paymentEventProcessor.Verify(p => p.ProcessAsync(
            It.IsAny<Platform>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<SubscriptionStatus>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithSubscribedNotification_MapsToActiveAndCallsProcessor()
    {
        var innerJws = TestJwsBuilder.CreateSignedJws(new
        {
            originalTransactionId = "orig-1",
            productId = "premium_monthly",
            expiresDate = DateTimeOffset.UtcNow.AddMonths(1).ToUnixTimeMilliseconds()
        }, out _);
        var outer = BuildOuterEnvelope("SUBSCRIBED", null, innerJws);

        var sut = CreateSut();
        var result = await sut.ProcessAsync(outer, CancellationToken.None);

        result.Should().BeTrue();
        _paymentEventProcessor.Verify(p => p.ProcessAsync(
            Platform.Apple, "uuid-1", "SUBSCRIBED", outer, "orig-1", SubscriptionStatus.Active, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithRefundNotification_MapsToRefunded()
    {
        var innerJws = TestJwsBuilder.CreateSignedJws(new { originalTransactionId = "orig-1", productId = "premium_monthly" }, out _);
        var outer = BuildOuterEnvelope("REFUND", null, innerJws);

        var sut = CreateSut();
        await sut.ProcessAsync(outer, CancellationToken.None);

        _paymentEventProcessor.Verify(p => p.ProcessAsync(
            Platform.Apple, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            "orig-1", SubscriptionStatus.Refunded, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithIrrelevantNotificationType_ReturnsTrueButSkipsProcessing()
    {
        var innerJws = TestJwsBuilder.CreateSignedJws(new { originalTransactionId = "orig-1", productId = "premium_monthly" }, out _);
        var outer = BuildOuterEnvelope("DID_CHANGE_RENEWAL_STATUS", "AUTO_RENEW_ENABLED", innerJws);

        var sut = CreateSut();
        var result = await sut.ProcessAsync(outer, CancellationToken.None);

        result.Should().BeTrue();
        _paymentEventProcessor.Verify(p => p.ProcessAsync(
            It.IsAny<Platform>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<SubscriptionStatus>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
