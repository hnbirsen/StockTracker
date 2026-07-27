using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    TokenService.JwtSettings GetJwtSettings();
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private JwtSettings _jwtSettings;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
        GetJwtSettings();
    }

    public string GenerateAccessToken(User user)
    {
        var secretKey = _jwtSettings.SecretKey ?? throw new InvalidOperationException("JWT secret key is not configured.");
        var issuer = _jwtSettings.Issuer ?? throw new InvalidOperationException("JWT issuer is not configured.");
        var audience = _jwtSettings.Audience ?? throw new InvalidOperationException("JWT audience is not configured.");
        var expiryMinutes = double.Parse(_jwtSettings.AccessTokenExpiryMinutes ?? throw new InvalidOperationException("JWT access token expiry is not configured."));

        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public record JwtSettings
    (
        string SecretKey,
        string Issuer,
        string Audience,
        string AccessTokenExpiryMinutes,
        string RefreshTokenExpiryDays
    );

    public JwtSettings GetJwtSettings()
    {
        if (_jwtSettings != null)
            return _jwtSettings;

        // Sensitive veriler env'dan
        var secretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY") 
            ?? throw new InvalidOperationException("JWT_SECRET_KEY environment variable not set.");
        var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") 
            ?? throw new InvalidOperationException("JWT_ISSUER environment variable not set.");
        var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") 
            ?? throw new InvalidOperationException("JWT_AUDIENCE environment variable not set.");

        // Configuration değerleri appsettings.json'dan
        var accessTokenExpiry = _configuration["JwtSettings:AccessTokenExpiryMinutes"]
            ?? throw new InvalidOperationException("JWT access token expiry is not configured.");
        var refreshTokenExpiry = _configuration["JwtSettings:RefreshTokenExpiryDays"]
            ?? throw new InvalidOperationException("JWT refresh token expiry is not configured.");

        _jwtSettings = new JwtSettings(
            SecretKey: secretKey,
            Issuer: issuer,
            Audience: audience,
            AccessTokenExpiryMinutes: accessTokenExpiry,
            RefreshTokenExpiryDays: refreshTokenExpiry
        );

        return _jwtSettings;
    }
}