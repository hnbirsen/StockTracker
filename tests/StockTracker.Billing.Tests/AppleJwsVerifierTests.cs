using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Billing.Services;

namespace StockTracker.Billing.Tests;

public class AppleJwsVerifierTests
{
    private static AppleJwsVerifier CreateSut() => new(Mock.Of<ILogger<AppleJwsVerifier>>());

    [Fact]
    public void TryVerifyAndDecode_WithValidSignature_ReturnsTrueAndDecodesPayload()
    {
        var jws = TestJwsBuilder.CreateSignedJws(new { originalTransactionId = "1000000123", productId = "premium_monthly" }, out _);

        var sut = CreateSut();
        var result = sut.TryVerifyAndDecode(jws, out var payload);

        result.Should().BeTrue();
        payload.GetProperty("originalTransactionId").GetString().Should().Be("1000000123");
    }

    [Fact]
    public void TryVerifyAndDecode_WhenPayloadTamperedAfterSigning_ReturnsFalse()
    {
        var jws = TestJwsBuilder.CreateSignedJws(new { originalTransactionId = "1000000123" }, out _);
        var parts = jws.Split('.');
        // Payload segmentini imzadan sonra değiştir — signature artık eşleşmemeli.
        var tamperedPayload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"originalTransactionId\":\"HACKED\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tamperedJws = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var sut = CreateSut();
        var result = sut.TryVerifyAndDecode(tamperedJws, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryVerifyAndDecode_WithMalformedToken_ReturnsFalse()
    {
        var sut = CreateSut();
        var result = sut.TryVerifyAndDecode("not-a-jws", out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryVerifyAndDecode_WithMissingX5cHeader_ReturnsFalse()
    {
        var headerB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"ES256\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payloadB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jws = $"{headerB64}.{payloadB64}.fakesignature";

        var sut = CreateSut();
        var result = sut.TryVerifyAndDecode(jws, out _);

        result.Should().BeFalse();
    }
}
