using FluentAssertions;
using StockTracker.Billing.Configuration;

namespace StockTracker.Billing.Tests;

// .env'de değer girilmemiş alanlar "REPLACE_WITH_ENV" placeholder'ıyla kalır (bkz. appsettings.json
// konvansiyonu) — IsConfigured bunu "yapılandırılmış" saymamalı, aksi halde AppleAppStoreServerClient/
// GooglePlayDeveloperClient gerçek olmayan bir anahtarla çağrı yapmaya çalışıp patlar (canlıda bulunup
// düzeltilen gerçek bir hata — bkz. .claude/ARCHITECTURE.md > Billing).
public class SettingsConfigurationTests
{
    [Fact]
    public void AppleStoreSettings_WithReplaceWithEnvPlaceholders_IsNotConfigured()
    {
        var settings = new AppleStoreSettings
        {
            IssuerId = "REPLACE_WITH_ENV",
            KeyId = "REPLACE_WITH_ENV",
            PrivateKeyBase64 = "REPLACE_WITH_ENV",
            BundleId = "REPLACE_WITH_ENV"
        };

        settings.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void AppleStoreSettings_WithAllRealValues_IsConfigured()
    {
        var settings = new AppleStoreSettings
        {
            IssuerId = "issuer-1",
            KeyId = "key-1",
            PrivateKeyBase64 = "cGVt",
            BundleId = "com.stocktracker.app"
        };

        settings.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void AppleStoreSettings_WithOneMissingField_IsNotConfigured()
    {
        var settings = new AppleStoreSettings
        {
            IssuerId = "issuer-1",
            KeyId = "key-1",
            PrivateKeyBase64 = "cGVt",
            BundleId = null
        };

        settings.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void GooglePlaySettings_WithReplaceWithEnvPlaceholders_IsNotConfigured()
    {
        var settings = new GooglePlaySettings
        {
            ServiceAccountJsonBase64 = "REPLACE_WITH_ENV",
            PackageName = "REPLACE_WITH_ENV"
        };

        settings.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void GooglePlaySettings_WithRealValues_IsConfigured()
    {
        var settings = new GooglePlaySettings
        {
            ServiceAccountJsonBase64 = "eyJ9",
            PackageName = "com.stocktracker.app"
        };

        settings.IsConfigured.Should().BeTrue();
    }
}
