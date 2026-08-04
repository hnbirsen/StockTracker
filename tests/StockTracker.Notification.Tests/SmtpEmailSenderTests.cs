using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StockTracker.Notification.Configuration;
using StockTracker.Notification.Services;

namespace StockTracker.Notification.Tests;

public class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendAsync_WhenHostNotConfigured_ReturnsFalseWithoutThrowing()
    {
        var sut = new SmtpEmailSender(Options.Create(new SmtpSettings { Host = "REPLACE_WITH_ENV" }), Mock.Of<ILogger<SmtpEmailSender>>());

        var result = await sut.SendAsync("user@example.com", "Subject", "Body");

        result.Should().BeFalse();
    }
}
