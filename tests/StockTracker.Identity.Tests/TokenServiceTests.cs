using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using StockTracker.Identity.Entities;
using StockTracker.Identity.Services;

namespace StockTracker.Identity.Tests;

// JWT_* secret'lar env variable'dan okunuyor (bkz. TokenService.GetJwtSettings) —
// bu yüzden her testten önce/sonra process env'i kaydedip geri yüklüyoruz.
public class TokenServiceTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalEnv = new();

    public TokenServiceTests()
    {
        SaveAndSet("JWT_SECRET_KEY", "test-secret-key-please-be-at-least-32-chars-long");
        SaveAndSet("JWT_ISSUER", "StockTracker.Tests");
        SaveAndSet("JWT_AUDIENCE", "StockTracker.Tests.Clients");
    }

    public void Dispose()
    {
        foreach (var (key, value) in _originalEnv)
            Environment.SetEnvironmentVariable(key, value);
        GC.SuppressFinalize(this);
    }

    private void SaveAndSet(string key, string value)
    {
        _originalEnv[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    private static IConfiguration BuildConfiguration(string accessExpiry = "15", string refreshExpiry = "7") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:AccessTokenExpiryMinutes"] = accessExpiry,
                ["JwtSettings:RefreshTokenExpiryDays"] = refreshExpiry
            })
            .Build();

    [Fact]
    public void GenerateAccessToken_ReturnsJwtWithExpectedClaimsAndIssuer()
    {
        var tokenService = new TokenService(BuildConfiguration());
        var user = new User { Id = Guid.NewGuid(), Email = "user@example.com" };

        var token = tokenService.GenerateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Issuer.Should().Be("StockTracker.Tests");
        jwt.Audiences.Should().Contain("StockTracker.Tests.Clients");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsDifferentValueOnEachCall()
    {
        var tokenService = new TokenService(BuildConfiguration());

        var token1 = tokenService.GenerateRefreshToken();
        var token2 = tokenService.GenerateRefreshToken();

        token1.Should().NotBe(token2);
        token1.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Constructor_WhenJwtSecretKeyMissing_ThrowsInvalidOperationException()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET_KEY", null);

        var act = () => new TokenService(BuildConfiguration());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_WhenAccessTokenExpiryNotConfigured_ThrowsInvalidOperationException()
    {
        var configWithoutExpiry = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:RefreshTokenExpiryDays"] = "7"
            })
            .Build();

        var act = () => new TokenService(configWithoutExpiry);

        act.Should().Throw<InvalidOperationException>();
    }
}
