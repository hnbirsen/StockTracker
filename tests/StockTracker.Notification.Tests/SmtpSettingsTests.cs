using FluentAssertions;
using StockTracker.Notification.Configuration;

namespace StockTracker.Notification.Tests;

public class SmtpSettingsTests
{
    [Fact]
    public void IsConfigured_WhenHostIsReplaceWithEnvPlaceholder_ReturnsFalse()
    {
        var settings = new SmtpSettings { Host = "REPLACE_WITH_ENV" };

        settings.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_WhenHostIsNull_ReturnsFalse()
    {
        var settings = new SmtpSettings { Host = null };

        settings.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_WhenHostIsSet_ReturnsTrue()
    {
        var settings = new SmtpSettings { Host = "smtp.example.com" };

        settings.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_DoesNotRequireUsernameOrPassword_SomeRelaysAreUnauthenticated()
    {
        var settings = new SmtpSettings { Host = "smtp.example.com", Username = null, Password = null };

        settings.IsConfigured.Should().BeTrue();
    }
}
