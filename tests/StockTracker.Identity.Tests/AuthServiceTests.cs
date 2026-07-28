using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace StockTracker.Identity.Tests;

public class AuthServiceTests
{
    private static IdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Mock<ITokenService> CreateTokenServiceMock()
    {
        var mock = new Mock<ITokenService>();
        mock.Setup(t => t.GetJwtSettings()).Returns(new TokenService.JwtSettings(
            SecretKey: "test-secret",
            Issuer: "test-issuer",
            Audience: "test-audience",
            AccessTokenExpiryMinutes: "15",
            RefreshTokenExpiryDays: "7"
        ));
        mock.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("fake-access-token");
        mock.Setup(t => t.GenerateRefreshToken()).Returns(() => Guid.NewGuid().ToString());
        return mock;
    }

    private static AuthService CreateSut(IdentityDbContext db, Mock<ITokenService>? tokenService = null) =>
        new(db, (tokenService ?? CreateTokenServiceMock()).Object, Mock.Of<IConfiguration>());

    [Fact]
    public async Task RegisterAsync_WhenEmailNotTaken_CreatesUserAndReturnsAuthResponse()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var request = new RegisterRequest("user@example.com", "password123", "Jane", "Doe");

        var response = await sut.RegisterAsync(request);

        response.Should().NotBeNull();
        response!.User.Email.Should().Be("user@example.com");
        response.AccessToken.Should().Be("fake-access-token");
        (await db.Users.CountAsync()).Should().Be(1);
        (await db.RefreshTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RegisterAsync_NormalizesEmailCasingAndWhitespace()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        var request = new RegisterRequest("  User@Example.com  ", "password123", null, null);

        await sut.RegisterAsync(request);

        var savedUser = await db.Users.SingleAsync();
        savedUser.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailAlreadyTaken_ReturnsNull()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Email = "user@example.com", PasswordHash = "hash" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var response = await sut.RegisterAsync(new RegisterRequest("user@example.com", "password123", null, null));

        response.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsAuthResponse()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password")
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var response = await sut.LoginAsync(new LoginRequest("user@example.com", "correct-password"));

        response.Should().NotBeNull();
        response!.User.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsNull()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Email = "user@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password")
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var response = await sut.LoginAsync(new LoginRequest("user@example.com", "wrong-password"));

        response.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var response = await sut.LoginAsync(new LoginRequest("nobody@example.com", "whatever"));

        response.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidToken_RevokesOldTokenAndReturnsNewAuthResponse()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "user@example.com", PasswordHash = "hash" };
        var oldToken = new RefreshToken { User = user, UserId = user.Id, Token = "old-token", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        db.Users.Add(user);
        db.RefreshTokens.Add(oldToken);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var response = await sut.RefreshTokenAsync("old-token");

        response.Should().NotBeNull();
        var reloaded = await db.RefreshTokens.FirstAsync(rt => rt.Token == "old-token");
        reloaded.IsRevoked.Should().BeTrue();
        (await db.RefreshTokens.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithExpiredToken_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "user@example.com", PasswordHash = "hash" };
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken { User = user, UserId = user.Id, Token = "expired-token", ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var response = await sut.RefreshTokenAsync("expired-token");

        response.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithRevokedToken_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "user@example.com", PasswordHash = "hash" };
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken { User = user, UserId = user.Id, Token = "revoked-token", ExpiresAt = DateTime.UtcNow.AddDays(1), IsRevoked = true });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var response = await sut.RefreshTokenAsync("revoked-token");

        response.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithUnknownToken_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var response = await sut.RefreshTokenAsync("does-not-exist");

        response.Should().BeNull();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_MarksTokenAsRevoked()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "user@example.com", PasswordHash = "hash" };
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken { User = user, UserId = user.Id, Token = "logout-token", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.RevokeRefreshTokenAsync("logout-token");

        var reloaded = await db.RefreshTokens.FirstAsync(rt => rt.Token == "logout-token");
        reloaded.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_WhenTokenNotFound_DoesNotThrow()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var act = async () => await sut.RevokeRefreshTokenAsync("does-not-exist");

        await act.Should().NotThrowAsync();
    }
}
