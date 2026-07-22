using Microsoft.EntityFrameworkCore;

public interface IAuthService
{
    Task<AuthResponse?> RegisterAsync(RegisterRequest request);
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<AuthResponse?> RefreshTokenAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken);
}

public class AuthService : IAuthService
{
    private readonly IdentityDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;

    public AuthService(IdentityDbContext dbContext, ITokenService tokenService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _configuration = configuration;
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        var normalizedEmail = request.Email.ToLower().Trim();

        var isEmailTaken = _dbContext.Users.Any(u => u.Email == normalizedEmail);
        if (isEmailTaken)
        {
            return null; // TODO : Handle email already taken scenario, return 409 Conflict or a custom error response
        }

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());
        if (user is null)
            return null;

        var isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!isPasswordValid)
            return null;

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse?> RefreshTokenAsync(string refreshToken)
    {
        var expiryDays = int.Parse(_tokenService.GetJwtSettings().RefreshTokenExpiryDays);

        var storedToken = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (storedToken is null || storedToken.IsRevoked || storedToken.ExpiresAt < DateTime.UtcNow)
            // token iptal edilmiş veya süresi dolmuş
            return null;

        storedToken.IsRevoked = true;

        return await GenerateAuthResponseAsync(storedToken.User);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (storedToken is null || storedToken.IsRevoked)
            return;

        storedToken.IsRevoked = true;
        await _dbContext.SaveChangesAsync();
    }

    private async Task<AuthResponse> GenerateAuthResponseAsync(User user)
    {
        var jwtSettings = _tokenService.GetJwtSettings();
        var expiryMinutes = int.Parse(jwtSettings.AccessTokenExpiryMinutes);
        var expiryDays = int.Parse(jwtSettings.RefreshTokenExpiryDays);

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays)
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshTokenValue,
            AccessTokenExpiresAt: DateTime.UtcNow.AddMinutes(expiryMinutes),
            User: new UserDto(user.Id, user.Email, user.FirstName, user.LastName, user.IsEmailVerified)
        );
    }
}